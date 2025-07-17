using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfGifControl.AvaloniaBase;

namespace WpfGifControl;

internal record struct GifDrawPayload(
		HandlerCommand HandlerCommand,
		Stream? Source = default,
		Size? GifSize = default,
		Size? Size = default,
		Stretch? Stretch = default,
		StretchDirection? StretchDirection = default,
		IterationCount? IterationCount = default);
