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
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ImageGlass.Common;
using ImageGlass.Common.Types;
using System;
using System.ComponentModel;

namespace ImageGlass.UI;

public class PhButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);


    // events
    public event TEventHandler<ContextMenu, CancelEventArgs>? DropdownOpening;
    public event TEventHandler<ContextMenu, RoutedEventArgs>? DropdownOpened;
    public event TEventHandler<ContextMenu, CancelEventArgs>? DropdownClosing;
    public event TEventHandler<ContextMenu, RoutedEventArgs>? DropdownClosed;


    #region Public Properties

    /// <summary>
    /// Gets the DPI scale value.
    /// </summary>
    public double Dpi => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;


    /// <summary>
    /// Gets, sets the icon geometry data.
    /// </summary>
    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
    public static readonly StyledProperty<Geometry?> IconDataProperty =
        AvaloniaProperty.Register<PhButton, Geometry?>(nameof(IconData));


    /// <summary>
    /// Gets, sets the text of this button.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<PhButton, string>(nameof(Text));


    /// <summary>
    /// Gets, sets the value indicates that the button is an accent button.
    /// </summary>
    public bool IsAccent
    {
        get => GetValue(IsAccentProperty);
        set => SetValue(IsAccentProperty, value);
    }
    public static readonly StyledProperty<bool> IsAccentProperty =
        AvaloniaProperty.Register<PhButton, bool>(nameof(IsAccent));


    /// <summary>
    /// Gets, sets the value indicates that the icon is visible.
    /// </summary>
    public bool IsIconVisible => IconData != null;
    public static readonly DirectProperty<PhButton, bool> IsIconVisibleProperty =
        AvaloniaProperty.RegisterDirect<PhButton, bool>(nameof(IsIconVisible), i => i.IsIconVisible);


    /// <summary>
    /// Gets, sets the value indicates that the text is visible.
    /// </summary>
    public bool IsTextVisible => !string.IsNullOrWhiteSpace(Text);
    public static readonly DirectProperty<PhButton, bool> IsTextVisibleProperty =
        AvaloniaProperty.RegisterDirect<PhButton, bool>(nameof(IsTextVisible), i => i.IsTextVisible);


    /// <summary>
    /// Gets, sets the value indicates that the button is checked on click.
    /// </summary>
    public bool IsCheckOnClick
    {
        get => GetValue(IsCheckOnClickProperty);
        set => SetValue(IsCheckOnClickProperty, value);
    }
    public static readonly StyledProperty<bool> IsCheckOnClickProperty =
        AvaloniaProperty.Register<PhButton, bool>(nameof(IsCheckOnClick));


    /// <summary>
    /// Gets, sets the value indicates that the dropdown menu is open on click.
    /// </summary>
    public bool IsOpenDropdownMenuOnClick
    {
        get => GetValue(IsOpenDropdownMenuOnClickProperty);
        set => SetValue(IsOpenDropdownMenuOnClickProperty, value);
    }
    public static readonly StyledProperty<bool> IsOpenDropdownMenuOnClickProperty =
        AvaloniaProperty.Register<PhButton, bool>(nameof(IsOpenDropdownMenuOnClick));


    /// <summary>
    /// Gets, sets the dropdown menu of this button.
    /// </summary>
    public ContextMenu? DropdownMenu
    {
        get => GetValue(DropdownMenuProperty);
        set
        {
            DropdownMenu?.Opening -= DropdownMenu_Opening;
            DropdownMenu?.Opened -= DropdownMenu_Opened;
            DropdownMenu?.Closing -= DropdownMenu_Closing;
            DropdownMenu?.Closed -= DropdownMenu_Closed;

            SetValue(DropdownMenuProperty, value);
        }
    }
    public static readonly StyledProperty<ContextMenu?> DropdownMenuProperty =
        AvaloniaProperty.Register<PhButton, ContextMenu?>(nameof(DropdownMenu));


    #endregion // Public Properties



    public PhButton()
    {
        Content = CreateContentElement();
    }




    #region Control Events

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        OnIgLanguageChanged();
        Core.ThemeChanged += Core_ThemeChanged;
        Core.LanguageChanged += Core_LanguageChanged;
    }


    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        Core.ThemeChanged -= Core_ThemeChanged;
        Core.LanguageChanged -= Core_LanguageChanged;
    }


    protected override void OnClick()
    {
        if (IsOpenDropdownMenuOnClick)
        {
            OpenDropdownMenu();
        }

        base.OnClick();
    }


    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == DropdownMenuProperty)
        {
            DropdownMenu?.Opening -= DropdownMenu_Opening;
            DropdownMenu?.Opened -= DropdownMenu_Opened;
            DropdownMenu?.Closing -= DropdownMenu_Closing;
            DropdownMenu?.Closed -= DropdownMenu_Closed;

            DropdownMenu?.Opening += DropdownMenu_Opening;
            DropdownMenu?.Opened += DropdownMenu_Opened;
            DropdownMenu?.Closing += DropdownMenu_Closing;
            DropdownMenu?.Closed += DropdownMenu_Closed;
        }
        else if (e.Property == TextProperty)
        {
            RaisePropertyChanged(IsTextVisibleProperty, default, IsTextVisible);
        }
        else if (e.Property == IconDataProperty)
        {
            RaisePropertyChanged(IsIconVisibleProperty, default, IsIconVisible);
        }
        else if (e.Property == IsAccentProperty)
        {
            if (IsAccent)
            {
                Classes.Add("accent");

                // The Fluent accent style only colors the inner ContentPresenter, not the
                // button's Foreground. Our custom content (icon + text) binds to Foreground,
                // so drive it from the accent-aware AccentButtonForeground resource to keep
                // the text readable on dark/light system accents.
                this[!ForegroundProperty] = Resx.CreateBinding(ResxId.AccentButtonForeground);
            }
            else
            {
                Classes.Remove("accent");
                ClearValue(ForegroundProperty);
            }
        }
    }


    private void DropdownMenu_Opening(object? sender, CancelEventArgs e)
    {
        OnIgDropdownMenuOpening(e);
    }


    private void DropdownMenu_Opened(object? sender, RoutedEventArgs e)
    {
        OnIgDropdownMenuOpened(e);
    }


    private void DropdownMenu_Closing(object? sender, CancelEventArgs e)
    {
        OnIgDropdownMenuClosing(e);
    }


    private void DropdownMenu_Closed(object? sender, RoutedEventArgs e)
    {
        OnIgDropdownMenuClosed(e);
    }


    private void Core_ThemeChanged(object? sender, ThemePackChangedEventArgs e)
    {
        OnIgThemeChanged(e);
    }


    private void Core_LanguageChanged(object? sender, EventArgs e)
    {
        OnIgLanguageChanged();
    }

    #endregion // Control Events



    #region Virtual Methods

    /// <summary>
    /// Occurs when the app theme is changed.
    /// </summary>
    protected virtual void OnIgThemeChanged(ThemePackChangedEventArgs e) { }


    /// <summary>
    /// Occurs when the app language is changed.
    /// </summary>
    protected virtual void OnIgLanguageChanged() { }


    /// <summary>
    /// Occurs when the value of the IsOpen property is changing from false to true.
    /// </summary>
    protected virtual void OnIgDropdownMenuOpening(CancelEventArgs e)
    {
        DropdownOpening?.Invoke(DropdownMenu!, e);
    }


    /// <summary>
    /// Occurs when the dropdown menu is opened.
    /// </summary>
    protected virtual void OnIgDropdownMenuOpened(RoutedEventArgs e)
    {
        DropdownOpened?.Invoke(DropdownMenu!, e);
    }


    /// <summary>
    /// Occurs when the value of the IsOpen property is changing from true to false.
    /// </summary>
    protected virtual void OnIgDropdownMenuClosing(CancelEventArgs e)
    {
        DropdownClosing?.Invoke(DropdownMenu!, e);
    }


    /// <summary>
    /// Occurs when the dropdown menu is closed.
    /// </summary>
    protected virtual void OnIgDropdownMenuClosed(RoutedEventArgs e)
    {
        DropdownClosed?.Invoke(DropdownMenu!, e);
    }

    #endregion // Virtual Methods



    #region Control Methods

    /// <summary>
    /// Creates default button content element with icon and text element.
    /// </summary>
    private StackPanel CreateContentElement()
    {
        var pathEl = new PathIcon
        {
            Width = 14,
            Height = 14,
            [!PathIcon.DataProperty] = this[!IconDataProperty],
            [!PathIcon.ForegroundProperty] = this[!ForegroundProperty],
            [!PathIcon.IsVisibleProperty] = this[!IsIconVisibleProperty],
        };

        var textEl = new TextBlock
        {
            [!TextBlock.TextProperty] = this[!TextProperty],
            [!TextBlock.ForegroundProperty] = this[!ForegroundProperty],
            [!TextBlock.IsVisibleProperty] = this[!IsTextVisibleProperty],
        };


        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
        };
        panel.Children.AddRange([pathEl, textEl]);

        return panel;
    }


    /// <summary>
    /// Opens dropdown menu of this button if any.
    /// </summary>
    public void OpenDropdownMenu(PlacementMode? placement = null)
    {
        if (DropdownMenu is null) return;

        DropdownMenu.Placement = placement ?? PlacementMode.BottomEdgeAlignedRight;
        DropdownMenu.Open(this);
    }

    #endregion // Control Methods


}
