using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfGifControl.AvaloniaBase;
using WpfGifControl.Decoding;

namespace WpfGifControl;

internal class GifInstance : IDisposable
{
	public event EventHandler? IterationFinished;

	private readonly WriteableBitmap? _targetBitmap;
	private readonly GifDecoder _gifDecoder;
	private readonly List<TimeSpan>? _frameTimes;
	private TimeSpan _totalTime;
	private uint _iterationCount;
	private int _currentFrameIndex;
	private bool _isDisposed;

	private CancellationTokenSource CurrentCts { get; }
	public IterationCount IterationCount { get; set; }
	public PixelSize GifPixelSize { get; }

	public GifInstance(Stream currentStream)
	{
		if (!currentStream.CanSeek)
			throw new InvalidDataException("The provided stream is not seekable");

		if (!currentStream.CanRead)
			throw new InvalidDataException("Can't read the stream provided");

		currentStream.Seek(0, SeekOrigin.Begin);
		CurrentCts = new CancellationTokenSource();
		_gifDecoder = new GifDecoder(currentStream, CurrentCts.Token);

		if (_gifDecoder.Header is null)
			return;

		GifPixelSize = new PixelSize(_gifDecoder.Header.Dimensions.Width, _gifDecoder.Header.Dimensions.Height);
		_targetBitmap = new WriteableBitmap(GifPixelSize.Width, GifPixelSize.Height, 96, 96, PixelFormats.Bgra32,
				BitmapPalettes.WebPaletteTransparent);

		_totalTime = TimeSpan.Zero;
		_frameTimes = _gifDecoder.Frames.Select(f =>
		{
			_totalTime = _totalTime.Add(f.FrameDelay);
			return _totalTime;
		}).ToList();

		_gifDecoder.RenderFrame(0, _targetBitmap);
	}

	public WriteableBitmap? ProcessFrameTime(TimeSpan elapsed)
	{
		if (!IterationCount.IsInfinite && _iterationCount > IterationCount.Value)
		{
			ProcessFrameIndex(_frameTimes?.Count - 1 ?? 0);
			IterationFinished?.Invoke(this, EventArgs.Empty);
		}

		if (CurrentCts.IsCancellationRequested)
			return null;

		if (_frameTimes?.DefaultIfEmpty() == null)
			return null;

		var totalTicks = _totalTime.Ticks;

		if (totalTicks == 0)
			return ProcessFrameIndex(0);

		var elapsedTicks = elapsed.Ticks;
		var timeModulus = TimeSpan.FromTicks(elapsedTicks % totalTicks);
		var targetFrame = _frameTimes.FirstOrDefault(ft => timeModulus < ft);
		var currentFrame = _frameTimes.IndexOf(targetFrame);
		currentFrame = currentFrame == -1 ? 0 : currentFrame;

		if (_currentFrameIndex == currentFrame)
			return _targetBitmap;

		_iterationCount = (uint)(elapsedTicks / totalTicks);
		return ProcessFrameIndex(currentFrame);
	}

	public WriteableBitmap? GetLastFrame() => _targetBitmap;

	public void Dispose()
	{
		if (_isDisposed)
			return;

		GC.SuppressFinalize(this);
		_isDisposed = true;
		CurrentCts.Cancel();
		_gifDecoder.Dispose();
		_targetBitmap?.Freeze();
		CurrentCts.Dispose();
	}

	private WriteableBitmap? ProcessFrameIndex(int frameIndex)
	{
		_gifDecoder.RenderFrame(frameIndex, _targetBitmap);
		_currentFrameIndex = frameIndex;

		return _targetBitmap;
	}
}
