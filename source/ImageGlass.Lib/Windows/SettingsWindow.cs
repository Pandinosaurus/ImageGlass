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
using ImageGlass.Common.Localization;
using ImageGlass.UI.Windowing;

namespace ImageGlass.Common.Windows;

public partial class SettingsWindow : DialogWindow
{

    public SettingsWindow()
    {
        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = true;

        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;
        ShowInTaskbar = true;

        DialogContent = new SettingsWindowView();
        DialogFooterLeftContent = CreateDialogFooterLeftContentElement();
    }



    #region Override Methods


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.FrmMain_MnuSettings];
        Button1Text = Core.Lang[LangId._OK];
        Button2Text = Core.Lang[LangId._Cancel];
        Button3Text = Core.Lang[LangId._Apply];
    }

    #endregion // Override Methods



    #region Private Methods


    private StackPanel CreateDialogFooterLeftContentElement()
    {
        var footerLeftPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };

        return footerLeftPanel;
    }


    #endregion // Private Methods




}
