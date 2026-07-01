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
using ImageGlass.Common.Localization;
using ImageGlass.SDK.Plugins;
using ImageGlass.UI.Windowing;

namespace ImageGlass.Common.Windows;

/// <summary>
/// Read-only modal window that displays a native plugin's manifest metadata.
/// </summary>
internal sealed class PluginInfoWindow : DialogWindow
{
    private readonly PluginInfoWindowView _view;


    // fixed dialog width so it doesn't grow/shrink with the metadata text
    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;


    /// <summary>
    /// Opens the window showing the metadata of <paramref name="manifest"/> (folder <paramref name="pluginDir"/>).
    /// </summary>
    public PluginInfoWindow(PluginManifest manifest, string pluginDir)
    {
        IsButton1Visible = true;
        IsButton2Visible = false;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;

        _view = new PluginInfoWindowView();
        _view.LoadData(manifest, pluginDir);
        DialogContent = _view;
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Settings_Plugins_ViewMetadata];
        Button1Text = Core.Lang[LangId._OK];
    }

}
