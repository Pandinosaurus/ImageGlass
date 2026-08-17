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
using ImageGlass.Common.Localization;
using ImageGlass.UI.Windowing;

namespace ImageGlass.Common.Windows;

public partial class ManageLicenseWindow : DialogWindow
{
    protected override int MIN_WIDTH => 500;
    protected override Thickness ContentPadding => new(0);


    public ManageLicenseWindow()
    {
        IsButton1Visible = true;
        IsButton2Visible = false;
        IsButton3Visible = false;

        // Close is the only footer button and must stay neutral, never accent
        DefaultButton = DialogButton.None;
        DefaultFocus = DialogFocus.Button1;
        ShowInTaskbar = true;

        Title = "ImageGlass Pro";
        DialogContent = new ManageLicenseView();
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Button1Text = Core.Lang[LangId._Close];
    }

}
