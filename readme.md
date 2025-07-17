# WpfGifControl

A lightweight and memory-efficient WPF control for displaying animated GIFs. This project is inspired by the [Avalonia UI GIF control](https://github.com/AvaloniaUI/Avalonia.Labs/tree/main/src/Avalonia.Labs.Gif) and [XamlAnimatedGif](https://github.com/XamlAnimatedGif/XamlAnimatedGif), with performance and usability enhancements specific to WPF.

---

## 🚀 Motivation

While [XamlAnimatedGif](https://github.com/XamlAnimatedGif/XamlAnimatedGif) provides robust functionality, it exhibits high memory usage, especially with multiple GIFs. After extensive research, Avalonia's GIF implementation proved to be the most memory-efficient, inspiring this WPF-specific port with improvements tailored for WPF's rendering system.

---

## ⚠️ Disclaimer

Some core components (such as `GifDecoder`, `GifControl`, and supporting internal types) are adapted directly from Avalonia UI. However, due to architectural differences in the rendering pipeline, significant portions of the codebase have been rewritten or redesigned. This is **not** a direct copy-paste. Additionally, this library introduces several new features not present in the original Avalonia version.

---

## 📦 Usage

A full-featured MVVM-based demo project is included to showcase all usage patterns. Below is a quick reference.

---

### 1️⃣ Import the Namespace

In your XAML:

```xml
<UserControl
    xmlns:gif="clr-namespace:WpfGifControl;assembly=WpfGifControl"
    ...>
</UserControl>
```

> You can use the control in any WPF element, not just `UserControl`.

---

### 2️⃣ Add the Control

```xml
<Grid>
    <gif:GifControl />
</Grid>
```

---

## 🔑 Key Properties

### 🎞️ `Source`

Accepts multiple formats for loading GIFs:

- **Relative URI (Project Resources)**
  If the GIF is embedded as a `Resource`:

  ```
  /Resources/Images/example1.gif
  ```

  ```xml
  <gif:GifControl Source="/Resources/Images/example1.gif"/>
  ```

- **Absolute URI (Pack or File Path)**

  ```xml
  <gif:GifControl Source="pack://application:,,,/YourApp;component/Resources/Images/example1.gif"/>
  <!-- or -->
  <gif:GifControl Source="C:\Users\YourName\Pictures\example.gif"/>
  ```

- **Web URL**

  ```xml
  <gif:GifControl Source="https://example.com/path/to/image.gif"/>
  ```

- **Stream Binding**

  ```xml
  <gif:GifControl Source="{Binding SourceStream}"/>
  ```

  ```csharp
  public Stream SourceStream { get; set; }
  ```

> ⚠️ **Unsupported Formats:** The control does not support non-GIF image formats (e.g., `.png`, `.jpg`, `.jpeg`). If an unsupported format is provided, an error message will be displayed: _"The given source isn't a valid GIF image"_.
>
> For programmatic handling, a `RoutedEvent` named `LoadFailed` is raised when the GIF fails to load.

---

### 🔁 `IterationCount`

Defines the repeat behavior:

```xml
<!-- Infinite animation (default) -->
<gif:GifControl Source="..." IterationCount="Infinite"/>

<!-- Play 5 times -->
<gif:GifControl Source="..." IterationCount="5"/>
```

---

### 📐 `Stretch` and `StretchDirection`

These properties behave the same as in WPF's built-in `Image` control, giving you control over how the GIF is scaled.

---

## ✨ Additional Features

### 🔄 Loading Spinner

A built-in, customizable spinner displays during long load times. You can adjust:

- `Foreground`
- `Background`
- Or override the control `Template` for full customization.

---

### ⏯️ Playback Control

Programmatic methods to control playback:

- `BeginReplay()` — Plays the animation infinitely until `EndReplay()` is called.
- `EndReplay()` — Stops infinite playback and resumes normal iteration count.
- `Stop()` — Fully stops animation.

---

## ⚠️ Performance Warning

To maintain low memory usage, frames are **not cached**. Each frame is decoded on-the-fly. Caching increases memory usage by \~3x. Due to this design, **a maximum of 4 GIFs** should be animated simultaneously for optimal performance.
