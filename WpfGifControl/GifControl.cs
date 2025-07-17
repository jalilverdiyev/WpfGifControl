using System.IO;
using System.IO.Packaging;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfGifControl.AvaloniaBase;
using WpfGifControl.Decoding;
using WpfGifControl.Exceptions;
using WpfGifControl.Extensions;
using WpfGifControl.Helpers;

namespace WpfGifControl;

public class GifControl : Control
{
	#region Private fields

	private readonly Grid _root = new();
	private double _gifWidth, _gifHeight;
	private IterationCount _lastIterationCount;
	private GifVisualHandler? _gifHandler;
	private Stream? _lastSourceStream;
	private MemoryStream? _lastMemoryFromStream = new();

	private readonly ProgressBar _spinner = new()
	{
			IsIndeterminate = true,
			Height = 20,
			Value = 100,
			Background = new SolidColorBrush(Color.FromRgb(20, 116, 184)),
			Foreground = new SolidColorBrush(Color.FromRgb(2, 169, 244)),
			Margin = new Thickness(20, 0, 20, 0)
	};

	#endregion

	#region Dependency Properties

	public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
			nameof(Source), typeof(object), typeof(GifControl),
			new FrameworkPropertyMetadata(default,
					FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure,
					OnSourceChanged));

	public object Source
	{
		get => GetValue(SourceProperty);
		set => SetValue(SourceProperty, value);
	}

	public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
			nameof(Stretch), typeof(Stretch), typeof(GifControl), new PropertyMetadata(default(Stretch)));

	public Stretch Stretch
	{
		get => (Stretch)GetValue(StretchProperty);
		set => SetValue(StretchProperty, value);
	}

	public static readonly DependencyProperty StretchDirectionProperty = DependencyProperty.Register(
			nameof(StretchDirection), typeof(StretchDirection), typeof(GifControl),
			new PropertyMetadata(StretchDirection.Both));

	public StretchDirection StretchDirection
	{
		get => (StretchDirection)GetValue(StretchDirectionProperty);
		set => SetValue(StretchDirectionProperty, value);
	}

	public static readonly DependencyProperty IterationCountProperty = DependencyProperty.Register(
			nameof(IterationCount), typeof(IterationCount), typeof(GifControl),
			new PropertyMetadata(IterationCount.Infinite));

	public IterationCount IterationCount
	{
		get => (IterationCount)GetValue(IterationCountProperty);
		set => SetValue(IterationCountProperty, value);
	}

	public static readonly DependencyProperty SpinnerBackgroundProperty = DependencyProperty.Register(
			nameof(SpinnerBackground), typeof(Brush), typeof(GifControl),
			new PropertyMetadata(default(Brush), OnSpinnerBackgroundChanged));

	public Brush SpinnerBackground
	{
		get => (Brush)GetValue(SpinnerBackgroundProperty);
		set => SetValue(SpinnerBackgroundProperty, value);
	}

	public static readonly DependencyProperty SpinnerForegroundProperty = DependencyProperty.Register(
			nameof(SpinnerForeground), typeof(Brush), typeof(GifControl),
			new PropertyMetadata(default(Brush), OnSpinnerForegroundChanged));

	public Brush SpinnerForeground
	{
		get => (Brush)GetValue(SpinnerForegroundProperty);
		set => SetValue(SpinnerForegroundProperty, value);
	}

	public static readonly DependencyProperty SpinnerTemplateProperty = DependencyProperty.Register(
			nameof(SpinnerTemplate), typeof(ControlTemplate), typeof(GifControl),
			new PropertyMetadata(default(ControlTemplate), OnSpinnerTemplateChanged));

	public ControlTemplate SpinnerTemplate
	{
		get => (ControlTemplate)GetValue(SpinnerTemplateProperty);
		set => SetValue(SpinnerTemplateProperty, value);
	}

	#endregion

	#region Routed Events

	public static readonly RoutedEvent LoadFailedEvent = EventManager.RegisterRoutedEvent(
			nameof(LoadFailed),
			RoutingStrategy.Bubble,
			typeof(RoutedEventHandler),
			typeof(GifControl));

	public event RoutedEventHandler LoadFailed
	{
		add => AddHandler(LoadFailedEvent, value);
		remove => RemoveHandler(LoadFailedEvent, value);
	}

	#endregion

	#region Public methods

	public void BeginReplay()
	{
		_lastIterationCount = IterationCount;
		IterationCount = IterationCount.Infinite;
		_gifHandler?.SendMessage(
				new GifDrawPayload(
						HandlerCommand.Start,
						_lastSourceStream,
						GetGifSize(),
						GetSize(),
						Stretch,
						StretchDirection,
						IterationCount.Infinite));

		InvalidateVisual();
	}

	public void EndReplay()
	{
		IterationCount = _lastIterationCount;

		if (!IterationCount.IsInfinite && IterationCount.Value > _gifHandler?.CurrentIterationCount.Value)
		{
			_gifHandler.SendMessage(new GifDrawPayload() { HandlerCommand = HandlerCommand.Resume });
			return;
		}

		Stop();
	}

	public void Stop()
	{
		_gifHandler?.SendMessage(new GifDrawPayload(HandlerCommand.Stop));
	}

	#endregion

	#region Overrides

	protected override int VisualChildrenCount => 1;

	protected override Visual GetVisualChild(int index)
	{
		return _root;
	}

	protected override Size MeasureOverride(Size constraint)
	{
		_root.Measure(constraint);
		return Stretch.CalculateSize(constraint, GetGifSize(), StretchDirection);
	}

	protected override Size ArrangeOverride(Size arrangeBounds)
	{
		var sourceSize = GetGifSize();
		var result = Stretch.CalculateSize(arrangeBounds, sourceSize);
		_root.Arrange(new Rect(arrangeBounds));
		_gifHandler?.Arrange(new Rect(result));

		return _gifHandler == null ? arrangeBounds : result;
	}

	protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
	{
		base.OnRenderSizeChanged(sizeInfo);

		if (_gifHandler != null)
			OnLayoutUpdated(this, EventArgs.Empty);

		_root.InvalidateMeasure();
	}

	protected override void OnInitialized(EventArgs e)
	{
		base.OnInitialized(e);
		AddVisualChild(_root);
	}

	#endregion

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Size GetGifSize()
	{
		return new Size(_gifWidth, _gifHeight);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Size GetSize()
	{
		return new Size(Width, Height);
	}

	private async Task InitGifAsync()
	{
		// Clear previous iteration
		Stop();
		DisposeImpl();

		_gifHandler = new GifVisualHandler();
		AddChild(_gifHandler);
		LayoutUpdated += OnLayoutUpdated;
		var stream = await GetStreamFromSourceAsync();

		if (stream is null)
			return;

		if (_gifHandler is null)
			return;

		try
		{
			using var tempGifDecoder = new GifDecoder(stream, CancellationToken.None);
			_gifHeight = tempGifDecoder.Size.Height;
			_gifWidth = tempGifDecoder.Size.Width;
			_lastSourceStream = stream;
			_lastIterationCount = IterationCount;
			_gifHandler.SendMessage(
					new GifDrawPayload(
							HandlerCommand.Start,
							stream,
							GetGifSize(),
							GetSize(),
							Stretch,
							StretchDirection,
							IterationCount));

			HideSpinner();
			InvalidateVisual();
		}
		catch (InvalidGifStreamException)
		{
			_gifHandler.SendMessage(new GifDrawPayload(HandlerCommand.Invalid));
			RaiseEvent(new RoutedEventArgs(LoadFailedEvent, this));
		}
	}

	private async Task<Stream?> GetStreamFromSourceAsync()
	{
		Stream? stream;

		switch (Source)
		{
			case Stream s when ReferenceEquals(_lastSourceStream, s):
			{
				_lastMemoryFromStream?.Seek(0, SeekOrigin.Begin);
				stream = _lastMemoryFromStream ?? throw new InvalidOperationException("Memory stream is null");
				break;
			}
			case Stream s:
			{
				if (_lastMemoryFromStream != null) await _lastMemoryFromStream.DisposeAsync();

				_lastSourceStream = s;
				_lastMemoryFromStream = new MemoryStream();
				await s.CopyToAsync(_lastMemoryFromStream);
				_lastMemoryFromStream.Seek(0, SeekOrigin.Begin);
				stream = _lastMemoryFromStream;
				break;
			}
			case Uri uri:
			{
				stream = ResourcesHelper.GetResourceStream(uri);
				break;
			}
			case string str when Uri.TryCreate(str, UriKind.Absolute, out var uri):
			{
				if (uri.Scheme == Uri.UriSchemeFile || uri.Scheme == PackUriHelper.UriSchemePack)
				{
					stream = ResourcesHelper.GetResourceStream(uri);
				}
				else if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
				{
					var httpStream = await new HttpClient().GetStreamAsync(uri);
					var ms = new MemoryStream();
					await httpStream.CopyToAsync(ms);
					httpStream.Close();
					ms.Seek(0, SeekOrigin.Begin);
					stream = ms;
					_lastMemoryFromStream = ms;
				}
				else
				{
					throw new ArgumentException(
							"Unsupported Source string: Uri format isn't correct.");
				}

				break;
			}
			case string strUri when ResourcesHelper.TryCreatePackUri(strUri, out var uri):
			{
				stream = ResourcesHelper.GetResourceStream(uri);
				break;
			}
			default:
				throw new ArgumentException(
						"Unsupported Source object: only Stream, Uri and absolute uri string are supported.");
		}

		return stream;
	}

	private void OnLayoutUpdated(object? sender, EventArgs e)
	{
		_gifHandler?.SendMessage(
				new GifDrawPayload(
						HandlerCommand.Update,
						null,
						GetGifSize(),
						RenderSize,
						Stretch,
						StretchDirection,
						IterationCount));
	}

	#region PropertyChangedEvent Handlers

	private static async void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not GifControl control)
			return;

		control.DisposeImpl();
		control.ShowSpinner();
		await control.Dispatcher.InvokeAsync(async () => { await control.InitGifAsync(); },
				DispatcherPriority.Background);
	}

	private static void OnSpinnerBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not GifControl gifControl)
			return;

		gifControl._spinner.Background = gifControl.SpinnerBackground;
	}

	private static void OnSpinnerForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not GifControl gifControl)
			return;

		gifControl._spinner.Foreground = gifControl.SpinnerForeground;
	}

	private static void OnSpinnerTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not GifControl gifControl)
			return;

		gifControl._spinner.Template = gifControl.SpinnerTemplate;
	}

	#endregion

	private void ShowSpinner()
	{
		if (_gifHandler != null)
			RemoveChild(_gifHandler);

		AddChild(_spinner);
	}

	private void HideSpinner()
	{
		RemoveChild(_spinner);
	}

	private void AddChild(UIElement element)
	{
		if (_root.Children.Contains(element))
			return;

		_root.Children.Add(element);
	}

	private void RemoveChild(UIElement element)
	{
		_root.Children.Remove(element);
	}

	private void DisposeImpl()
	{
		if (_gifHandler != null)
		{
			_gifHandler.SendMessage(new GifDrawPayload(HandlerCommand.Dispose));
			RemoveChild(_gifHandler);
			_gifHandler = null;
		}

		_lastMemoryFromStream?.Dispose();
		_lastMemoryFromStream = null;
		_lastSourceStream = null;
		GC.SuppressFinalize(this);
	}
}
