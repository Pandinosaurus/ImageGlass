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
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using ImageGlass.Common;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Types;
using System.Collections.Generic;
using System.Linq;

namespace ImageGlass.UI;

/// <summary>
/// Records and lists keyboard shortcuts: a recorder box on top (focus it and press a chord to add a
/// hotkey) with the recorded hotkeys shown as removable chips below. Read or replace the whole set
/// through <see cref="Hotkeys"/>.
/// </summary>
public class PhHotkeyPicker : PhControl
{
    private readonly List<Hotkey> _hotkeys = [];
    private readonly WrapPanel _chips;
    private readonly PhTextBox _recorder;


    /// <summary>
    /// Gets a copy of the recorded hotkeys, or replaces the whole set.
    /// </summary>
    public IReadOnlyList<Hotkey> Hotkeys
    {
        get => [.. _hotkeys];
        set
        {
            _hotkeys.Clear();
            if (value is not null) _hotkeys.AddRange(value);
            RenderChips();
        }
    }


    /// <summary>
    /// Gets, sets the placeholder shown in the recorder box.
    /// </summary>
    public string? PlaceholderText
    {
        get => _recorder.PlaceholderText;
        set => _recorder.PlaceholderText = value;
    }


    public PhHotkeyPicker()
    {
        _recorder = new PhTextBox
        {
            IsReadOnly = true,
            ValidateByPressingEnter = false,
        };

        // tunnel + handledEventsToo so we capture the chord before the (read-only) TextBox does
        _recorder.AddHandler(KeyDownEvent, OnRecorderKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        _chips = new WrapPanel
        {
            IsVisible = false,
            Margin = new Thickness(0, 8, 0, 0),
        };

        Content = new StackPanel
        {
            Children = { _recorder, _chips },
        };
    }


    #region Recording

    /// <summary>
    /// Records a shortcut from a key press: ignores lone modifiers (and leaves Tab/Escape/Enter for
    /// normal navigation/close/submit), otherwise adds the chord.
    /// </summary>
    private void OnRecorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Escape or Key.Enter) return;

        if (IsModifierKey(e.Key)) { e.Handled = true; return; }

        AddHotkey(new Hotkey(e.KeyModifiers, e.Key));
        e.Handled = true;
    }


    private static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin;


    private void AddHotkey(Hotkey hk)
    {
        if (hk.Key == Key.None) return;
        if (_hotkeys.Any(h => h.Key == hk.Key && h.Modifiers == hk.Modifiers)) return;

        _hotkeys.Add(hk);
        RenderChips();
    }

    #endregion // Recording


    #region Chips

    private void RenderChips()
    {
        _chips.Children.Clear();
        foreach (var hk in _hotkeys)
        {
            _chips.Children.Add(BuildChip(hk));
        }
        _chips.IsVisible = _hotkeys.Count > 0;
    }


    private Border BuildChip(Hotkey hk)
    {
        var label = new TextBlock
        {
            Text = hk.KeyString,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // the shared close icon is a stroke-only X, so render it with a stroked Path (not a filled
        // PathIcon, which would draw nothing)
        var icon = new Path
        {
            Width = 9,
            Height = 9,
            Data = FindIcon("IconClose"),
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.2,
            StrokeLineCap = PenLineCap.Round,
        };
        icon[!Shape.StrokeProperty] = Resx.CreateBinding(ResxId.TextControlForeground);

        // a PhToolButton gives the square tool-button shape with the app's hover + press feedback
        var remove = new PhToolButton
        {
            Padding = new Thickness(6),
            Content = icon,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(remove, Core.Lang[LangId._Delete]);
        remove.Click += (_, _) =>
        {
            _hotkeys.Remove(hk);
            RenderChips();
        };

        // a small gap keeps the close button off the hotkey text
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(label);
        panel.Children.Add(remove);

        var chip = new Border
        {
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(8, 3, 4, 3),
            BorderThickness = new Thickness(1),
            Child = panel,
        };
        // match the Default PhButton fill (PhButtonBackground has no ResxId, so resolve it directly)
        chip[!Border.BackgroundProperty] = new DynamicResourceExtension("PhButtonBackground");
        chip[!Border.BorderBrushProperty] = Resx.CreateBinding(ResxId.IG_BorderControlBrush);
        chip[!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius);
        return chip;
    }


    /// <summary>
    /// Resolves a shared icon geometry (from IconResources) by key.
    /// </summary>
    private static Geometry? FindIcon(string key)
        => Application.Current is { } app && app.TryFindResource(key, out var res)
            ? res as Geometry
            : null;

    #endregion // Chips

}
