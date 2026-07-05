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
/// Read-only modal window that displays a native plugin's manifest metadata.
/// </summary>
internal sealed class PluginInfoWindow : DialogWindow
{
    private readonly PluginInfoWindowView _view;
    private readonly bool _consentMode;


    // fixed dialog width so it doesn't grow/shrink with the metadata text
    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;
    protected override Thickness ContentPadding => new(0);


    /// <summary>
    /// Opens the window showing the metadata of <paramref name="manifest"/> (folder <paramref name="pluginDir"/>).
    /// When <paramref name="consentMode"/> is <c>true</c>, the window becomes an "Enable this plugin?"
    /// consent prompt ([Enable] / [Cancel]); <paramref name="hashChanged"/> adds a stronger warning.
    /// </summary>
    public PluginInfoWindow(PluginManifest manifest, string pluginDir, bool consentMode = false, bool hashChanged = false)
    {
        _consentMode = consentMode;

        if (consentMode)
        {
            // consent prompt: [Enable] [Cancel], with Cancel as the safe default
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
        if (consentMode) _view.ShowConsentWarning(manifest, hashChanged);
        DialogContent = _view;
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        // both modes keep the same window title; consent mode's "Enable this plugin?" heading
        // lives in the banner, and only the footer buttons differ.
        Title = Core.Lang[LangId.Settings_Plugins_ViewMetadata];

        if (_consentMode)
        {
            Button1Text = Core.Lang[LangId.Settings_Plugins_TrustAndEnable];
            Button2Text = Core.Lang[LangId._Cancel];
        }
        else
        {
            Button1Text = Core.Lang[LangId._OK];
        }
    }

}
