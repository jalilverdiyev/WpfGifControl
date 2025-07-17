using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfGifControl.AvaloniaBase;
using WpfGifControl.Extensions;

namespace WpfGifControl;

internal class GifVisualHandler : CustomVisualHandler
{
	private readonly object _lock = new();
	private bool _running;
	private bool _isDisposed;
	private bool _isInvalid;
	private Stretch? _stretch;
	private StretchDirection? _stretchDirection;
	private Size _gifSize;
	private Size _renderSize;
	private GifInstance? _gifInstance;
	private TimeSpan _animationElapsed;
	private TimeSpan _lastServerTime;

	protected override void OnMessage(object message)
	{
		if (message is not GifDrawPayload msg)
			return;

		switch (msg)
		{
			case
			{
					HandlerCommand: HandlerCommand.Start,
					Source: { } stream,
					IterationCount: { } iteration,
					Stretch: { } st,
					StretchDirection: { } sd,
					GifSize: { } gifSize,
					Size: { } renderSize
			}:
			{
				_isInvalid = false;
				_gifInstance?.Dispose();
				_gifInstance = new GifInstance(stream);
				_gifInstance.IterationCount = iteration;
				_gifInstance.IterationFinished += HandleIterationFinished;
				_lastServerTime = CompositionNow;
				_animationElapsed = TimeSpan.Zero;
				_gifSize = gifSize;
				_renderSize = renderSize;
				_running = true;
				_stretch = st;
				_stretchDirection = sd;
				Invalidate();
				RequestAnimationFrame();
				break;
			}
			case
			{
					HandlerCommand: HandlerCommand.Update,
					Stretch: { } st,
					IterationCount: { } iteration,
					StretchDirection: { } sd,
					Size: { } renderSize
			}:
			{
				_stretch = st;
				_stretchDirection = sd;
				_renderSize = renderSize;

				if (_gifInstance != null)
					_gifInstance.IterationCount = iteration;

				Invalidate();
				RequestAnimationFrame();
				break;
			}
			case { HandlerCommand: HandlerCommand.Stop }:
			{
				if (_gifInstance != null)
					_gifInstance.IterationFinished -= HandleIterationFinished;

				_running = false;
				_isInvalid = false;
				break;
			}
			case { HandlerCommand: HandlerCommand.Dispose }:
			{
				DisposeImpl();
				break;
			}
			case
			{
					HandlerCommand: HandlerCommand.Invalid
			}:
			{
				_isInvalid = true;
				Invalidate();
				RequestAnimationFrame();
				break;
			}
		}
	}

	protected override void OnAnimationFrameUpdate()
	{
		if (!_running || _isDisposed)
			return;

		Invalidate();
		RequestAnimationFrame();
	}

	protected override void OnRenderCustom(DrawingContext context)
	{
		lock (_lock)
		{
			if (_stretch is not { } st
			    || _stretchDirection is not { } sd
			    || (!_isInvalid && _gifInstance is null)
			    || _isDisposed)
				return;

			var bounds = _renderSize;
			var sourceSize = _renderSize;
			var destRect = CalculateDestinationRect(bounds, sourceSize, st, sd);
			WriteableBitmap? bitmap;

			if (_isInvalid)
			{
				DrawText(context, "The given source isn't a valid GIF image", 16, new Point(destRect.X, destRect.Y));
				return;
			}

			if (_running)
			{
				_animationElapsed += CompositionNow - _lastServerTime;
				_lastServerTime = CompositionNow;
				bitmap = _gifInstance!.ProcessFrameTime(_animationElapsed);
			}
			else
			{
				bitmap = _gifInstance!.GetLastFrame();
			}

			if (st == Stretch.UniformToFill)
				context.PushClip(new RectangleGeometry(new Rect(0, 0, bounds.Width, bounds.Height)));

			context.DrawImage(bitmap, destRect);
		}
	}

	private Rect CalculateDestinationRect(Size bounds, Size sourceSize, Stretch stretch,
			StretchDirection stretchDirection)
	{
		var scale = stretch.CalculateScaling(bounds, sourceSize, stretchDirection);
		var scaledSize = new Size(scale.X * sourceSize.Width, scale.Y * sourceSize.Height);
		var x = (bounds.Width - scaledSize.Width) / 2;
		var y = (bounds.Height - scaledSize.Height) / 2;

		if (stretch != Stretch.Uniform && stretch != Stretch.None)
			return new Rect(x, y, scaledSize.Width, scaledSize.Height);

		x = Math.Max(0, x);
		y = Math.Max(0, y);

		var finalWidth = Math.Min(scaledSize.Width, bounds.Width);
		var finalHeight = Math.Min(scaledSize.Height, bounds.Height);

		return new Rect(x, y, finalWidth, finalHeight);
	}

	private void DrawText(DrawingContext context, string text, double fontSize, Point drawPoint)
	{
		var typeface = new Typeface("Segoe UI");

		if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
		{
			typeface = new Typeface("Arial");
			typeface.TryGetGlyphTypeface(out glyphTypeface);
		}

		var glyphIndexes = new ushort[text.Length];
		var advanceWidths = new double[text.Length];

		for (var i = 0; i < text.Length; i++)
		{
			if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(text[i], out var glyph))
				glyph = 0;

			glyphIndexes[i] = glyph;
			advanceWidths[i] = glyphTypeface.AdvanceWidths[glyph] * fontSize;
		}

		var glyphRun = new GlyphRun(glyphTypeface, 0, false, fontSize, 16, glyphIndexes, drawPoint, advanceWidths, null,
				null, null, null, null, null);

		context.DrawGlyphRun(Brushes.Black, glyphRun);
	}

	private void DisposeImpl()
	{
		lock (_lock)
		{
			if (_isDisposed) return;
			_isDisposed = true;

			if (_gifInstance != null)
			{
				_gifInstance.IterationFinished -= HandleIterationFinished;
				_gifInstance.Dispose();
			}

			_running = false;
			_animationElapsed = TimeSpan.Zero;
			_lastServerTime = TimeSpan.Zero;
			_isInvalid = false;
		}
	}

	private void HandleIterationFinished(object? sender, EventArgs args)
	{
		SendMessage(new GifDrawPayload(HandlerCommand.Stop));
	}
}
