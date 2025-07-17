namespace WpfGifControl.AvaloniaBase;

using System;
using System.Windows;
using System.Windows.Media;

internal abstract class CustomVisualHandler : FrameworkElement
{
	private bool _inRender;
	private Vector _effectiveSize;
	private Rect _currentTransformedClip;

	protected virtual void OnMessage(object message)
	{
	}

	protected virtual void OnAnimationFrameUpdate()
	{
	}

	public void SendMessage(object message)
		=> OnMessage(message);

	protected void RequestAnimationFrame()
	{
		CompositionTarget.Rendering -= OnCompositionTargetRendering;
		CompositionTarget.Rendering += OnCompositionTargetRendering;
	}

	private void OnCompositionTargetRendering(object? sender, EventArgs e)
	{
		CompositionTarget.Rendering -= OnCompositionTargetRendering;
		OnAnimationFrameUpdate();
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		base.OnRender(drawingContext);

		_inRender = true;
		_effectiveSize = new Vector(RenderSize.Width, RenderSize.Height);
		_currentTransformedClip = new Rect(RenderSize);
		OnRenderCustom(drawingContext);
		_inRender = false;
	}

	protected abstract void OnRenderCustom(DrawingContext context);

	protected Vector EffectiveSize
	{
		get
		{
			VerifyAccess();
			return _effectiveSize;
		}
	}

	protected TimeSpan CompositionNow => TimeSpan.FromMilliseconds(Environment.TickCount);

	protected void Invalidate()
	{
		VerifyAccess();
		InvalidateVisual();
	}

	protected void Invalidate(Rect rect)
	{
		VerifyAccess();
		InvalidateVisual();
	}

	protected bool RenderClipContains(Point pt)
	{
		VerifyInRender();
		return _currentTransformedClip.Contains(pt);
	}

	protected bool RenderClipIntersects(Rect rc)
	{
		VerifyInRender();
		return _currentTransformedClip.IntersectsWith(rc);
	}

	private new void VerifyAccess()
	{
		if (!Dispatcher.CheckAccess())
			throw new InvalidOperationException("UI thread access required");
	}

	private void VerifyInRender()
	{
		VerifyAccess();
		if (!_inRender)
			throw new InvalidOperationException("This API is only available during rendering");
	}
}
