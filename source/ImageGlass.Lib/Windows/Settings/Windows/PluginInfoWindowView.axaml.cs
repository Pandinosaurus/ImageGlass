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
using ImageGlass.Common.Localization;
using ImageGlass.SDK.Plugins;
using ImageGlass.UI;

namespace ImageGlass.Common.Windows;

/// <summary>
/// Read-only view that renders a native plugin's manifest metadata. Optional fields with no value
/// are hidden.
/// </summary>
public partial class PluginInfoWindowView : PhControl
{
    private string _website = string.Empty;
    private string _pluginDir = string.Empty;


    public PluginInfoWindowView()
    {
        InitializeComponent();

        PART_Website.Click += (_, _) => _ = BHelper.OpenUrlAsync(this, _website, "from_plugin_settings");
        PART_OpenFolder.Click += (_, _) => BHelper.OpenFolderPath(_pluginDir);
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        PART_OpenFolder.Text = Core.Lang[LangId.Settings_Plugins_OpenPluginFolder];
    }


    /// <summary>
    /// Populates the fields from the given manifest and its folder path.
    /// </summary>
    public void LoadData(PluginManifest manifest, string pluginDir)
    {
        _website = manifest.Website ?? string.Empty;
        _pluginDir = pluginDir;

        SetField(PART_IdRow, PART_Id, manifest.Id);
        SetField(PART_NameRow, PART_Name, manifest.Name);
        SetField(PART_VersionRow, PART_Version, manifest.Version);
        SetField(PART_TypeRow, PART_Type, manifest.Kind.ToString());
        SetField(PART_DescriptionRow, PART_Description, manifest.Description);
        SetField(PART_ExecutableRow, PART_Executable, manifest.Executable);
        SetField(PART_AuthorRow, PART_Author, manifest.Author);

        // prefer the codec's own extensions (re-probed live); fall back to the manifest override
        var codecExts = Core.PluginRegistry.GetCodecSupportedExtensions(manifest.Id);
        var extText = codecExts.Length > 0
            ? string.Join(", ", codecExts)
            : manifest.SupportedExtensions;
        SetField(PART_ExtensionsRow, PART_Extensions, extText);

        PART_WebsiteRow.IsVisible = !string.IsNullOrWhiteSpace(_website);
        PART_Website.Text = _website;

        PART_Folder.Text = pluginDir;
        ToolTip.SetTip(PART_Folder, pluginDir);
    }


    /// <summary>
    /// Sets a field's value text and hides the whole row when the value is empty.
    /// </summary>
    private static void SetField(Control row, SelectableTextBlock value, string? text)
    {
        value.Text = text ?? string.Empty;
        row.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

}
