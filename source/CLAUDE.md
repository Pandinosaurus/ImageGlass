# ImageGlass v10

## Project Overview
ImageGlass v10 is a complete rewrite in **C# with .NET 10**, using **Avalonia 12** for cross-platform image viewing and **SkiaSharp 3.119.x** for high-performance rendering. The primary targets are **Windows, macOS, Linux** on x64 and ARM64 with AOT (Ahead-of-Time) publishing enabled.

## Repository Layout
- **`/source`** — v10 source code (the active codebase; this `CLAUDE.md` lives here). All development happens here.
- **`/v9`** — legacy v9 source code (WinForms/.NET, archived).

> **Default to v10 (`/source`).** Do not read, modify, or reference the v9 source in `/v9` unless the user explicitly asks about v9 or requests porting/comparison work.

**Key Projects:**
- `ImageGlass.Lib`: Core library (net10.0) — UI controls, rendering, codecs, themes, localization, settings
- `ImageGlass.Win32`: Windows desktop app (net10.0-windows10.0.19041.0) — entry point with Win32 APIs and platform-specific services
- `ImageGlass.Linux`: Linux desktop app (net10.0) — entry point with Linux-specific services
- `ImageGlass.Mac`: macOS desktop app (net10.0) — entry point with macOS-specific services

---

## Critical Focus Areas (Always Prioritize)

### 1. AOT/Trim Safety
- **All platform projects** (Win32, Linux, Mac) use `PublishAot=true` and `PublishTrimmed=true` with trim analyzer enabled.
- **Win32 project** additionally uses `DisableRuntimeMarshalling=true` and CsWin32 code generation.
- Avoid reflection-based serialization; use custom JSON converters in `Common/Types/JsonTypeConverters/`.
- Test AOT builds: `dotnet publish ImageGlass.Win32 -c Release` produces a self-contained, trimmed executable.

### 2. Thread-Safety (No Data Races)
- Use `Lock` for protecting shared state (not `object` locks).
- Use `InterlockedBool` instead of plain `bool` for concurrent flags.
- Use `ConcurrentDictionary` for thread-safe collections (`PhotoManager` index).
- Always check `IsDisposed` before accessing resources in multi-threaded contexts.
- Example: `PhotoManager` uses `Lock _lock` to protect `_items`, `_dict`, and `_cachedIndexes`.

### 3. Memory Leak Prevention
- Use `try/finally` or `using` statements for all `SKObject` acquisitions.
- Override `OnDisposing()` in `PhDisposable` subclasses to clean up unmanaged resources.
- Example: `MipmapTileCache` evicts with `RequestDispose()` (never `Dispose()`) so an in-flight render lease stays valid.
- Always dispose `SKImageRef` leases when done; use `KeepAlive()` pattern in `MipmapTileCache`.

### 4. Memory Usage Control
- Respect cache limits: `MipmapTileCache` caps 100 tiles; `PhotoManager_Caching` honors `Config.MaxMemoryCacheInMb`, `Config.MaxFileSizeCacheInMb`, `Config.MaxDimensionCache`.
- LRU eviction is mandatory in tile and photo caches to prevent unbounded growth.
- **Hard decode ceiling**: no `SKBitmap` or raster `SKImage` may exceed `int.MaxValue` bytes, whatever the free RAM. That is ~536 MP at 4 bpp, ~268 MP at `RgbaF16`, ~134 MP at `RgbaF32`. There is no bypass: `SKImage.FromPixels` from an `SKData`, a raw pointer, or an `SKPixmap` all fail at the same boundary, so host-owned-buffer tricks do not help. Both codecs shrink to fit instead of failing (see "Oversized image decode").
- Mark a finished bitmap `SetImmutable()` before `SKImage.FromBitmap` (`SkiaCodec.ToSKImageNoCopy`), otherwise Skia duplicates the entire pixel buffer and doubles peak memory.
- Profile memory with large images and GIF animations to ensure caches don't leak.

### 5. Cross-Platform Design
- **Interfaces first** in `ImageGlass.Lib/Common/ServiceProviders/*`: `IFileSearchProvider`, `IShellProvider`, `IPrintProvider`, etc.
- **Implementations** per platform:
  - `ImageGlass.Win32/Common/ServiceProviders/*`: `Win32FileSearchProvider`, `Win32ShellProvider`, etc.
  - `ImageGlass.Linux/Common/ServiceProviders/*`: `LinuxPrintProvider`, `LinuxShellProvider`, etc.
  - `ImageGlass.Mac/Common/ServiceProviders/*`: `MacPrintProvider`, `MacShellProvider`, etc.
- Register service providers in each platform's `Program.cs` before app bootstrap.
- Avoid platform-specific code in library; use dependency injection via `Core.API`, `Core.FileSearchProvider`, etc.

---

## Architecture Highlights

### Core Application Layers
1. **Core Static Hub** (`Core.cs`, `Core_Events.cs`): Central event dispatcher and singleton registry
   - `Core.AppInstance`: App lifecycle & unique instance enforcement
   - `Core.Config`: Global configuration state
   - `Core.Photos`: Photo collection and file searching
   - Events: `LanguageChanged`, `ThemeChanged`, `PhotoUnloaded`, `PhotoSaved`, `ColorProfileChanged`
   - Service provider slots: `ShellProvider`, `PreviewProvider`, `FileSearchProvider`, `PrintProvider`, `API`

2. **Service Providers** (Plug-in Pattern)
   - Platform-agnostic interfaces in `ImageGlass.Lib/Common/ServiceProviders/*`
   - Platform implementations:
     - Win32: `ImageGlass.Win32/Common/ServiceProviders/*`
     - Linux: `ImageGlass.Linux/Common/ServiceProviders/*`
     - macOS: `ImageGlass.Mac/Common/ServiceProviders/*`
   - Shared concrete providers in library: `PhotoPreviewProvider`, `SlideshowProvider`, `UpdateProvider`
   - Examples: `IFileSearchProvider`, `IShellProvider`, `IPrintProvider`, `IWindowColorProfileProvider`, `IPhotoPreviewProvider`
   - Registered at startup in each platform's `Program.cs`; initialized in `App.xaml.cs`

3. **Photo & Codec Abstraction**
   - `PhotoManager`: Thread-safe collection with `AvaloniaList<Photo>` + `ConcurrentDictionary` index
   - Codecs: `SkiaCodec` (fast SkiaSharp pipeline) and `MagickCodec` (Magick.NET fallback for obscure formats)
   - Async loading via `PhotoLoadingOptions` + frame animation support via `SkiaAnimator`/`AnimatorImpl`
   - Caching: `PhotoManager_Caching.cs` uses spiral pattern, LRU eviction, memory budgets

