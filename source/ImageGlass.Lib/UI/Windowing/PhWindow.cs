/*
ImageGlass - A Fast, Seamless Photo Viewer
Copyright (C) 2010 - 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ImageGlass.Common;
using ImageGlass.Common.AppThemes;
using ImageGlass.Common.Extensions;
using ImageGlass.Common.Photoing;
using ImageGlass.Common.Types;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ImageGlass.UI.Windowing;


public partial class PhWindow : Window
{
    protected bool _canUseBackdrop = false;

    protected static Color DefaultActivateBg => Core.Theme.Settings.IsDarkMode
        ? AppThemeColors.BackgroundActivateDark
        : AppThemeColors.BackgroundActivateLight;

    protected static Color DefaultInactivateBg => Core.Theme.Settings.IsDarkMode
        ? AppThemeColors.BackgroundInactivateDark
        : AppThemeColors.BackgroundInactivateLight;



    #region Public Properties

    /// <summary>
    /// Gets the handle of this window.
    /// </summary>
    public nint Handle => GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;


    /// <summary>
    /// Gets the DPI scale of this window.
    /// </summary>
    public double Dpi => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;


    /// <summary>
    /// Gets, sets the value indicates that if this window uses a custom backdrop.
    /// </summary>
    public virtual bool UseCustomBackdrop { get; set; } = false;


    /// <summary>
    /// Gets, sets the value indicates that the window icon won't be loaded by default.
    /// </summary>
    public virtual bool UseCustomWindowIcon { get; set; } = false;


    /// <summary>
    /// Gets, sets the window backdrop style.
    /// </summary>
    public BackdropStyle BackdropStyle
    {
        get => GetValue(BackdropStyleProperty);
        set => SetValue(BackdropStyleProperty, value);
    }
    public static readonly StyledProperty<BackdropStyle> BackdropStyleProperty =
        AvaloniaProperty.Register<Window, BackdropStyle>(nameof(BackdropStyle), BackdropStyle.None);



    /// <summary>
    /// Gets, sets the hotkey to close the window with.
    /// </summary>
    public Hotkey[] CloseWindowHotkeys
    {
        get => GetValue(CloseWindowHotkeysProperty);
        set => SetValue(CloseWindowHotkeysProperty, value);
    }
    public static readonly StyledProperty<Hotkey[]> CloseWindowHotkeysProperty =
        AvaloniaProperty.Register<Window, Hotkey[]>(nameof(CloseWindowHotkeys), []);



    /// <summary>
    /// Gets, sets the value indicates that the app icon is shown on the title bar.
    /// The taskbar icon is not affected.
    /// </summary>
    public bool ShowTitleBarIcon
    {
        get => GetValue(ShowTitleBarIconProperty);
        set => SetValue(ShowTitleBarIconProperty, value);
    }
    public static readonly StyledProperty<bool> ShowTitleBarIconProperty =
        AvaloniaProperty.Register<Window, bool>(nameof(ShowTitleBarIcon), true);



    /// <summary>
    /// Gets, sets the frameless mode.
    /// </summary>
    public bool IsFrameless
    {
        get => GetValue(IsFramelessProperty);
        set => SetValue(IsFramelessProperty, value);
    }
    public static readonly StyledProperty<bool> IsFramelessProperty =
        AvaloniaProperty.Register<Window, bool>(nameof(IsFrameless), false);


    #endregion // Public Properties



    public PhWindow()
    {
        OnIgFramelessModeChanged(IsFrameless);
        if (BackdropStyle == BackdropStyle.None)
        {
            UpdateBackground(true);
        }

        Core.ThemeChanged += Core_ThemeChanged;
        Core.LanguageChanged += Core_LanguageChanged;
    }



    #region Events & Override methods

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        OnIgLanguageChanged();

        // needs a live window handle, so it cannot be applied when the property is set
        OnIgTitleBarIconVisibilityChanged(ShowTitleBarIcon);

        DetachImeWhenNotEditingText();
    }


    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        ActualThemeVariantChanged += PhWindow_ActualThemeVariantChanged;
        Activated += PhWindow_Activated;
        Deactivated += PhWindow_Deactivated;
    }


    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        ActualThemeVariantChanged -= PhWindow_ActualThemeVariantChanged;
        Activated -= PhWindow_Activated;
        Deactivated -= PhWindow_Deactivated;

        Core.ThemeChanged -= Core_ThemeChanged;
        Core.LanguageChanged -= Core_LanguageChanged;
    }


    private void PhWindow_ActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (UseCustomBackdrop) return;

        if (IsActive) PhWindow_Activated(sender, e);
        else PhWindow_Deactivated(sender, e);
    }


    private async void PhWindow_Activated(object? sender, EventArgs e)
    {
        OnIgActivated(e);

        // another window may have re-attached the IME while we were inactive
        DetachImeWhenNotEditingText();


        // handle built-in backdrop style
        if (UseCustomBackdrop) return;
        if (_canUseBackdrop)
        {
            await AnimateBackgroundColorAsync(DefaultActivateBg.A(0));
        }
    }


    private async void PhWindow_Deactivated(object? sender, EventArgs e)
    {
        OnIgDeactivated(e);


        // handle built-in backdrop style
        if (UseCustomBackdrop) return;
        if (_canUseBackdrop)
        {
            await AnimateBackgroundColorAsync(DefaultInactivateBg);
        }
    }


    private void Core_ThemeChanged(object? sender, ThemePackChangedEventArgs e)
    {
        // update app icon
        if (!UseCustomWindowIcon)
        {
            _ = UpdateWindowIconAsync();
        }

        UpdateBackground(IsActive);
        OnIgThemeChanged(e);
    }


    private void Core_LanguageChanged(object? sender, EventArgs e)
    {
        OnIgLanguageChanged();
    }


    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // BackdropStyle
        if (e.Property == BackdropStyleProperty)
        {
            OnIgBackdropStyleChanged((BackdropStyle)e.NewValue!);
        }

        // IsFrameless
        else if (e.Property == IsFramelessProperty)
        {
            OnIgFramelessModeChanged((bool)e.NewValue!);
        }

        // ShowTitleBarIcon
        else if (e.Property == ShowTitleBarIconProperty)
        {
            OnIgTitleBarIconVisibilityChanged((bool)e.NewValue!);
        }

        // a new window icon (theme change) resets the title bar icon, so re-apply
        else if (e.Property == IconProperty)
        {
            OnIgTitleBarIconVisibilityChanged(ShowTitleBarIcon);
        }
    }


    protected override void OnKeyDown(KeyEventArgs e)
    {
        // check if the hotkey for closing window is pressed
        foreach (var hk in CloseWindowHotkeys)
        {
            if (hk.IsSame(e.Key, e.KeyModifiers))
            {
                OnIgCloseWindowHotkeyPressed(e);
                if (!e.Handled)
                {
                    e.Handled = true;
                    Close();
                    return;
                }

                break;
            }
        }


        base.OnKeyDown(e);
    }


    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Frameless mode: enable window dragging at top border
        if (IsFrameless)
        {
            var p = e.GetCurrentPoint(this);
            if (p.Properties.IsLeftButtonPressed && p.Position.Y < 15) BeginMoveDrag(e);
        }
    }


    #endregion // Events & Override methods



    #region Virtual methods

    /// <summary>
    /// Occurs when the window is activated.
    /// </summary>
    protected virtual void OnIgActivated(EventArgs e) { }


    /// <summary>
    /// Occurs when the window is deactivated.
    /// </summary>
    protected virtual void OnIgDeactivated(EventArgs e) { }


    /// <summary>
    /// Occurs when one of the hotkey for closing window is pressed.
    /// </summary>
    protected virtual void OnIgCloseWindowHotkeyPressed(KeyEventArgs e) { }


    /// <summary>
    /// Occurs when the frameless mode is changed.
    /// </summary>
    protected virtual void OnIgFramelessModeChanged(bool enable)
    {
        if (enable)
        {
            ExtendClientAreaToDecorationsHint = true;
            WindowDecorations = WindowDecorations.BorderOnly;
        }
        else
        {
            ExtendClientAreaToDecorationsHint = false;
            WindowDecorations = WindowDecorations.Full;
        }

        // the restored title bar comes back without the app icon; re-assert it once the
        // platform has rebuilt the frame
        Dispatcher.Post(
            () => OnIgTitleBarIconVisibilityChanged(ShowTitleBarIcon),
            DispatcherPriority.Background);
    }


    /// <summary>
    /// Occurs when the visibility of the title bar icon is changed.
    /// Does nothing by default; platforms that draw an app icon on the title bar override this.
    /// </summary>
    protected virtual void OnIgTitleBarIconVisibilityChanged(bool show) { }


    /// <summary>
    /// Occurs when the app theme is changed.
    /// </summary>
    protected virtual void OnIgThemeChanged(ThemePackChangedEventArgs e) { }


    /// <summary>
    /// Occurs when the app language is changed.
    /// </summary>
    protected virtual void OnIgLanguageChanged() { }


    /// <summary>
    /// Occurs whenthe backdrop style is changed.
    /// </summary>
    protected virtual void OnIgBackdropStyleChanged(BackdropStyle style)
    {
        if (style != BackdropStyle.None)
        {
            // map the built-in backdrop styles
            if (!UseCustomBackdrop)
            {
                WindowTransparencyLevel[] levels = style switch
                {
                    BackdropStyle.Mica => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None],
                    BackdropStyle.MicaAlt => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None],
                    BackdropStyle.Acrylic => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
                    _ => [WindowTransparencyLevel.None],
                };

                TransparencyLevelHint = levels;
            }
        }


        // check if we can apply window backdrop
        _canUseBackdrop = !BHelper.IsWindows10
            && !ActualTransparencyLevel.Equals(WindowTransparencyLevel.None)
            && !ActualTransparencyLevel.Equals(WindowTransparencyLevel.Transparent);


        // update background according to the backdrop
        UpdateBackground(true);
    }


    /// <summary>
    /// Updates the background color to reflect the current transparency and activation state.
    /// </summary>
    protected virtual void UpdateBackground(bool isActive)
    {
        var windowBg = isActive ? DefaultActivateBg : DefaultInactivateBg;

        // update background color for transparency
        if (_canUseBackdrop)
        {
            Background = windowBg.A(0).ToBrush();
        }
        else
        {
            Background = windowBg.ToBrush();
        }
    }

    #endregion // Virtual methods



    #region Internal Methods

    /// <summary>
    /// Detaches the OS IME while focus is outside a text field, so a CJK IME cannot swallow
    /// single keys. Avalonia only detaches it after a text field has been focused once.
    /// </summary>
    protected void DetachImeWhenNotEditingText()
    {
        if (FocusManager?.GetFocusedElement() is TextBox
            or NumericUpDown
            or MaskedTextBox
            or AutoCompleteBox) return;

        Core.ShellProvider?.DetachIme(Handle);
    }


    /// <summary>
    /// Updates icon for window and taskbar.
    /// </summary>
    protected async Task UpdateWindowIconAsync(string? customIconPath = null)
    {
        // 1. get full path of icon
        var iconPath = Core.Theme.GetIconPath(IgThemeIcon.AppLogo);
        var useDefaultIcon = !File.Exists(iconPath);


        // 2. use default icon as logo
        if (string.IsNullOrWhiteSpace(customIconPath))
        {
            if (useDefaultIcon)
            {
                // get default logo icon if theme's app logo does not exist
                Icon = Resx.GetDefaultWindowIcon();

                return;
            }
        }
        // 3. use custom icon path
        else
        {
            iconPath = customIconPath;
        }


        // 4. use theme icon as logo
        // decode the logo
        var size = DpiScale(64);
        var bytes = await MagickCodec.QuickDecodeAsync(iconPath, ImageMagick.MagickFormat.Ico, size, size);
        if (bytes is null) return;

        // update icon
        using var ms = new MemoryStream(bytes);
        Icon = new WindowIcon(ms);
    }


    /// <summary>
    /// Animates the window background color.
    /// </summary>
    protected async Task AnimateBackgroundColorAsync(Color toColor)
    {
        if (Background is not SolidColorBrush fromBrush) return;

        var fromColor = fromBrush.Color;
        var toBrush = toColor.ToBrush();
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new LinearEasing(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters = { new Setter(SolidColorBrush.ColorProperty, fromColor) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters = { new Setter(SolidColorBrush.ColorProperty, toColor) }
                }
            },
        };


        Background = toBrush;
        await animation.RunAsync(toBrush);
    }

    #endregion // Internal Methods



    #region Public Methods

    /// <summary>
    /// Scales the given number on the DPI scaling factor.
    /// </summary>
    public double DpiScale(double value, double? scaleFactor = null) => (scaleFactor ?? Dpi) * value;


    /// <summary>
    /// Scales the given size based on the DPI scaling factor.
    /// </summary>
    public Size DpiScale(Size value, double? scaleFactor = null) => new Size(DpiScale(value.Width, scaleFactor), DpiScale(value.Height, scaleFactor));


    /// <summary>
    /// Scales the given point on the DPI scaling factor.
    /// </summary>
    public Point DpiScale(Point value, double? scaleFactor = null) => new Point(DpiScale(value.X, scaleFactor), DpiScale(value.Y, scaleFactor));

    #endregion // Public Methods


}
