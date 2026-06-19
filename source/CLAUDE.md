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
- Example: `MipmapTileCache.GetTile()` disposes evicted tiles and double-checks for race conditions.
- Always dispose `SKImageRef` leases when done; use `KeepAlive()` pattern in `MipmapTileCache`.

### 4. Memory Usage Control
- Respect cache limits: `MipmapTileCache` caps 100 tiles; `PhotoManager_Caching` honors `Config.MaxMemoryCacheInMb`, `Config.MaxFileSizeCacheInMb`, `Config.MaxDimensionCache`.
- LRU eviction is mandatory in tile and photo caches to prevent unbounded growth.
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
   - Service provider slots: `ShellProvider`, `PreviewProvider`, `FileSearchProvider`, `ShareProvider`, `PrintProvider`, `API`

2. **Service Providers** (Plug-in Pattern)
   - Platform-agnostic interfaces in `ImageGlass.Lib/Common/ServiceProviders/*`
   - Platform implementations:
     - Win32: `ImageGlass.Win32/Common/ServiceProviders/*`
     - Linux: `ImageGlass.Linux/Common/ServiceProviders/*`
     - macOS: `ImageGlass.Mac/Common/ServiceProviders/*`
   - Shared concrete providers in library: `PhotoPreviewProvider`, `SlideshowProvider`, `UpdateProvider`
   - Examples: `IFileSearchProvider`, `IShareProvider`, `IShellProvider`, `IPrintProvider`, `IWindowColorProfileProvider`, `IPhotoPreviewProvider`
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
│   ├── ServiceProviders/          # Interface contracts + shared providers
│   │   ├── AppAPIs/               # App API provider interfaces
│   │   ├── FileSearchService/     # File search service abstractions
│   │   ├── Update/                # Update provider abstractions
│   │   ├── I*.cs                  # Interfaces (IFileSearchProvider, IShellProvider, etc.)
│   │   ├── PhotoPreviewProvider.cs
│   │   ├── SlideshowProvider.cs
│   │   └── UpdateProvider.cs
│   ├── Photoing/                  # Photo management, codecs, animators
│   │   ├── Manager/               # PhotoManager & related (search, watcher, caching)
│   │   ├── Codecs/                # SkiaCodec, MagickCodec
│   │   ├── Animators/             # SkiaAnimator, AnimatorImpl
│   │   └── Photos/                # Photo, PhotoMetadata, PhotoColorProfile, etc.
│   ├── AppThemes/                 # Theme system (IgTheme, IgThemeColors, IgThemeMetadata)
│   ├── Types/                     # Base classes (PhReactive, PhDisposable, InterlockedBool, etc.)
│   ├── Extensions/                # SK* and Avalonia extensions (SKObject_Exts, DrawingContext_Exts, etc.)
│   ├── BHelper/                   # Utilities (Path, ProcessHelper, JsonEx, Format, Color, etc.)
│   ├── Localization/              # Lang.cs, LangId enum
│   └── OsApi/                     # OS-level info (SystemInfo, etc.)
├── UI/
│   ├── Viewer/                    # ViewerControl (partial classes by feature)
│   │   ├── NavButtons/            # NavButtonsOverlay, NavButtonsInfo, NavButtonClickedEventArgs
│   │   ├── Renderer/              # MipmapTileCache, PhotoRenderer
│   │   ├── ZoomAndPan/            # Gesture recognizers, zoom/pan math
│   │   ├── Selection/             # Selection tools
│   │   └── (other ViewerControl_*.cs files)
│   ├── BaseControls/              # PhControl, PhWindow, PhTextBox, PhToolButton, etc.
│   ├── Converters/                # Avalonia value converters
│   ├── Toolbar/                   # Toolbar UI and model
│   ├── Gallery/                   # Gallery browsing UI
│   ├── Tools/                     # Tool UI (ColorPicker, etc.)
│   └── Windowing/                 # MainWindow, DialogWindow, ModalWindow
├── Settings/                      # Config.cs, ConfigMetadata, Config_Static.cs
├── ViewModels/                    # MainWindowViewModel, MainWindowModel, ToolbarControlModel
└── Windows/                       # MainWindow.axaml.cs, AboutWindow.cs, etc.

ImageGlass.Win32/
├── Program.cs                     # Entry point, service registration, AOT bootstrap
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
    └── ServiceProviders/          # macOS implementations (MacPrintProvider, etc.)
```

### Naming Rules
- **Classes inheriting from Control/UserControl**: Use `PhControl` base (e.g., `ToolbarControl : PhControl`)
- **View Models**: Suffix with `ViewModel` (e.g., `MainWindowViewModel : PhReactive`)
- **Async methods**: Always suffix with `Async`
- **Codecs/Providers**: Prefix with platform/source (e.g., `SkiaCodec`, `Win32FileSearchProvider`, `MagickCodec`)
- **Partial class files**: Name by concern (e.g., `ViewerControl_Render.cs`, `PhotoManager_Caching.cs`, `AppAPIProvider_Hotkeys.cs`)

---

## Critical Base Classes & Patterns

### 1. Reactive Programming — `PhReactive`
- All ViewModels inherit from `PhReactive : INotifyPropertyChanged`
- Thread-safe property change notifications using `Lock` (not `object`)
- Use with Avalonia compiled bindings: `{Binding PropertyName}`
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
      → PhotoRenderer.RenderAsync()
      → AnimatorImpl for GIF/WEBP animation
    → PhotoLoading/PhotoAnimatorFrameChanged events
    → MipmapTileCache.Create() if image > 8192×8192
```