4. **Viewer & Rendering** (`ViewerControl`)
   - Partial classes split by concern: `ViewerControl_Render.cs`, `ViewerControl_ZoomAndPan.cs`, `ViewerControl_Events.cs`, `ViewerControl_Animation.cs`, `ViewerControl_NavButtons.cs`, etc.
   - High-performance mipmap tile caching: `MipmapTileCache` with LRU eviction for large images (8192×8192+)
   - Gesture recognizers: `PhPanGestureRecognizer`, `PhPinchGestureRecognizer` accumulate points for smooth interaction
   - SkiaSharp rendering in `ViewerControl_Render.cs` + `PhotoRenderer.cs`
   - **Navigation buttons**: `NavButtonsOverlay` (separate `PhControl` overlay) renders left/right arrow buttons on hover; uses `RequestAnimationFrame` for frame-rate-independent slide+fade animation; click detection on pointer release (does not interfere with panning); config-bound via `EnableNavButtonsProperty` StyledProperty

---

## Code Organization & Naming Conventions

### Folder Structure
```
ImageGlass.Lib/
├── Common/
│   ├── Actions/                   # Action definitions
│   ├── Commands/                  # Command definitions
│   ├── AppThemes/                 # Theme system: IgTheme, IgThemeColors, AppThemeColors, IgThemeMetadata
│   ├── BHelper/                   # Static helper methods (see "Reusing Shared Code"): Color, Format, General, JsonEx, Path, ProcessHelper, Sound, Task
│   ├── Extensions/                # Extension methods (see "Reusing Shared Code"): Color_Exts, DrawingContext_Exts, ISolidColorBrush_Exts, Point_Exts, Rect_Exts, Size_Exts, SKObject_Exts
│   ├── Localization/              # Lang.cs, LangId enum
│   ├── Photoing/                  # Photo management, codecs, animators
│   │   ├── Animators/             # SkiaAnimator, AnimatorImpl
│   │   ├── Codecs/                # Codec pipeline
│   │   │   ├── MagickCodecs/      # Magick.NET fallback codec
│   │   │   ├── Registry/          # CodecRegistry
│   │   │   ├── SkiaCodecs/        # SkiaSharp fast-path codec
│   │   │   └── SvgCodecs/         # SVG (vector) codec
│   │   ├── Manager/               # PhotoManager & PhotoManager_*.cs (search, watcher, caching)
│   │   └── Photos/                # Photo, PhotoMetadata, PhotoColorProfile, etc.
│   ├── ServiceProviders/          # Interface contracts + shared providers
│   │   ├── AppAPIs/               # App API provider interfaces
│   │   ├── FileSearchService/     # File search service abstractions
│   │   ├── Update/                # Update provider abstractions
│   │   ├── I*.cs                  # Interfaces (IFileSearchProvider, IShellProvider, etc.)
│   │   ├── PhotoPreviewProvider.cs
│   │   ├── SlideshowProvider.cs
│   │   └── UpdateProvider.cs
│   └── Types/                     # Base classes + shared types: PhReactive, PhDisposable, InterlockedBool, Resx, Const, Hotkey, SKImageRef, Enums, Dir
│       └── JsonTypeConverters/    # AOT-safe JSON converters (see "Reusing Shared Code")
├── Plugins/                       # Native plugin host: PluginRegistry, codec proxies
├── Settings/                      # Config.cs, Config_Static.cs, ConfigMetadata.cs
├── Tools/                         # Built-in & external tool framework
│   ├── Builtins/                  # ColorPicker, CropImage, FrameNav, ImageResizer, LosslessCompression
│   └── External/                  # External tool process management
├── UI/
│   ├── BaseControls/              # Ph* controls (see "Designing UI"): PhControl, PhButton, PhTextBox, PhTextBlock, PhToolButton, PhMenuItem, PhHotkeyPicker, PhGridSplitter, PhVirtualizingUniformPanel, PhCommandPreview, PhSearchBoxControl, PhTableControl
│   ├── Gallery/                   # Gallery browsing UI (GalleryControl)
│   ├── Styles/                    # App-wide XAML styles + resource dictionaries (see "Designing UI"); merged in App.axaml
│   ├── Toolbar/                   # Toolbar UI and model (ToolbarControl)
│   ├── Viewer/                    # ViewerControl (partial classes by feature)
│   │   ├── Checkerboard/          # Transparency checkerboard background
│   │   ├── NavButtons/            # NavButtonsOverlay, NavButtonsInfo, NavButtonClickedEventArgs
│   │   ├── Renderer/              # MipmapTileCache, PhotoRenderer
│   │   ├── Selection/             # Selection tools
│   │   └── ZoomAndPan/            # Gesture recognizers, zoom/pan math
│   └── Windowing/                 # PhWindow, ModalWindow, DialogWindow, PhColorPickerDialog
├── ViewModels/                    # MainWindowViewModel, MainWindowModel, SettingsViewModel
└── Windows/                       # Top-level windows: MainWindow, AboutWindow, SettingsWindow, UpdateWindow, ExportFramesWindow
    ├── Main/                      # MainWindow_View partial
    └── Settings/                  # Settings UI: Controls/, Pages/, Windows/

ImageGlass.Win32/
├── Program.cs                     # Entry point, service registration, AOT bootstrap
├── Properties/                    # PublishProfiles
├── Windows/                       # MainWindow32.cs (Win32-specific window)
└── Common/
    ├── ServiceProviders/          # Win32 implementations (Win32FileSearchProvider, etc.)
    └── WinAPI/                    # P/Invoke wrappers (Win32*.cs files)

ImageGlass.Linux/
├── Program.cs                     # Entry point, service registration
└── Common/
    └── ServiceProviders/          # Linux implementations (LinuxPrintProvider, etc.)

ImageGlass.Mac/
├── Program.cs                     # Entry point, service registration
└── Common/
    ├── Sandbox/                   # macOS sandbox helpers
    └── ServiceProviders/          # macOS implementations (MacPrintProvider, etc.)
```

### Naming Rules
- **Classes inheriting from Control/UserControl**: Use `PhControl` base (e.g., `ToolbarControl : PhControl`)
- **View Models**: Suffix with `ViewModel` (e.g., `MainWindowViewModel : PhReactive`)
- **Async methods**: Always suffix with `Async`
- **Codecs/Providers**: Prefix with platform/source (e.g., `SkiaCodec`, `Win32FileSearchProvider`, `MagickCodec`)
- **Partial class files**: Name by concern (e.g., `ViewerControl_Render.cs`, `PhotoManager_Caching.cs`, `AppAPIProvider_Hotkeys.cs`)

---

## Reusing Shared Code (Always Check Before Writing New)

Before adding a helper, converter, or extension, check these folders; the utility you need likely already exists.

