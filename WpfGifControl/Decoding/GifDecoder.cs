using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using WpfGifControl.AvaloniaBase;
using WpfGifControl.Exceptions;
using WpfGifControl.Extensions;
using static WpfGifControl.Extensions.StreamExtensions;


namespace WpfGifControl.Decoding;

public sealed class GifDecoder : IDisposable
{
	private static readonly ReadOnlyMemory<byte> Sg87AMagic
			= "GIF87a"u8.ToArray().AsMemory();

	private static readonly ReadOnlyMemory<byte> Sg89AMagic
			= "GIF89a"u8.ToArray().AsMemory();

	private static readonly ReadOnlyMemory<byte> SNetscapeMagic
			= "NETSCAPE2.0"u8.ToArray().AsMemory();

	private static readonly (int Start, int Step)[] SPass = [(0, 8), (4, 8), (2, 4), (1, 2)];
	private static readonly TimeSpan SFrameDelayThreshold = TimeSpan.FromMilliseconds(10);
	private static readonly TimeSpan SFrameDelayDefault = TimeSpan.FromMilliseconds(100);
	private static readonly GifColor STransparentColor = new(0, 0, 0, 0);
	private const int MaxTempBuf = 768;
	private const int MaxStackSize = 4096;
	private const int MaxBits = 4097;

	private readonly Stream _fileStream;
	private readonly CancellationToken _currentCtsToken;
	private readonly bool _hasFrameBackups;
	private readonly int _backBufferBytes;

	private int _gctSize, _prevFrame = -1, _backupFrame = -1;
	private bool _gctUsed;
	private GifRect _gifDimensions;
	private GifColor[]? _bitmapBackBuffer;
	private short[]? _prefixBuf;
	private byte[]? _suffixBuf;
	private byte[]? _pixelStack;
	private byte[]? _indexBuf;
	private byte[]? _backupFrameIndexBuf;
	private volatile bool _hasNewFrame;

	internal readonly List<GifFrame> Frames = [];

	public GifHeader? Header { get; private set; }
	public PixelSize Size => new(Header?.Dimensions.Width ?? 0, Header?.Dimensions.Height ?? 0);

	public GifDecoder(Stream fileStream, CancellationToken currentCtsToken)
	{
		_fileStream = fileStream;
		_currentCtsToken = currentCtsToken;

		ProcessHeaderData();
		ProcessFrameData();

		if (Header != null)
			Header.IterationCount = Header.Iterations switch
			{
					-1 => new GifRepeatBehavior { Count = 1 },
					0 => new GifRepeatBehavior { LoopForever = true },
					> 0 => new GifRepeatBehavior { Count = Header.Iterations },
					_ => Header.IterationCount
			};

		var pixelCount = _gifDimensions.TotalPixels;
		_hasFrameBackups = Frames
				.Any(f => f.FrameDisposalMethod == FrameDisposal.Restore);

		_bitmapBackBuffer = new GifColor[pixelCount];
		_indexBuf = new byte[pixelCount];

		if (_hasFrameBackups)
			_backupFrameIndexBuf = new byte[pixelCount];

		_prefixBuf = new short[MaxStackSize];
		_suffixBuf = new byte[MaxStackSize];
		_pixelStack = new byte[MaxStackSize + 1];
		_backBufferBytes = pixelCount * Marshal.SizeOf(typeof(GifColor));
	}

	public void Dispose()
	{
		Frames.Clear();

		_bitmapBackBuffer = null;
		_prefixBuf = null;
		_suffixBuf = null;
		_pixelStack = null;
		_indexBuf = null;
		_backupFrameIndexBuf = null;
	}


