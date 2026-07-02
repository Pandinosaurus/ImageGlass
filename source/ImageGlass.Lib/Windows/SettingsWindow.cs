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
using Avalonia.Layout;
using ImageGlass.Common.Localization;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using ImageGlass.Windows;

namespace ImageGlass.Common.Windows;

public partial class SettingsWindow : DialogWindow
{
    private readonly SettingsViewModel _vm = new();
    private readonly SettingsWindowView _viewEl;
    private readonly string? _targetConfigId;

    private PhButton _btnGetHelp = null!;
    private PhButton _btnResetSettings = null!;


    protected override int MIN_WIDTH => 0;
    protected override int MAX_WIDTH => int.MaxValue;
    protected override Thickness ContentPadding => new(0);



    public SettingsWindow(string? targetConfigId = null)
    {
        _targetConfigId = targetConfigId;

        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = true;

        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Default;
        ShowInTaskbar = true;
        PressEnterToSubmit = false;

        // resizable window
        CanResize = true;
        CanMinimize = true;
        CanMaximize = true;
        SizeToContent = SizeToContent.Manual;
        // no min size: let the window be resized as small as the OS allows

        // restore window size & position
        var bounds = Core.Config.SettingsWindowBounds;
        Width = bounds.Width;
        Height = bounds.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new((int)bounds.X, (int)bounds.Y);

        // restore window maximized state
        if (Core.Config.EnableSettingsWindowMaximized) WindowState = WindowState.Maximized;

        _viewEl = new SettingsWindowView(_vm);
        DialogContent = _viewEl;
        DialogFooterLeftContent = CreateDialogFooterLeftContentElement();
    }



    #region Override Methods

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // a specific setting was requested -> jump to it (overrides the restored page)
        if (!string.IsNullOrWhiteSpace(_targetConfigId))
        {
            _viewEl.NavigateToConfig(_targetConfigId);
        }

        // focus the search box on open
        _viewEl.FocusSearch();
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Menu_MnuSettings];
        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[LangId._Cancel];
        Button3Text = Core.Lang[LangId._Apply];

        if (_btnGetHelp is not null) _btnGetHelp.Text = Core.Lang[LangId._GetHelp];
        if (_btnResetSettings is not null) _btnResetSettings.Text = Core.Lang[LangId.Settings_ResetSettings];
    }


    protected override async void OnDialogSubmitted(DialogEventArgs e)
    {
        if (!e.CanProceed) return;

        await _vm.CommitAsync();
        base.OnDialogSubmitted(e);
    }


    protected override async void OnDialogApplied(DialogEventArgs e)
    {
        if (!e.CanProceed) return;

        await _vm.CommitAsync();
        base.OnDialogApplied(e);
    }


    protected override void OnDialogCancelled(DialogEventArgs e)
    {
        _vm.Discard();
        base.OnDialogCancelled(e);
    }


    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // capture bounds here (not OnClosed) — the native window is still alive,
        // so Position/ClientSize are valid; by OnClosed they read (0,0).

        // save state regardless of how the dialog was closed
        Core.Config.EnableSettingsWindowMaximized = WindowState == WindowState.Maximized;

        // save window bounds only when in normal state (don't store maximized size as the restore size)
        if (WindowState == WindowState.Normal)
        {
            var size = ClientSize;
            Core.Config.SettingsWindowBounds = new(Position.X, Position.Y,
                (int)size.Width,
                (int)size.Height);
        }

        _ = Core.Config.SaveAsync();

        base.OnClosing(e);
    }


    protected override void OnDialogAborted()
    {
        _vm.Discard();
        base.OnDialogAborted();
    }

    #endregion // Override Methods



    #region Public Methods

    /// <summary>
    /// Navigates the settings window to the page hosting the given config id and scrolls to it.
    /// </summary>
    public void NavigateToConfig(string? configId) => _viewEl.NavigateToConfig(configId);

    #endregion // Public Methods



    #region Private Methods

    private StackPanel CreateDialogFooterLeftContentElement()
    {
        _btnGetHelp = CreateLinkButton(Core.Lang[LangId._GetHelp], async () =>
        {
            var campaign = string.IsNullOrEmpty(_viewEl.CurrentNavId)
                ? "from_setting"
                : $"from_setting_{_viewEl.CurrentNavId}";
            await BHelper.OpenUrlAsync(this, "https://imageglass.org/docs", campaign);
        });

        _btnResetSettings = CreateLinkButton(Core.Lang[LangId.Settings_ResetSettings], async () =>
        {
            // reset-to-defaults is handled by the Quick Setup wizard
            var quickSetup = new QuickSetupWindow();
            await quickSetup.ShowAsync(this);
        });

        var footerLeftPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
        };
        footerLeftPanel.Children.AddRange([_btnGetHelp, _btnResetSettings]);

        return footerLeftPanel;
    }


    /// <summary>
    /// Creates a borderless, link-style button.
    /// </summary>
    private static PhButton CreateLinkButton(string text, System.Action onClick)
    {
        var btn = new PhButton
        {
            Text = text,
            Variant = PhButtonVariant.Link,
        };
        btn.Click += (_, _) => onClick();

        return btn;
    }

    #endregion // Private Methods


}