### Type Extensions: `Common/Extensions/`
Extension methods on framework types. Prefer these over re-implementing:
- `Color_Exts`: `ToBrush()`, `WithAlpha()`, `NoAlpha()`, `Blend()`, `WithBrightness()`, `IsLight()`, `BlackOrWhite()` / `InvertBlackOrWhite()`, `ToHex()`, `ToRgbaString()`, `ToCmyk()` / `ToCmykString()`, `ToHslString()`, `ToHsvString()`, `ToCIELAB()` / `ToCIELABString()`
- `ISolidColorBrush_Exts`, `DrawingContext_Exts`, `Point_Exts`, `Rect_Exts`, `Size_Exts`: Avalonia geometry & drawing helpers
- `SKObject_Exts`: `IsDisposed()` and other SkiaSharp guards

### Helper Methods: `Common/BHelper/` (static `BHelper` partial class)
- `Color.cs`: `ColorFromHex()`
- `Format.cs`: `FormatSize()`, `FormatDateTime()`, `SimplifyFractions()`, star-rating formatting
- `General.cs`: `OS`, `GenerateWrappedIndexes()` (spiral cache order), `ComputeIndexInRange()`, `ResizeRatio()`, `GetInAppError()`
- `Path.cs`: `ConfigDir()` / `BaseDir()`, `ResolvePath()`, `CheckPath()`, `OpenUrlAsync()`, `OpenFilePath()` / `OpenFolderPath()`, `DeleteFile()`
  - `ResolvePath()` is the **single** public path normalizer: it unwraps `ig://`, strips quotes, expands env vars, follows `.lnk` and macOS `.app`, then makes the result absolute. Never add a second normalizer beside it; if a caller needs only part of that, make the missing piece a `private` helper *inside* `ResolvePath` rather than a parallel public API.
- `ProcessHelper.cs`: `RunExeAsync()` / `RunExeCmd()`, `RunSync()`, `ExitApp()`
- `Sound.cs`: `PlayNotificationSoundAsync()` (extracts the bundled `Assets/Sounds/beep.wav` to `_temp`, then plays it via NetCoreAudio)
- `JsonEx.cs`: `CreateJsonOptions()`, `ReadJsonFromFile()` / `WriteJsonToFileAsync()` (AOT-safe via `JsonTypeInfo<T>`)
- `Task.cs`: `Debounce()`, `GcCollect()`

### JSON Converters: `Common/Types/JsonTypeConverters/`
AOT-safe converters for `Config` serialization. Reuse an existing one, or add a new converter here; never use reflection-based serialization.
- `JsonStringEnumSafeConverter<T>` (always use for enums; never the built-in `JsonStringEnumConverter`)
- `JsonStringToHotkeyConverter`, `JsonArrayToZoomFactorConverter`, `JsonArrayToRectConverter` / `JsonArrayToPixelRectConverter`, `JsonArrayToDoubleConverter` / `JsonArrayToIntConverter`, `JsonDateTimeConverter`, `JsonHashSetToStringConverter`, `JsonObservableCollectionToStringConverter`

---

## Designing UI

When building or restyling UI, reuse the app's existing controls, design tokens, styles, icons, and fonts instead of raw Avalonia controls or hardcoded values.

### 1. Controls: prefer `UI/` over raw Avalonia
- **Base controls** (`UI/BaseControls/`, all inherit `PhControl`): `PhButton`, `PhTextBox`, `PhTextBlock`, `PhToolButton`, `PhMenuItem`, `PhHotkeyPicker`, `PhGridSplitter`, `PhVirtualizingUniformPanel`, `PhCommandPreview`, `PhTableControl` (read-only data table: aligned auto-fit columns + hover/focus-revealed icon-button actions; use for settings tables).
- **Windows & dialogs** (`UI/Windowing/`): inherit `PhWindow`; for modal/dialog flows use `ModalWindow` / `DialogWindow`; use `PhColorPickerDialog` for color picking.
- **Feature controls**: `ViewerControl` (`UI/Viewer/`), `GalleryControl` (`UI/Gallery/`), `ToolbarControl` (`UI/Toolbar/`).

### 2. Colors, brushes & styles: never hardcode
- **Themed resources via `Resx` / `ResxId`** (`Common/Types/Resx.cs`): resolve in XAML with `{DynamicResource <ResxId-name>}`, or in code with `Resx.Get<T>(ResxId.X)` / `Resx.CreateBinding(ResxId.X)`. The resource name equals the `ResxId` enum name. These keys are populated at runtime by the `Update*` methods in `Core.cs` (`UpdateBaseResources`, `UpdateAppThemedColorResources`, `UpdateViewerBackgroundBrushResource`, `UpdateAccentColorResources`; see `Core.cs:387-635`). To add a new themed value: add a `ResxId` entry and set it inside the matching `Update*` method.
- **Brush vs Color**: `ResxId` keys ending in `Brush` resolve to an `IBrush`; keys ending in `Color` resolve to a `Color`. Use the `*Color` variant where a `Color` is required (e.g. `GradientStop.Color`).
- **Theme & situational colors** (`Common/AppThemes/AppThemeColors.cs`): the static source of truth for text success/warning/danger, situational backgrounds, and window backgrounds. The `Update*` methods derive `Resx` resources from these, so reuse them rather than duplicating hex literals. `IgThemeColors.cs` is the per-theme-pack color model.
- **Shared styles & tokens** (`UI/Styles/`, merged in `App.axaml`): control styles (`ButtonStyle`, `TextBoxStyle`, `ComboBoxStyle`, `CheckBoxStyle`, `RadioButtonStyle`, `MenuStyle`, `SliderStyle`, `ListBoxItemStyle`, `ControlStyle`) and resource dictionaries (`CommonResources`, `MenuResources`, `ComboBoxResources`, `ScrollBarResources`, `IconResources`). Reuse tokens from `CommonResources.axaml`: `ControlCornerRadius` (7), `OverlayCornerRadius` (10), `PhAccentFill` / `PhAccentFillPointerOver` / `PhAccentFillPressed`, and the shared disabled palette (`PhControlFillDisabled`, `PhControlGlyphDisabled`, `PhControlBorderDisabled`, `PhControlContentOpacityDisabled`, `PhSvgCssDisabled`).

### 3. Icons: `UI/Styles/IconResources.axaml`
Reuse the `StreamGeometry` icon resources via `{StaticResource IconName}`: `IconSearch`, `IconSettings`, `IconSave`, `IconSaveAs`, `IconCrop`, `IconCopy`, `IconReset`, `IconClose`, `IconEllipsis`, `IconArrowPrevious` / `IconArrowNext` / `IconArrowLeft` / `IconArrowRight`, `IconPlay`, `IconPause`, `IconImageForward`, `IconLivePhoto`, `IconFolderOpen`. In code-behind, resolve the same geometries with `Resx.GetIcon(ResxIconId.X)` (enum name equals the resource key). Add new icons here as `StreamGeometry` and add a matching `ResxIconId` entry in `Resx.cs`. Platform stock icons (window/system bitmaps) come from `Resx.GetStockIcon(StockIconId.X)` / `Resx.GetDefaultWindowIcon()`.