	public void RenderFrame(int fIndex, WriteableBitmap? writeableBitmap, bool forceClear = false)
	{
		if (_currentCtsToken.IsCancellationRequested)
			return;

		if ((fIndex < 0) | (fIndex >= Frames.Count))
			return;

		if (_prevFrame == fIndex)
			return;

		// Ensure bitmap operations are on UI thread
		if (writeableBitmap != null && !writeableBitmap.Dispatcher.CheckAccess())
		{
			writeableBitmap.Dispatcher.InvokeAsync(() => RenderFrame(fIndex, writeableBitmap, forceClear));
			return;
		}

		if (fIndex == 0 || forceClear || fIndex < _prevFrame)
			ClearImage();

		DisposePreviousFrame();

		_prevFrame++;

		// render intermediate frames
		for (var idx = _prevFrame; idx < fIndex; ++idx)
		{
			var prevFrame = Frames[idx];

			if (prevFrame.FrameDisposalMethod == FrameDisposal.Restore)
				continue;

			if (prevFrame.FrameDisposalMethod == FrameDisposal.Background)
			{
				ClearArea(prevFrame.Dimensions);
				continue;
			}

			RenderFrameAt(idx, writeableBitmap);
		}

		RenderFrameAt(fIndex, writeableBitmap);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int PixelCoordinate(int x, int y)
	{
		return x + y * _gifDimensions.Width;
	}

	private void ClearImage()
	{
		if (_bitmapBackBuffer != null)
			Array.Fill(_bitmapBackBuffer, STransparentColor);

		_prevFrame = -1;
		_backupFrame = -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void RenderFrameAt(int idx, WriteableBitmap? writeableBitmap)
	{
		writeableBitmap?.Dispatcher.InvokeAsync(() =>
		{
			if (Frames.Count == 0)
				return;

			var tmpB = ArrayPool<byte>.Shared.Rent(MaxTempBuf);

			try
			{
				var curFrame = Frames[idx];

				DecompressFrameToIndexBuffer(curFrame, _indexBuf, tmpB);

				if (_hasFrameBackups & curFrame.ShouldBackup
				    && _indexBuf != null && _backupFrameIndexBuf != null)
				{
					Buffer.BlockCopy(_indexBuf, 0,
							_backupFrameIndexBuf, 0,
							curFrame.Dimensions.TotalPixels);
					_backupFrame = idx;
				}

				DrawFrame(curFrame, _indexBuf);

				_prevFrame = idx;
				_hasNewFrame = true;

				// Ensure we're on the UI thread for bitmap operations
				if (writeableBitmap.Dispatcher.CheckAccess())
				{
					writeableBitmap.Lock();
					try
					{
						WriteBackBufToFb(writeableBitmap.BackBuffer);
						writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, writeableBitmap.PixelWidth,
								writeableBitmap.PixelHeight));
					}
					finally
					{
						writeableBitmap.Unlock();
					}
				}
				else
				{
					// Marshal to UI thread
					writeableBitmap.Dispatcher.Invoke(() =>
					{
						writeableBitmap.Lock();
						try
						{
							WriteBackBufToFb(writeableBitmap.BackBuffer);
							writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, writeableBitmap.PixelWidth,
									writeableBitmap.PixelHeight));
						}
						finally
						{
							writeableBitmap.Unlock();
						}
					});
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(tmpB);
			}
		});
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DrawFrame(GifFrame curFrame, Memory<byte> frameIndexSpan)
	{
		var activeColorTable =
				curFrame.IsLocalColorTableUsed ? curFrame.LocalColorTable : Header?.GlobalColorTable;

		var cX = curFrame.Dimensions.X;
		var cY = curFrame.Dimensions.Y;
		var cH = curFrame.Dimensions.Height;
		var cW = curFrame.Dimensions.Width;
		var tC = curFrame.TransparentColorIndex;
		var hT = curFrame.HasTransparency;

		if (curFrame.IsInterlaced)
		{
			var curSrcRow = 0;
			for (var i = 0; i < 4; i++)
			{
				var curPass = SPass[i];
				var y = curPass.Start;
				while (y < cH)
				{
					DrawRow(curSrcRow++, y);
					y += curPass.Step;
				}
			}
		}
		else
		{
			for (var i = 0; i < cH; i++)
				DrawRow(i, i);
		}

		return;

		void DrawRow(int srcRow, int destRow)
		{
			// Get the starting point of the current row on frame's index stream.
			var indexOffset = srcRow * cW;

			// Get the target back buffer offset from the frames coords.
			var targetOffset = PixelCoordinate(cX, destRow + cY);
			if (_bitmapBackBuffer == null) return;
			var len = _bitmapBackBuffer.Length;

			for (var i = 0; i < cW; i++)
			{
				var indexColor = frameIndexSpan.Span[indexOffset + i];

				if (activeColorTable == null || targetOffset >= len ||
				    indexColor > activeColorTable.Length) return;

				if (!(hT & (indexColor == tC)))
					_bitmapBackBuffer[targetOffset] = activeColorTable[indexColor];

				targetOffset++;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DisposePreviousFrame()
	{
		if (_prevFrame == -1)
			return;

		var prevFrame = Frames[_prevFrame];

		switch (prevFrame.FrameDisposalMethod)
		{
			case FrameDisposal.Background:
				ClearArea(prevFrame.Dimensions);
				break;
			case FrameDisposal.Restore:
				if (_hasFrameBackups && _backupFrame != -1)
					DrawFrame(Frames[_backupFrame], _backupFrameIndexBuf);
				else
					ClearArea(prevFrame.Dimensions);
				break;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ClearArea(GifRect area)
	{
		if (_bitmapBackBuffer is null) return;

		for (var y = 0; y < area.Height; y++)
		{
			var targetOffset = PixelCoordinate(area.X, y + area.Y);
			for (var x = 0; x < area.Width; x++)
				_bitmapBackBuffer[targetOffset + x] = STransparentColor;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DecompressFrameToIndexBuffer(GifFrame curFrame, Span<byte> indexSpan, byte[] tempBuf)
	{
		if (_prefixBuf is null || _suffixBuf is null || _pixelStack is null)
			return;

		try
		{
			_fileStream.Position = curFrame.LzwStreamPosition;
			var totalPixels = curFrame.Dimensions.TotalPixels;

			// Clear the index buffer first
			indexSpan.Clear();

			// Initialize GIF data stream decoder.
			var dataSize = curFrame.LzwMinCodeSize;
			var clear = 1 << dataSize;
			var endOfInformation = clear + 1;
			var available = clear + 2;
			var oldCode = -1;
			var codeSize = dataSize + 1;
			var codeMask = (1 << codeSize) - 1;

			for (var code = 0; code < clear; code++)
			{
				_prefixBuf[code] = 0;
				_suffixBuf[code] = (byte)code;
			}

			// Decode GIF pixel stream.
			int bits, first, top, pixelIndex;
			var datum = bits = first = top = pixelIndex = 0;

			while (pixelIndex < totalPixels)
			{
				if (_currentCtsToken.IsCancellationRequested)
					break;

				var blockSize = _fileStream.ReadBlock(tempBuf);

				if (blockSize == 0)
					break;

				var blockPos = 0;

				while (blockPos < blockSize && pixelIndex < totalPixels)
				{
					datum += tempBuf[blockPos] << bits;
					blockPos++;

					bits += 8;

					while (bits >= codeSize && pixelIndex < totalPixels)
					{
						// Get the next code.
						var code = datum & codeMask;
						datum >>= codeSize;
						bits -= codeSize;

						// Interpret the code
						if (code == clear)
						{
							// Reset decoder.
							codeSize = dataSize + 1;
							codeMask = (1 << codeSize) - 1;
							available = clear + 2;
							oldCode = -1;
							continue;
						}

						// Check for explicit end-of-stream
						if (code == endOfInformation)
							return;

						if (oldCode == -1)
						{
							if (pixelIndex < indexSpan.Length)
								indexSpan[pixelIndex++] = _suffixBuf[code];
							oldCode = code;
							first = code;
							continue;
						}

						var inCode = code;
						if (code >= available)
						{
							if (top < _pixelStack.Length)
								_pixelStack[top++] = (byte)first;
							code = oldCode;

							if (top >= MaxBits)
								ThrowLswException();
						}

						while (code >= clear)
						{
							if (code >= MaxBits || code == _prefixBuf[code])
								ThrowLswException();

							if (top < _pixelStack.Length)
								_pixelStack[top++] = _suffixBuf[code];
							code = _prefixBuf[code];

							if (top >= MaxBits)
								ThrowLswException();
						}

						first = _suffixBuf[code];
						if (top < _pixelStack.Length)
							_pixelStack[top++] = (byte)first;

						// Add new code to the dictionary
						if (available < MaxStackSize)
						{
							_prefixBuf[available] = (short)oldCode;
							_suffixBuf[available] = (byte)first;
							available++;

							if ((available & codeMask) == 0 && available < MaxStackSize)
							{
								codeSize++;
								codeMask += available;
							}
						}

						oldCode = inCode;

						// Drain the pixel stack.
						while (top > 0 && pixelIndex < indexSpan.Length) indexSpan[pixelIndex++] = _pixelStack[--top];
					}
				}
			}

			// Fill remaining pixels with transparent color
			while (pixelIndex < totalPixels && pixelIndex < indexSpan.Length)
				indexSpan[pixelIndex++] = 0;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Error decompressing frame: {ex.Message}");
			// Fill with transparent pixels on error
			indexSpan.Clear();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ThrowLswException()
	{
		throw new LzwDecompressionException();
	}

	/// <summary>
	/// Directly copies the <see cref="GifColor"/> struct array to a bitmap IntPtr.
	/// </summary>
	// Fixed WriteBackBufToFb method with proper error handling
	private void WriteBackBufToFb(IntPtr targetPointer)
	{
		if (_currentCtsToken.IsCancellationRequested)
			return;

		if (!(_hasNewFrame && _bitmapBackBuffer != null))
			return;

		if (targetPointer == IntPtr.Zero)
			return;

		try
		{
			unsafe
			{
				fixed (void* src = &_bitmapBackBuffer[0])
				{
					Buffer.MemoryCopy(src, targetPointer.ToPointer(),
							(uint)_backBufferBytes, (uint)_backBufferBytes);
				}
			}

			_hasNewFrame = false;
		}
		catch (Exception ex)
		{
			// Log the exception or handle it appropriately
			System.Diagnostics.Debug.WriteLine($"Error writing to bitmap: {ex.Message}");
		}
	}

	/// <summary>
	/// Processes GIF Header.
	/// </summary>
	private void ProcessHeaderData()
	{
		var str = _fileStream;
		var tmpB = ArrayPool<byte>.Shared.Rent(MaxTempBuf);
		var tempBuf = tmpB.AsSpan();
		_ = str.Read(tmpB, 0, 6);

		if (!tempBuf[..3].SequenceEqual(Sg87AMagic[..3].Span))
			throw new InvalidGifStreamException("Not a GIF stream.");

		if (!(tempBuf[..6].SequenceEqual(Sg87AMagic.Span) | tempBuf[..6].SequenceEqual(Sg89AMagic.Span)))
			throw new InvalidGifStreamException("Unsupported GIF Version: " +
			                                    Encoding.ASCII.GetString(tempBuf[..6].ToArray()));

		ProcessScreenDescriptor(tmpB);
		Header = new GifHeader
		{
				Dimensions = _gifDimensions,
				GlobalColorTable =
						_gctUsed ? ProcessColorTable(ref str, tmpB, _gctSize) : [],
				HeaderSize = _fileStream.Position
		};

		ArrayPool<byte>.Shared.Return(tmpB);
	}

	/// <summary>
	/// Parses colors from file stream to target color table.
	/// </summary>
	private static GifColor[] ProcessColorTable(ref Stream stream, byte[] rawBufSpan, int nColors)
	{
		var nBytes = 3 * nColors;
		var target = new GifColor[nColors];

		var n = stream.Read(rawBufSpan, 0, nBytes);

		if (n < nBytes)
			throw new InvalidOperationException("Wrong color table bytes.");

		int i = 0, j = 0;

		while (i < nColors)
		{
			var r = rawBufSpan[j++];
			var g = rawBufSpan[j++];
			var b = rawBufSpan[j++];
			target[i++] = new GifColor(r, g, b);
		}

		return target;
	}

	/// <summary>
	/// Parses screen and other GIF descriptors.
	/// </summary>
	private void ProcessScreenDescriptor(byte[] tempBuf)
	{
		var width = _fileStream.ReadUShortS(tempBuf);
		var height = _fileStream.ReadUShortS(tempBuf);
		var packed = _fileStream.ReadByteS(tempBuf);

		_gctUsed = (packed & 0x80) != 0;
		_gctSize = 2 << (packed & 7);
		_ = _fileStream.ReadByteS(tempBuf);

		_gifDimensions = new GifRect(0, 0, width, height);
		_fileStream.Skip(1);
	}

	/// <summary>
	/// Parses all frame data.
	/// </summary>
	private void ProcessFrameData()
	{
		_fileStream.Position = Header?.HeaderSize ?? -1;

		var tempBuf = ArrayPool<byte>.Shared.Rent(MaxTempBuf);
		var terminate = false;
		var curFrame = 0;
		Frames.Add(new GifFrame());

		do
		{
			var blockType = (BlockTypes)_fileStream.ReadByteS(tempBuf);

			switch (blockType)
			{
				case BlockTypes.Empty:
					break;

				case BlockTypes.Extension:
					ProcessExtensions(ref curFrame, tempBuf);
					break;

				case BlockTypes.ImageDescriptor:
					ProcessImageDescriptor(ref curFrame, tempBuf);
					_fileStream.SkipBlocks(tempBuf);
					break;

				case BlockTypes.Trailer:
					Frames.RemoveAt(Frames.Count - 1);
					terminate = true;
					break;

				default:
					_fileStream.SkipBlocks(tempBuf);
					break;
			}

			// Break the loop when the stream is not valid anymore.
			if ((_fileStream.Position >= _fileStream.Length) & (terminate == false))
				throw new InvalidProgramException("Reach the end of the filestream without trailer block.");
		} while (!terminate);

		ArrayPool<byte>.Shared.Return(tempBuf);
	}

	/// <summary>
	/// Parses GIF Image Descriptor Block.
	/// </summary>
	private void ProcessImageDescriptor(ref int curFrame, byte[] tempBuf)
	{
		var str = _fileStream;
		var currentFrame = Frames[curFrame];

		// Parse frame dimensions.
		var frameX = str.ReadUShortS(tempBuf);
		var frameY = str.ReadUShortS(tempBuf);
		var frameW = str.ReadUShortS(tempBuf);
		var frameH = str.ReadUShortS(tempBuf);

		frameW = (ushort)Math.Min(frameW, _gifDimensions.Width - frameX);
		frameH = (ushort)Math.Min(frameH, _gifDimensions.Height - frameY);

		currentFrame.Dimensions = new GifRect(frameX, frameY, frameW, frameH);

		// Unpack interlace and lct info.
		var packed = str.ReadByteS(tempBuf);
		currentFrame.IsInterlaced = (packed & 0x40) != 0;
		currentFrame.IsLocalColorTableUsed = (packed & 0x80) != 0;
		currentFrame.LocalColorTableSize = (int)Math.Pow(2, (packed & 0x07) + 1);

		if (currentFrame.IsLocalColorTableUsed)
			currentFrame.LocalColorTable =
					ProcessColorTable(ref str, tempBuf, currentFrame.LocalColorTableSize);

		currentFrame.LzwMinCodeSize = str.ReadByteS(tempBuf);
		currentFrame.LzwStreamPosition = str.Position;

		curFrame += 1;
		Frames.Add(new GifFrame());
	}

	/// <summary>
	/// Parses GIF Extension Blocks.
	/// </summary>
	private void ProcessExtensions(ref int curFrame, byte[] tempBuf)
	{
		var extType = (ExtensionType)_fileStream.ReadByteS(tempBuf);

		switch (extType)
		{
			case ExtensionType.GraphicsControl:

				_fileStream.ReadBlock(tempBuf);
				var currentFrame = Frames[curFrame];
				var packed = tempBuf[0];

				currentFrame.FrameDisposalMethod = (FrameDisposal)((packed & 0x1c) >> 2);

				if (currentFrame.FrameDisposalMethod != FrameDisposal.Restore
				    && currentFrame.FrameDisposalMethod != FrameDisposal.Background)
					currentFrame.ShouldBackup = true;

				currentFrame.HasTransparency = (packed & 1) != 0;

				currentFrame.FrameDelay =
						TimeSpan.FromMilliseconds(SpanToShort(tempBuf.AsSpan(1)) * 10);

				if (currentFrame.FrameDelay <= SFrameDelayThreshold)
					currentFrame.FrameDelay = SFrameDelayDefault;

				currentFrame.TransparentColorIndex = tempBuf[3];
				break;

			case ExtensionType.Application:
				var blockLen = _fileStream.ReadBlock(tempBuf);
				var _ = tempBuf.AsSpan(0, blockLen);
				var blockHeader = tempBuf.AsSpan(0, SNetscapeMagic.Length);

				if (blockHeader.SequenceEqual(SNetscapeMagic.Span))
				{
					var count = 1;

					while (count > 0)
						count = _fileStream.ReadBlock(tempBuf);

					var iterationCount = SpanToShort(tempBuf.AsSpan(1));

					if (Header != null)
						Header.Iterations = iterationCount;
				}
				else
				{
					_fileStream.SkipBlocks(tempBuf);
				}

				break;

			default:
				_fileStream.SkipBlocks(tempBuf);
				break;
		}
	}
}
