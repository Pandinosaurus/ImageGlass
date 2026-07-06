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
using ImageGlass.SDK.Plugins;
using ImageGlass.UI.Windowing;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The action a <see cref="PluginInfoWindow"/> offers for a plugin, decided from its trust state.
/// </summary>
internal enum PluginInfoWindowMode
{
    /// <summary>
    /// Read-only metadata view ([OK]).
    /// </summary>
    View,

    /// <summary>
    /// Trust-and-enable consent prompt ([Trust and Enable] / [Cancel]).
    /// </summary>
    Enable,

    /// <summary>
    /// Disable prompt ([Disable] / [Cancel]).
    /// </summary>
    Disable,
}


/// <summary>
/// Modal window that displays a native plugin's manifest metadata and, depending on
/// <see cref="PluginInfoWindowMode"/>, offers to enable or disable it.
/// </summary>
internal sealed class PluginInfoWindow : DialogWindow
{
    private readonly PluginInfoWindowView _view;
    private readonly PluginInfoWindowMode _mode;


    // fixed dialog width so it doesn't grow/shrink with the metadata text
    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;
    protected override Thickness ContentPadding => new(0);


    /// <summary>
    /// Opens the window showing the metadata of <paramref name="manifest"/> (folder <paramref name="pluginDir"/>).
    /// The <paramref name="mode"/> controls the footer buttons: <see cref="PluginInfoWindowMode.Enable"/>
    /// shows the trust consent prompt ([Trust and Enable] / [Cancel]) and <paramref name="hashChanged"/>
    /// adds a stronger warning; <see cref="PluginInfoWindowMode.Disable"/> shows [Disable] / [Cancel];
    /// <see cref="PluginInfoWindowMode.View"/> is a read-only [OK] view.
    /// </summary>
    public PluginInfoWindow(PluginManifest manifest, string pluginDir,
        PluginInfoWindowMode mode = PluginInfoWindowMode.View, bool hashChanged = false)
    {
        _mode = mode;

        if (mode is PluginInfoWindowMode.Enable or PluginInfoWindowMode.Disable)
        {
            // action prompt: [Enable|Disable] [Cancel], with Cancel as the safe default
            IsButton1Visible = true;
            IsButton2Visible = true;
            IsButton3Visible = false;
            DefaultButton = DialogButton.Button2;
            DefaultFocus = DialogFocus.Button2;
        }
        else
        {
            IsButton1Visible = true;
            IsButton2Visible = false;
            IsButton3Visible = false;
            DefaultButton = DialogButton.Button1;
            DefaultFocus = DialogFocus.Button1;
        }

        _view = new PluginInfoWindowView();
        _view.LoadData(manifest, pluginDir);
        if (mode == PluginInfoWindowMode.Enable) _view.ShowConsentWarning(manifest, hashChanged);
        DialogContent = _view;
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        // all modes keep the same window title; the enable prompt's heading lives in the banner,
        // and only the footer buttons differ per mode.
        Title = Core.Lang[LangId.Settings_Plugins_ViewMetadata];

        switch (_mode)
        {
            case PluginInfoWindowMode.Enable:
                Button1Text = Core.Lang[LangId.Settings_Plugins_TrustAndEnable];
                Button2Text = Core.Lang[LangId._Cancel];
                break;

            case PluginInfoWindowMode.Disable:
                Button1Text = Core.Lang[LangId.Settings_Plugins_Disable];
                Button2Text = Core.Lang[LangId._Cancel];
                break;

            default:
                Button1Text = Core.Lang[LangId._OK];
                break;
        }
    }

}