### 4. Fonts: `Common/Types/Const.cs:81-104`
- **Font family**: `Const.FONT_CODE` (OS-specific monospace stack for code / credits / metadata text).
- **Font sizes**: `Const.FONT_SIZE_BODY`, `FONT_SIZE_TITLE`, `FONT_SIZE_SUBTITLE`, `FONT_SIZE_SMALL`. Reference in XAML via `{x:Static types:Const.FONT_SIZE_SMALL}` (with `xmlns:types="using:ImageGlass.Common.Types"`); do not hardcode sizes.

---

## Critical Base Classes & Patterns

### 1. Reactive Programming — `PhReactive`
- All ViewModels inherit from `PhReactive : INotifyPropertyChanged`
- Thread-safe property change notifications using `Lock` (not `object`)
- Use with Avalonia compiled bindings: `{CompiledBinding PropertyName}`
- Example:
  ```csharp
  public class MainWindowViewModel : PhReactive
  {
      public string Title
      {
          get; set
          {
              if (field.Equals(value)) return;
              field = value;
              OnPropertyChanged();
          }
      } = BHelper.AppName;
  }
  ```

### 2. Resource Management — `PhDisposable`
- Inherit when managing unmanaged resources (SKImage, file handles, etc.)
- Override `OnDisposing()` (called on Dispose, not destructor)
- Uses `InterlockedBool _isDisposed` for thread-safe disposal checks
- Use with `await using` in async contexts
- Example:
  ```csharp
  public sealed class MipmapTileCache : PhDisposable
  {
      protected override void OnDisposing()
      {
          lock (_lock)
          {
              foreach (var bitmap in _tiles.Values)
              {
                  bitmap.Dispose();
              }
              _tiles.Clear();
          }
      }
  }
  ```

### 3. Thread-Safe Operations — `InterlockedBool`
- Use instead of `bool` for concurrent state flags
- Atomic read/write using `Volatile` and `Interlocked` operations
- Example: `_isPreviewing`, `_isFirstDraw`, `_isDisposed` in ViewerControl
- Check with `.Value` property or implicit conversion

### 4. SkiaSharp Safety — `IsDisposed()` Extension
- Always check `SKObject.IsDisposed()` before using SkiaSharp objects
- Defined in `ImageGlass.Common.Extensions.SKObject_Exts`
- Returns `true` if object is `null` or `Handle == IntPtr.Zero`
- Example:
  ```csharp
  if (_skImage.IsDisposed()) { _skImage = null; }
  ```

---

## Critical Workflows & Commands

### Build
```powershell
# Restore and build
dotnet build

# Publish AOT-enabled binary (ImageGlass.Win32)
dotnet publish ImageGlass.Win32 -c Release -o ./bin/publish

# Run from source
dotnet run --project ImageGlass.Win32 -- [image_path] [additional_args]
```

### Debug
- Open solution in Visual Studio 2026
- Set `ImageGlass.Win32` as startup project
- **Breakpoints work normally in C# code**
- **Avalonia Hot Reload**: Available with `--debug` flag (limited to XAML/styles)
- **Profiling**: Use Visual Studio's memory profiler for cache leak detection

### Testing
- No dedicated test projects in v10 (focus on functional validation)
- Manual testing via `dotnet run`
- Test command-line parsing with sample images
- Profile memory usage with large images and GIF animations

