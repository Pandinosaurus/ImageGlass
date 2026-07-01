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
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Types;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System.Collections.Generic;
using System.Linq;

namespace ImageGlass.Common.Windows;

/// <summary>
/// Modal window to add, edit, remove or restore the hotkeys of a single main-menu action.
/// </summary>
internal sealed class MenuHotkeyEditWindow : DialogWindow
{
    private readonly string _actionPath;
    private readonly Hotkey[] _defaultHotkeys;
    private readonly PhHotkeyPicker _picker;
    private readonly PhButton _restoreBtn;
    private readonly PhTextBlock _pathLabel;
    private readonly PhTextBlock _defaultLabel;


    protected override int MIN_WIDTH => 460;
    protected override int MAX_WIDTH => 460;


    /// <summary>
    /// Gets the hotkeys built from the picker, or <c>null</c> if the dialog wasn't submitted.
    /// </summary>
    public Hotkey[]? ResultHotkeys { get; private set; }


    /// <summary>
    /// Opens the editor for the action labelled <paramref name="actionPath"/>, seeded with its
    /// <paramref name="current"/> hotkeys; "Reset to default" reverts to <paramref name="defaultHotkeys"/>.
    /// </summary>
    public MenuHotkeyEditWindow(string actionPath, IReadOnlyList<Hotkey> current, IReadOnlyList<Hotkey> defaultHotkeys)
    {
        _actionPath = actionPath;
        _defaultHotkeys = [.. defaultHotkeys];

        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Default;

        // Enter is reserved for the hotkey recorder, so it must not submit the dialog
        PressEnterToSubmit = false;

        _pathLabel = new PhTextBlock
        {
            Text = actionPath,
            FontWeight = FontWeight.SemiBold
        };
        _picker = new PhHotkeyPicker { Hotkeys = [.. current] };
        _restoreBtn = new PhButton { Variant = PhButtonVariant.Link };
        _restoreBtn.Click += (_, _) => _picker.Hotkeys = [.. _defaultHotkeys];

        _defaultLabel = new PhTextBlock
        {
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        };

        // reset link with the default-hotkey hint directly below it
        var footer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Children = { _restoreBtn, _defaultLabel },
        };

        DialogContent = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            Children = { _pathLabel, _picker, footer },
        };
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Settings_Keyboard_EditTitle];
        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[LangId._Cancel];

        _restoreBtn.Text = Core.Lang[LangId._ResetToDefault];
        _picker.PlaceholderText = Core.Lang[LangId.Settings_Toolbar_RecordHotkeyHint];

        var defaultText = string.Join(", ", _defaultHotkeys.Select(h => h.KeyString));
        if (string.IsNullOrEmpty(defaultText)) defaultText = Core.Lang[LangId._Empty];
        _defaultLabel.Text = defaultText;
    }


    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        ResultHotkeys = [.. _picker.Hotkeys];
        base.OnDialogSubmitted(e);
    }

}