### 2. Theme/Localization Changes
- Change language: `Core.LanguageChanged?.Invoke()`
- Change theme: `Core.ThemeChanged?.Invoke(new ThemePackChangedEventArgs(...))`
- UI elements auto-refresh via bindings + event subscribers

### 3. Win32 Service Registration
Occurs in `Program.cs` before Avalonia app setup (Linux/Mac have equivalent registrations):
- `FileSearchProvider` (native shell enumeration with Explorer sort order support)
- `ShareProvider` (Windows Share API)
- `ShellProvider` (context menu, file properties)
- `PrintProvider` (Windows Print API)
- `ColorProfileProvider` (Monitor color profile retrieval via Win32 APIs)
- `PreviewProvider` (thumbnail cache via Windows shell)

### 4. Configuration Persistence
- `Config.cs` handles JSON serialization with custom converters
- Converters in `Common/Types/JsonTypeConverters/` (e.g., `JsonStringToHotkeyConverter`, `JsonArrayToZoomFactorConverter`)
- Cached in `AppData\Local\ImageGlass\`
- Async save via `Config.SaveAsync()`

### 5. Photo Caching Strategy
- **Tier 1**: Current photo in viewer (always loaded)
- **Tier 2**: Neighboring photos preloaded via `PhotoManager.RequestCacheAround()`
- **Tier 3**: LRU eviction when memory budget exhausted
- Spiral pattern: right-1, left-1, right-2, left-2, ... to balance browsing directions

---

## Code Style & Best Practices

### General
- **Comments**: Explain *why*, not what; only add if needed. Keep comments short and straight to the point — never write long-winded/verbose comments.
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
- **Reuse UI components**: when building UI, always check and reuse the existing controls in `ImageGlass.Lib/UI/` (e.g. `PhButton`, `PhTextBox`, `PhTextBlock` in `UI/BaseControls/`) instead of raw Avalonia controls; use the app's style resources via `Resx`/`ResxId` (`Common/Types/Resx.cs`) rather than hardcoding colors/brushes.
- **Platform-conditional visibility**: hide/show controls per platform with the XAML `OnPlatform` markup extension directly on `IsVisible`, not with `OperatingSystem.IsWindows()` in code-behind. Example: `IsVisible="{OnPlatform False, Windows=True, x:TypeArguments=x:Boolean}"` (see `UI/Toolbar/ToolbarControl.axaml`).
- **Compiled bindings**: `AvaloniaUseCompiledBindingsByDefault = true` (type-safe, zero-runtime cost)
- **Attached behaviors**: Prefer to UI trigger patterns for event handling
- **Threading**: Use `Dispatcher.UIThread.Post()` or `Dispatcher.UIThread.InvokeAsync()` for cross-thread updates
- **Resources**: XAML/PNG/ICO in `Assets/`, referenced via `avares://` protocol
- **Gestures**: Accumulate pointer events before expensive calculations; use `PhPanGestureRecognizer` and `PhPinchGestureRecognizer`
- **Animations**: Use `TopLevel.GetTopLevel(this)?.RequestAnimationFrame(callback)` for frame-rate-independent animations synced to the render loop; compute delta from the `TimeSpan` timestamp parameter; request the next frame at the end of each callback to continue the loop (see `ViewerControl_Animation.cs`, `NavButtonsOverlay.cs`)

### SkiaSharp-Specific
- **Always dispose**: SKImage, SKPaint, SKCanvas, SKMatrix (use try/finally or `using`)
- **No pooling in hot paths**: Allocation is fast; premature pooling hurts readability
- **Pixel formats**: `SKColorType.Rgba8888` for sRGB; consider `ColorSpace` for ICC
- **Filtering**: `SKFilterQuality.High` for thumbnails; `Medium` for interactive zoom
- **Mipmap strategy**: Precompute at load time; cache tiles on-demand; preserve zoom-aware LRU

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
| `Common/AppThemes/` | Theme loading, color management |
| `Common/Localization/Lang.cs` | Translation key registry |
| `Directory.Packages.props` | Central package version management (Avalonia 12.0.0-rc1, SkiaSharp 3.119.x, Magick.NET 14.x, etc.) |

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
- **NativeMethods.g.cs**: Auto-generated by CsWin32 from `NativeMethods.json`; do not edit manually.

---

## Debugging Tips

- **PhotoLoading hangs?** Check `_cancelPreview` in ViewerControl; may be a codec timeout
- **Memory leak?** Verify disposal of `SKImage`, `SKBitmap`, `SKSurface`, and `PhDisposable` subclasses; profile with large images
- **Theme not updating?** Ensure `Core.ThemeChanged` is invoked and subscribers listen
- **Gesture not working?** Check `PhPanGestureRecognizer`, `PhPinchGestureRecognizer` in `ZoomAndPan/`; verify point accumulation
- **Nav buttons not showing?** Check `Config.EnableNavigationButtons` binding, `NavButtonsOverlay.Background` must be `Brushes.Transparent` for hit-testing, and `EnableSelection` disables nav buttons
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