### Key C# Version & Langversion
- **LangVersion**: `Preview` (C# 14 features available)
- **Nullable**: `enable` across projects
- **AllowUnsafeBlocks**: `true` (for P/Invoke, SkiaSharp interop)
- **PublishAot**: `true` in all platform projects (Win32, Linux, Mac)
- **PublishTrimmed**: `true` in all projects (library + platform)
- **PublishSingleFile** + **SelfContained** + **PublishReadyToRun**: `true` in all platform projects
- **DisableRuntimeMarshalling**: `true` in Win32 only (AOT-safe marshalling)

---

## Key Integration Points

### 1. Photo Loading Pipeline
```
App.Initialize() 
  → Core.InitializeAsync() 
    → PhotoManager.Add/SearchAsync()
    → ViewerControl.LoadPhotoAsync(photoPath)
      → Codec.DecodeAsync() (SkiaCodec or MagickCodec with fallback)
      → shrink to fit the pixel ceiling if needed → Photo.DecodeScale
      → PhotoRenderer.RenderAsync()
      → AnimatorImpl for GIF/WEBP animation
    → PhotoLoading/PhotoAnimatorFrameChanged events
    → MipmapTileCache.Create() if image > 8192×8192
```

### Oversized image decode
- Images past the `int.MaxValue`-byte ceiling are decoded smaller rather than refused.
- **Skia** (`SkiaCodec.GetDecodableImageInfo`): steps down through the codec's native scales, JPEG eighths largest-first. Non-JPEG codecs cannot scale and throw a readable `NotSupportedException`.
- **Magick** (`SkiaCodec.FromMagick`): resizes in place to fit, so it lands closer to the ceiling than Skia can. Callers must be handing over an image they are about to discard.
- The reduction flows `SkiaDecoderOutput`/`MagickCodecAdapter` → `CodecDecodeResult.DecodeScale` → `Photo.DecodeScale`, and `AppStatusInfo.Dimension` shows it.
- `Photo.Size` and `ViewerControl.BitmapSize` follow the DECODED size so zoom, selection, crop, and the color picker stay consistent; only `Photo.Metadata.Width/Height` keep the true file dimensions.

### 2. Theme/Localization Changes
- Change language: `Core.LanguageChanged?.Invoke()`
- Change theme: `Core.ThemeChanged?.Invoke(new ThemePackChangedEventArgs(...))`
- UI elements auto-refresh via bindings + event subscribers

### 3. Win32 Service Registration
Occurs in `Program.cs` before Avalonia app setup (Linux/Mac have equivalent registrations):
- `FileSearchProvider` (native shell enumeration with Explorer sort order support)
- `ShellProvider` (context menu, file properties, Windows Share API)
- `PrintProvider` (Windows Print API)
- `ColorProfileProvider` (Monitor color profile retrieval via Win32 APIs)
- `PreviewProvider` (thumbnail cache via Windows shell)

### 4. Configuration Persistence
- `Config.cs` handles JSON serialization with custom converters
- Converters in `Common/Types/JsonTypeConverters/` (e.g., `JsonStringToHotkeyConverter`, `JsonArrayToZoomFactorConverter`)
- Cached in `AppData\Local\ImageGlass\`, or the startup dir in portable mode (see below)
- Async save via `Config.SaveAsync()`

### Portable mode (`ConfigMode`)
- A `.igportable` marker file (`Const.PORTABLE_MARKER_FILE`) in the **startup dir** moves the whole config dir there: `BHelper.ConfigPath` returns `BasePath` instead of `%LocalAppData%\ImageGlass`, so `igconfig.json`, `_cache`, `_logs`, `_plugins`, `_lang`, `_themes` all live next to the exe and the folder can be relocated without losing settings.
- `ConfigMode` only answers **which mode** (`IsPortable`, resolved once per process on first access); `BHelper.ConfigPath` stays the single accessor that turns that into a path, so every existing `ConfigDir()` caller follows automatically. Never expose a second config-path property, and never re-read the marker at a new call site.
- The marker must be resolved before anything touches the config dir, so `App.InitializeAppInstance` forces it (trace mark `InitInstance:portableMode`) ahead of `LicenseService.LoadActive` and `Config.Load`.
- **A marker in a non-writable startup dir is fatal, never a fallback.** `ConfigMode.PortableError` holds the raw exception from a single create-probe (a per-process temp file, never the marker itself), `App.OnFrameworkInitializationCompleted` shows it (real message + `GetExceptionDetails`) and quits before creating the main window, and `Config.SaveAsync` refuses to write. Falling back to `%LocalAppData%` would hide the portable settings behind a second config.
- `ConfigDir()` swallows directory-creation failures on purpose: a read-only config dir must not throw out of a path getter, or startup crashes before it can report the real problem.
- Shipped by the **ZIP packer only** (`__assets/win/script-pack-win-zip.ps1`, `-NoPortable` to omit). Never add it to `__assets/__app`: the MSIX payload folder is read-only, so a packaged build would refuse to start.

### 5. Photo Caching Strategy
- **Tier 1**: Current photo in viewer (always loaded)
- **Tier 2**: Neighboring photos preloaded via `PhotoManager.RequestCacheAround()`
- **Tier 3**: LRU eviction when memory budget exhausted
- Spiral pattern: right-1, left-1, right-2, left-2, ... to balance browsing directions

### File search & captured file metadata
- `FileSearchProvider.EnumerateFileEntries` captures `FileInfo` (size, timestamps, attributes) during enumeration into a `FileSearchEntry`, and `PhotoManager.Add(IEnumerable<FileSearchEntry>)` builds `Photo`/`PhotoMetadata` from it, so no per-file re-stat. On Windows those fields come free from `WIN32_FIND_DATA`; on Unix they cost one `stat`, still fewer than before.
- `PhotoMetadata(FileSearchEntry)` deliberately skips `SetFilePath__` (which re-stats). It is a **partial** fill: `IsOutdated()` stays true until a codec loads real image metadata, so the capture *defers* the codec metadata load rather than replacing it.
- Enumeration emits `FileInfo.FullName`, i.e. absolute paths, and `PhotoManager.IndexOf` is an exact-string `ConcurrentDictionary` lookup (`OrdinalIgnoreCase`). Every path entering `StartLoadingFiles` must therefore pass through `BHelper.ResolvePath` or the init photo never resolves to a list index (gallery shows nothing selected, navigation starts from -1).
- `FileSearchingEventArgs.Results` is a materialized `IReadOnlyList`, and filtering/sorting now run on the background thread instead of lazily on the UI thread during `Add`.
- The **Win32** override publishes progress through `Dispatcher.UIThread.Post` with a token re-check *inside* the posted lambda, which is what serializes a stale batch against `PhotoManager.Clear()`. The base provider (Linux/macOS) still invokes `progressFn` directly on the worker, so that race remains open there.

### Gallery thumbnail pipeline (`Photo.LoadThumbnailAsync`)
- Tiers in order, all skipped when `useCache` is false: in-memory `GalleryThumbnail`, `ThumbnailDiskCache`, `IPhotoPreviewProvider.TryGetCachedThumbnailAsync` (platform cache), then `GetThumbnailAsync` (decodes).
- Both cache tiers run **before** `LoadMetadataAsync`; that ordering is the whole point, since neither may read the source file. `ThumbnailDiskCache.TryGetAsync` therefore validates against the `Metadata.FileLastWriteTimeUtc` captured at enumeration, not a fresh stat, so an out-of-band edit is corrected by the file watcher rather than by the cache check.
- `_galleryThumbnailRequestSize` keys the in-memory thumbnail to the size it was produced for, so a DPI or `Config.ThumbnailSize` change reloads instead of serving a stale size.
- **Shrink whatever a cache returns** to `thumbSize` via `SkiaCodec.ScaleDown` before `ToWritableBitmap`. The Windows Shell hands back whole cache tiers (96/256/768/1920), and retaining one costs MBs of gallery memory per photo. Keep the *original* cached image as the size authority for `IsPreviewLargeEnough` and the too-small check.
- **Never nest a process-wide gate inside this call.** The caller already holds a `_thumbnailThrottleLock` slot (`clamp(ProcessorCount-1, 4, 16)`), so a second semaphore parks threads while they hold a slot and collapses the gallery to one photo at a time.
- `TryGetCachedThumbnailAsync` must not decode: Win32 implements it with `SIIGBF_INCACHEONLY`, and the base returns `null`, so Linux/macOS have only the disk tier. Pass `skipPlatformCache` down to `GetThumbnailAsync`/`GetPreviewAsync` when the caller already probed, or the same shell query runs twice per photo.
- `GalleryThumbnail` is assigned on the UI thread (`ShowGalleryThumbnailAsync`) because the compiled binding writes `Image.Source` on whichever thread raises `PropertyChanged`.

### 6. Color Management (`Config.ColorProfile`)
- The display profile is applied at **two** points; never let both hit the same pixels:
  - **Decode-time (Magick only)**: `MagickCodec.ProcessMagickImage__()` bakes it via `TransformColorSpace`; runs only when `MagickCodecAdapter` decodes (Skia decode applies no profile).
  - **Viewer-time (Skia)**: `ViewerControl.TryApplySkiaColorSpace()` (HDR tone-map + SDR ICC), applied in `HandlePhotoLoadedAsync()`.
- **CMYK profiles**: `SKColorSpace.CreateIcc()` returns `null` for CMYK → `Core.IsDestColorProfileSupported=false` → `SkiaCodecAdapter.CanDecode` refuses → Magick decodes. `ProcessMagickImage__` must never output CMYK pixels (they render inverted): cast `refImgM.ColorSpace = ColorSpace.sRGB` (keeps the print-like tint; do not ICC round-trip, it over-brightens).
- **Codec-selection cache**: `Core.UpdateDestColorProfile()` calls `CodecRegistry.InvalidateSelectionCaches()` and always fires `ColorProfileChanged`. Without the invalidation, a CMYK choice leaves `.jpg` stuck on Magick ("sticky downgrade") and every later profile double-applies (brighter/over-saturated until restart).
- **On change**: `Core_ColorProfileChanged()` re-decodes the current photo via `SetPhotoAsync(photo, { ResetZoom = false, UseCache = false })` — re-applies cleanly while keeping zoom/pan; never transforms `_imgSource` in place. `ColorProfile` is intentionally NOT in `SettingsViewModel.reloadPhoto` (a generic reload would reset zoom).
- **Monitor change vs settings change**: `UpdateDestColorProfile(requiresPhotoReload)` carries the intent through `DestColorProfileChangedEventArgs`. The monitor-profile provider passes `false` (dragging the window between monitors must not flash the viewer, matching the `Settings_CurrentMonitorProfile_Description` note); the settings page uses the default `true`. The profile still updates for later loads either way.

---

## Code Style & Best Practices

### General
- **Comments**: Explain *why*, not what; only add if needed. **Write one line.** Two only when genuinely unavoidable, never three; that ceiling covers XML doc tags (`<param>`, `<summary>`) exactly as much as `//` and `<!-- -->`. If the reasoning does not fit on one line, it belongs in the commit message or the PR, not in the source.
- **XML documentation comments**: For C# classes, methods, and public properties in infrastructure / coordination code (plugins, tools, host bridges, process managers, IPC handlers, similar files), keep XML docs present and current.
- **XML summary format**: Never use single-line XML summaries like `/// <summary>Text</summary>`. Always use the multi-line form:
  ```csharp
  /// <summary>
  /// Text
  /// </summary>
  ```
- **Long/complex methods**: Add brief inline comments for the main implementation phases so the control flow is easy to scan.
- **Async/Await**: Use `ConfigureAwait(false)` in library code; omit in UI entry points
- **Timeouts**: Use `CancellationTokenSource.CancelAfter()` for time-based cancellation
- **Cancellation**: Always check `token.IsCancellationRequested` in loops; throw on cancellation

### Avalonia-Specific
- **Reuse UI components**: when building UI, always check and reuse the existing controls in `ImageGlass.Lib/UI/` (e.g. `PhButton`, `PhTextBox`, `PhTextBlock` in `UI/BaseControls/`) instead of raw Avalonia controls; use the app's style resources via `Resx`/`ResxId` (`Common/Types/Resx.cs`) rather than hardcoding colors/brushes. See the **Designing UI** section for the full controls/resources/icons/fonts inventory.
- **Platform-conditional visibility**: hide/show controls per platform with the XAML `OnPlatform` markup extension directly on `IsVisible`, not with `OperatingSystem.IsWindows()` in code-behind. Example: `IsVisible="{OnPlatform False, Windows=True, x:TypeArguments=x:Boolean}"` (see `UI/Toolbar/ToolbarControl.axaml`).
- **Compiled bindings**: `AvaloniaUseCompiledBindingsByDefault = true` (type-safe, zero-runtime cost). Always write `{CompiledBinding ...}` explicitly in XAML — never `{Binding ...}` — including in styles, `ControlTheme`s, and `DataTemplate`s. Compiled bindings need an `x:DataType` in scope; for a self-binding to an untyped collection (e.g. a `DataValidationErrors` error template) set `x:DataType` on the `DataTemplate` (e.g. `coll:IEnumerable` via `xmlns:coll="using:System.Collections"`). Element/ancestor/`TemplatedParent` bindings (`#name`, `$parent[Type]`, `RelativeSource={RelativeSource TemplatedParent}`) infer their type and need no `x:DataType`.
- **Attached behaviors**: Prefer to UI trigger patterns for event handling
- **Threading**: Use `Dispatcher.UIThread.Post()` or `Dispatcher.UIThread.InvokeAsync()` for cross-thread updates
- **Resources**: XAML/PNG/ICO in `Assets/`, referenced via `avares://` protocol
- **Gestures**: Accumulate pointer events before expensive calculations; use `PhPanGestureRecognizer` and `PhPinchGestureRecognizer`
- **Animations**: Use `TopLevel.GetTopLevel(this)?.RequestAnimationFrame(callback)` for frame-rate-independent animations synced to the render loop; compute delta from the `TimeSpan` timestamp parameter; request the next frame at the end of each callback to continue the loop (see `ViewerControl_Animation.cs`, `NavButtonsOverlay.cs`)

### SkiaSharp-Specific
- **Always dispose**: SKImage, SKPaint, SKCanvas, SKMatrix (use try/finally or `using`)
- **No pooling in hot paths**: Allocation is fast; premature pooling hurts readability
- **Pixel formats**: `SKColorType.Rgba8888` for sRGB; consider `ColorSpace` for ICC
- **Bitmap to image**: use `SkiaCodec.ToSKImageNoCopy()` when the bitmap is finished (shares pixels); plain `ToSKImage()` copies the whole buffer
- **Size ceiling**: check `width * height * bytesPerPixel` against `int.MaxValue` before allocating any full-image buffer
- **Filtering**: `SKFilterQuality.High` for thumbnails; `Medium` for interactive zoom
- **Mipmap strategy**: Precompute at load time; cache tiles on-demand; preserve zoom-aware LRU

### Magick.NET-Specific (native heap safety)
A cancelled gallery scroll once crashed the app with `0xc0000374`, bucket `BlockNotBusy DOUBLE_FREE` in `Magick.Native-*.dll`. Two rules fixed it; a third suspect was ruled out.
- **Never `Ping` then `Read` into the same `MagickImage`.** `Ping` allocates a native image, and reading into that same wrapper leaves a second one to be freed twice. Probe with a throwaway `using var`, or `Dispose()` before re-reading (`MagickCodec.DecodeImageAsync` does the latter).
- **Dispose the `MagickImage` on every failure path**, including early returns and `catch`. An abandoned partially-read image handed to the finalizer is what turns the above into a delayed crash that surfaces at an unrelated allocation, pointing nowhere near the read.
- **Passing a `CancellationToken` to `ReadAsync` is fine** and was tested: it is NOT the cause. Cancellation only *exposed* the two bugs above, because a fast scroll cancels many in-flight reads at once. Do not remove tokens from Magick reads chasing this class of crash; look for an undisposed or double-allocated image instead.
- Applies to any path a fast gallery scroll can cancel: `QuickDecodeAsync` is reached from `PhotoPreviewProvider.GetThumbnailAsync`, whose token is cancelled on every container recycle.

### Performance-Critical Code
- **Mipmap caching**: Use `MipmapTileCache` for images > 8192×8192; reuse instance across frames
- **Gesture recognizers**: Accumulate points in `PhPanGestureRecognizer` and `PhPinchGestureRecognizer` before expensive math
- **File searching**: Async + debounce in `PhotoManager_FileWatcher.cs`; use `IFileSearchProvider` for platform-specific optimizations
- **Photo preloading**: Use spiral pattern in `PhotoManager_Caching.RunCacheAroundAsync()` to balance memory and responsiveness

---

## Important Files to Know

| File/Folder | Purpose |
|---|---|
| `Core.cs`, `Core_Events.cs` | Global event hub and singleton access; service provider slots |
| `App.xaml.cs` | Avalonia app lifecycle, theme/lang initialization |
| `ImageGlass.Win32/Program.cs` | Entry point, service registration, AOT bootstrap |
| `PhotoManager.cs` + `PhotoManager_*.cs` | Photo collection, file search, caching (LRU), file watching |
| `ViewerControl.cs` + `ViewerControl_*.cs` | Image rendering, zoom/pan, selection, animation, nav buttons |
| `UI/Viewer/Renderer/MipmapTileCache.cs` | Tiled mipmap cache with LRU eviction |
| `Common/ServiceProviders/` | Interface contracts + shared providers for cross-platform features |
| `ImageGlass.Win32/Common/ServiceProviders/` | Win32 implementations of service providers |
| `ImageGlass.Linux/Common/ServiceProviders/` | Linux implementations of service providers |
| `ImageGlass.Mac/Common/ServiceProviders/` | macOS implementations of service providers |
| `Common/ServiceProviders/AppAPIs/FeatureManager.cs` | Feature lock (`Config.LockedFeatures` API names): `IsLocked`, `IsZoomLocked`/`IsPanLocked`, `HideLockedMenuItems` |
| `Common/Types/ConfigMode.cs` | Portable-mode detection (`.igportable` marker in the startup dir) consumed by `BHelper.ConfigPath`; an unwritable folder is a fatal startup error |
| `Settings/Config_Static.cs` (`AdminLockedConfigs`/`IsConfigLocked`) + `Windows/Settings/` | Admin settings-lock: disables + refuses to save settings defined in `igconfig.admin.json` (Pro only; read by `ReadAdminConfigDocument()` from the startup dir, else the config dir) |
| `Common/AppThemes/` | Theme loading, color management (`AppThemeColors`, `IgThemeColors`) |
| `Common/Localization/Lang.cs` | Translation key registry |
| `Common/Types/Resx.cs` | Themed resource registry (`ResxId`); resolve via `{DynamicResource}` / `Resx.Get`. Also hosts icon helpers (`ResxIconId` + `GetIcon`) and platform stock icons (`StockIconId` + `GetStockIcon` / `GetDefaultWindowIcon`) |
| `Common/Types/Const.cs` | App constants: icon sizes, `FONT_CODE`, `FONT_SIZE_*` |
| `Common/Extensions/`, `Common/BHelper/`, `Common/Types/JsonTypeConverters/` | Shared extensions, helpers, AOT-safe JSON converters (check before writing new) |
| `UI/BaseControls/` | Reusable `Ph*` controls (`PhButton`, `PhTextBox`, etc.) |
| `UI/Styles/` | App-wide XAML styles + resource dictionaries (incl. `IconResources.axaml`) |
| `Directory.Packages.props` | Central package version management (Avalonia 12.1.x, SkiaSharp 3.119.x, Magick.NET 14.x, etc.) |

---

## Project-Specific Conventions

1. **No auto-generated code edits**: Skip `/obj/`, `*.g.cs`, generated Win32 P/Invoke (`NativeMethods.g.cs`)
2. **Event naming**: Use `TEventHandler<TSender, TArgs>` for type-safe events (defined in `Common/Types/TEventHandler.cs`)
3. **Weak event subscriptions**: Prefer `IDisposable` unsubscribe over weak events for clarity
4. **Config JSON**: Add custom converters in `JsonTypeConverters/` folder if new types need serialization
5. **Cross-thread calls**: Always check `IsDisposed` before invoking on disposed ViewModels/Services
6. **Horizontal code splits**: Many large classes split into partial files; search by concern (e.g., `PhotoManager_*.cs`)
7. **Lock usage**: Always use `Lock` for critical sections; avoid nested locks to prevent deadlock
8. **Resource cleanup**: Use try/finally or `using` for all `SKObject` allocations; dispose tiles in `MipmapTileCache` on eviction

---

## Tools & Build Integration

- **Tools/CodeSigning/sign_publish_files_x64.bat** (repo-level): Legacy v9 code signing script; documents publication binaries but does not apply to `v10` output paths without modification.
- **AOT Publishing**: Use `dotnet publish ImageGlass.Win32 -c Release` to generate trimmed, self-contained executable.
- **Windows packaging** (`__assets/win/`, VS Code tasks `pack-win-*`): `script-pack-win-msix.ps1` (Store + sideload MSIX) and `script-pack-win-zip.ps1` (portable ZIP; writes the `.igportable` marker unless `-NoPortable`). Both publish a fresh AOT build themselves.
- **NativeMethods.g.cs**: Auto-generated by CsWin32 from `NativeMethods.json`; do not edit manually.

---

## Debugging Tips

- **PhotoLoading hangs?** Check `_cancelPreview` in ViewerControl; may be a codec timeout
- **Memory leak?** Verify disposal of `SKImage`, `SKBitmap`, `SKSurface`, and `PhDisposable` subclasses; profile with large images
- **Theme not updating?** Ensure `Core.ThemeChanged` is invoked and subscribers listen
- **Gesture not working?** Check `PhPanGestureRecognizer`, `PhPinchGestureRecognizer` in `ZoomAndPan/`; verify point accumulation
- **Nav buttons not showing?** Check `Config.EnableNavigationButtons` binding, `NavButtonsOverlay.Background` must be `Brushes.Transparent` for hit-testing, and `EnableSelection` disables nav buttons
- **Color wrong after switching profiles (brighter/over-saturated, persists until restart)?** The codec-selection cache is stuck on Magick (double profile application); ensure `Core.UpdateDestColorProfile()` calls `CodecRegistry.InvalidateSelectionCaches()`. CMYK profile showing inverted/negative? `ProcessMagickImage__` must cast a CMYK result to `ColorSpace.sRGB`.
- **Admin-locked setting still editable or saved?** The admin settings-lock (`igconfig.admin.json` keys → `Config.AdminLockedConfigs`/`IsConfigLocked`) enforces at three points, all required: `SettingsViewModel.CommitAsync` (skip locked ids), `Config.ApplyCliOverrides` (skip locked ids), and the settings UI (`SettingsRegistry.DisableLockedControls` for `Bind*` controls + `SettingsPageView.DisableIfLocked` for composite editors). Capture runs in `Config.LoadAdminLockedConfigs()` independently of the user-config read (a corrupt `igconfig.json` must not empty the lock set). Plugins page is UI-disable-only (`PluginTrust` applies live via `PluginTrustPolicy`, never staged).
- **Whole admin layer ignored?** It is Pro-gated in `ReadAdminConfigDocument()`, so `Core.AppLicense` must be loaded before `Config.Load()` (`App.axaml.cs`). Also check the lookup order: startup dir wins, config dir is only a fallback, and the two files are never merged together.
- **Locked feature (`LockedFeatures`) still triggerable?** Only `AppAPIProvider.RunApiAsync` (both overloads) and the hotkey handler are auto-gated by `FeatureManager.IsLocked`. Any path that skips them must self-guard: context-menu items (bound via `GetApiCommand`) go through the `LockAwareApiCommand` wrapper; viewer wheel/drag/touch zoom-pan self-checks `FeatureManager.IsZoomLocked()`/`IsPanLocked()`. A new input path or a direct command execution without one of these is a bypass.
- **"Unable to allocate pixels for the bitmap" on a huge image?** Not an out-of-memory problem, so do not chase RAM or fragmentation. Skia refuses any pixel buffer over `int.MaxValue` bytes; the decode must shrink to fit first (`GetDecodableImageInfo` on the Skia path, the in-place resize in `FromMagick` on the Magick path). A new code path that allocates a full-image buffer without that check reintroduces the crash.
- **Huge image opens smaller than the file?** Expected: `Photo.DecodeScale < 1` and the status bar shows the reduction. `Photo.Metadata.Width/Height` keep the true size; everything else follows the decoded size.
- **Plugin-owned format shows a garbage gallery thumbnail (thin line, blank) while the viewer renders it fine?** A content-sniffing built-in decoder (`SKCodec.Create`, Magick) succeeded with absurd dimensions instead of failing, and that satisfies `IsPreviewLargeEnough` (longest side only), so the plugin is never asked. Every preview/thumbnail source must gate on `CodecRegistry.IsDecodingExtensionOwnedByPlugin` (via `PhotoPreviewProvider.IsPluginOwnedFormat`); trace with `--ig-photo-trace` (`preview:*` = which source won, `thumb:preview`/`thumb:codec`/`thumb:decode` = whether the slow path ran).
- **Settings not staying with the app folder (or the app quits at startup with "Cannot use portable mode")?** Portable mode is driven by the `.igportable` marker in the startup dir (`ConfigMode`). The marker must exist next to the exe AND that folder must be writable; an unwritable folder is a hard stop by design, not a fallback to `%LocalAppData%`. The ZIP packer writes the marker; the MSIX must never carry it.
- **App vanishes with no error dialog (no in-app exception, nothing logged)?** That is a native crash, so `App.UIThread_UnhandledException` never runs. Do not hunt for a managed exception. Read the real fault first: `Get-WinEvent -FilterHashtable @{LogName='Application'}` filtered to `Application Error` gives the exception code (`0xc0000374` = heap corruption, `0xc0000005` = access violation). Then bucket it with `cdb.exe -z <dump> -c ".lastevent; !analyze -v; q"` (WinDbg installs via `winget install Microsoft.WinDbg`; `cdb.exe` sits in the package's `amd64` folder). Dumps land in `%LocalAppData%\CrashDumps`. `!analyze -v` names the faulting module and whether it was a double free, which is usually the whole answer. `dotnet-dump analyze <dump> -c "clrstack -all"` then shows which managed call was in flight on that thread.
- **App vanishes on macOS with `EXC_CRASH (SIGABRT)` / `abort() called` on a `.NET TP Worker`?** That is NativeAOT `FailFast` from a *managed* exception that escaped a background thread — usually `async void`, whose `AsyncVoidMethodBuilder.SetException` rethrows with no `SynchronizationContext`. Do not chase native heap corruption. Symbolicate before theorising: crash reports are `~/Library/Logs/DiagnosticReports/ImageGlass-*.ips`, and `ImageGlass.Mac/bin/<Platform>/Release/net10.0/osx-arm64/native/ImageGlass.dSYM` matches when `dwarfdump --uuid` equals the report's UUID. Then `atos -o <dSYM>/Contents/Resources/DWARF/ImageGlass -arch arm64 -l <load addr> <frame addrs>` names the exact method — the frame under `AsyncVoidMethodBuilder__SetException` is the culprit.
- **An `async void` outside a UI event handler is a crash, not a bug.** On the UI thread `Dispatcher.UIThread.UnhandledException` catches it; on any worker it kills the process silently. Every `async void` reachable from a worker (`Photo.OnDisposing`/`Unload`, `GalleryItem.LoadThumbnail`, file-search callbacks) must wrap its whole body in `try/catch (Exception)`. `AppDomain.CurrentDomain.UnhandledException` cannot save it: the process is torn down as soon as the handler returns, so a `Dispatcher.Post` there never renders.
- **Works on Windows but crashes on macOS/Linux in the file-search path?** Only `Win32FileSearchProvider` overrides `SearchAsync` to publish through `Dispatcher.UIThread.Post` with a token re-check; the base provider invokes `progressFn` straight from its `Task.Run` worker. So a search callback that touches `Core.Photos.Items` (bound to the gallery) or any control runs off-thread on macOS/Linux only. Marshal the *whole* callback body, not just the `Add`.
- **Gallery thumbnails load twice per item, or a wrong-size thumbnail flashes then gets replaced?** `PhVirtualizingUniformPanel.GetOrCreateElement` sets `DataContext` *before* `AddInternalChild`, so a brand-new `GalleryItem` is still detached when its `DataContextProperty` handler runs, `TopLevel.GetTopLevel(this)` is null, and `Dpi` silently falls back to `1`. Gate that handler on `IsLoaded`: `OnLoaded` covers the first attach, and recycled containers stay loaded (`RecycleElement` only sets `IsVisible = false`, it never removes the child) so the handler remains their only trigger.
- **Gallery shows nothing selected after opening a file (and navigation starts from -1)?** The init photo path did not match the enumerated form. Enumeration emits absolute `FileInfo.FullName` and `IndexOf` is an exact-string lookup, so a relative launch path such as `dotnet run --project ImageGlass.Win32 -- pics\a.jpg` misses unless it went through `BHelper.ResolvePath`.
- **No slideshow notification sound?** `BHelper.PlayNotificationSoundAsync()` plays the bundled `Assets/Sounds/beep.wav` via NetCoreAudio, never a system beep. That package needs winmm MCI on Windows, `afplay` on macOS, and `/bin/bash` + `aplay` (alsa-utils) on Linux, so a distro without alsa-utils is silent by design.
- **Serialization fails?** Validate JSON converter exists in `Common/Types/JsonTypeConverters/`
- **Cache not evicting?** Check `MipmapTileCache.MAX_CACHED_TILES` (100) and LRU promotion logic
- **AOT trimming errors?** Review trimmer warnings; add `[DynamicallyAccessedMembers]` annotations or custom converters

---

## When Adding New Features

1. **Cross-platform?** Define interface in `Common/ServiceProviders/`; implement in each platform's `Common/ServiceProviders/` (Win32, Linux, Mac) and register via `Core.API` or service slots
2. **User-facing string?** Add to `Lang.cs` enum, then reference via `Lang.GetString(LangId.KeyName)`
3. **New codec?** Inherit from codec base (SkiaCodec or MagickCodec); register in `PhotoManager`
4. **UI control?** Inherit from `PhControl` or `PhWindow`; use `PhReactive` for ViewModels
5. **Settings?** Add property to `Config.cs`; if complex JSON, create converter in `JsonTypeConverters/`
6. **Async operation with cancellation?** Accept `CancellationToken ct`, pass to subtasks, call `ct.ThrowIfCancellationRequested()`
7. **Memory-critical?** Test with large images (8192×8192+) and profile cache behavior
8. **Threaded work?** Use `Lock` for shared state; avoid blocking UI thread; prefer async/await over Thread
