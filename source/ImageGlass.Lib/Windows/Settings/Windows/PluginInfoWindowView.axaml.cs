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
using Avalonia.Media;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Types;
using ImageGlass.Plugins;
using ImageGlass.SDK.Plugins;
using ImageGlass.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageGlass.Common.Windows;

/// <summary>
/// Renders a native plugin's manifest metadata, plus a per-extension decode/encode picker when the
/// plugin is loaded. Optional fields with no value are hidden.
/// </summary>
public partial class PluginInfoWindowView : PhControl
{
    private static readonly FontFamily _codeFont = new(Const.FONT_CODE);

    private string _website = string.Empty;
    private string _pluginDir = string.Empty;
    private string _pluginId = string.Empty;

    // Declared per direction, plus the working exclusion sets the checkboxes edit.
    private string[] _declaredDecode = [];
    private string[] _declaredEncode = [];
    private readonly HashSet<string> _disabledDecode = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledEncode = new(StringComparer.OrdinalIgnoreCase);
    private bool _isEditable;
    private bool _isLoaded;


    /// <summary>
    /// Gets the extensions the user switched off for decoding.
    /// </summary>
    public IReadOnlyCollection<string> DisabledDecodeExtensions => _disabledDecode;

    /// <summary>
    /// Gets the extensions the user switched off for encoding.
    /// </summary>
    public IReadOnlyCollection<string> DisabledEncodeExtensions => _disabledEncode;

    /// <summary>
    /// Gets whether the format choices differ from what was loaded.
    /// </summary>
    public bool ChoicesChanged { get; private set; }


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
        PART_TabInfo.Header = Core.Lang[LangId.Settings_Plugins_ViewMetadata];
        PART_TabFormats.Header = Core.Lang[LangId.Settings_FileFormats];

        // PhTableControl captures header text at Build time, so rebuild from the working sets
        // (not the persisted ones) or a language switch would discard unsaved ticks.
        if (_pluginId.Length > 0) RebuildExtensionsTable();
    }


    /// <summary>
    /// Shows or hides the formats tab, hiding the tab strip when only one tab is left.
    /// </summary>
    private void SetFormatsTabVisible(bool visible)
    {
        PART_TabFormats.IsVisible = visible;

        if (visible) PART_Tabs.Classes.Remove("singleTab");
        else PART_Tabs.Classes.Add("singleTab");

        // an invisible tab can still be the selected one, which would blank the panel
        if (!visible) PART_Tabs.SelectedItem = PART_TabInfo;
    }


    /// <summary>
    /// Populates the fields from the given manifest and its folder path. <paramref name="allowEdit"/>
    /// lets the user change the format choices; <paramref name="showFormats"/> keeps the formats tab,
    /// which only a codec plugin has any use for.
    /// </summary>
    public void LoadData(PluginManifest manifest, string pluginDir, bool allowEdit = false,
        bool showFormats = true)
    {
        _website = manifest.Website ?? string.Empty;
        _pluginDir = pluginDir;
        _pluginId = manifest.Id;

        // Admin-locked plugin trust makes the whole picker informational.
        _isEditable = allowEdit && !Config.IsConfigLocked(ConfigId.PluginTrust);

        SetField(PART_IdRow, PART_Id, manifest.Id);
        SetField(PART_NameRow, PART_Name, manifest.Name);
        SetField(PART_VersionRow, PART_Version, manifest.Version);
        SetField(PART_TypeRow, PART_Type, manifest.Kind.ToString());
        SetField(PART_DescriptionRow, PART_Description, manifest.Description);
        SetField(PART_ExecutableRow, PART_Executable, manifest.Executable);
        SetField(PART_AuthorRow, PART_Author, manifest.Author);

        // Formats come from the codec itself, so they are only knowable while the plugin is loaded;
        // reading them means running it, and running it requires trust.
        _isLoaded = Core.PluginRegistry.IsLoaded(manifest.Id);
        (_declaredDecode, _declaredEncode) = Core.PluginRegistry.GetDeclaredCodecExtensions(manifest.Id);

        var (disabledDecode, disabledEncode) = PluginTrustPolicy.GetExtensionExclusions(manifest.Id);
        _disabledDecode.Clear();
        _disabledEncode.Clear();
        foreach (var ext in disabledDecode) _disabledDecode.Add(ext);
        foreach (var ext in disabledEncode) _disabledEncode.Add(ext);
        ChoicesChanged = false;

        SetFormatsTabVisible(showFormats);
        RebuildExtensionsTable();

        PART_WebsiteRow.IsVisible = !string.IsNullOrWhiteSpace(_website);
        PART_Website.Text = _website;

        PART_Folder.Text = pluginDir;
        ToolTip.SetTip(PART_Folder, pluginDir);
    }


    /// <summary>
    /// Reveals the consent warning banner shown when the user is about to enable (trust) a plugin.
    /// When <paramref name="hashChanged"/> is <c>true</c>, prepends a stronger "file changed" warning.
    /// </summary>
    public void ShowConsentWarning(PluginManifest manifest, bool hashChanged)
    {
        var name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name;
        var msg = Core.Lang[LangId.Settings_Plugins_TrustPrompt, name];
        if (hashChanged)
        {
            msg = Core.Lang[LangId.Settings_Plugins_TrustChangedWarning] + "\n\n" + msg;
        }

        PART_ConsentTitle.Text = Core.Lang[LangId.Settings_Plugins_TrustTitle];
        PART_ConsentMessage.Text = msg;
        PART_ConsentRow.IsVisible = true;
    }


    /// <summary>
    /// Rebuilds the format picker: one row per declared extension, with a Decode and an Encode
    /// checkbox. An unloaded plugin has nothing to show, so the hint replaces the table.
    /// </summary>
    private void RebuildExtensionsTable()
    {
        var rowExts = new List<string>(_declaredDecode);
        foreach (var ext in _declaredEncode)
        {
            if (!rowExts.Contains(ext, StringComparer.OrdinalIgnoreCase)) rowExts.Add(ext);
        }
        rowExts.Sort(StringComparer.OrdinalIgnoreCase);

        // PhTableControl renders no header at all with zero rows, so hide the table instead.
        PART_ExtensionsTable.IsVisible = rowExts.Count > 0;
        PART_ExtensionsHint.Text = rowExts.Count > 0
            ? (_isEditable ? Core.Lang[LangId.Settings_Plugins_ExtensionsHint] : string.Empty)
            : Core.Lang[_isLoaded ? LangId.Settings_Plugins_NoFormats : LangId.Settings_Plugins_FormatsAfterEnable];
        PART_ExtensionsHint.IsVisible = !string.IsNullOrEmpty(PART_ExtensionsHint.Text);

        if (rowExts.Count == 0) return;

        PhTableColumn[] columns =
        [
            new() { Header = Core.Lang[LangId._FileExtension], Star = true, MinWidth = 140 },
            new() { Header = Core.Lang[LangId.Settings_Plugins_Decode] },
            new() { Header = Core.Lang[LangId.Settings_Plugins_Encode] },
        ];

        var rows = rowExts.Select(ext => new PhTableRow
        {
            Key = ext,
            Cells =
            [
                PhTableControl.TextCell(ext, selectable: true, font: _codeFont),
                DirectionCell(ext, _declaredDecode, _disabledDecode),
                DirectionCell(ext, _declaredEncode, _disabledEncode),
            ],
        }).ToList();

        PART_ExtensionsTable.EmptyText = Core.Lang[LangId._Empty];
        PART_ExtensionsTable.Build(columns, rows);
    }


    /// <summary>
    /// One direction cell: a checkbox when the plugin declares this extension for it, otherwise a
    /// muted dash meaning "not applicable" (a greyed checkbox would read as "off, could be on").
    /// </summary>
    private Control DirectionCell(string ext, string[] declared, HashSet<string> disabled)
    {
        if (!declared.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            return PhTableControl.TextCell("-", muted: true);
        }

        var check = new CheckBox
        {
            IsChecked = !disabled.Contains(ext),
            IsEnabled = _isEditable,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        // Click, not IsCheckedChanged, so the initial assignment above doesn't mark it dirty.
        check.Click += (_, _) =>
        {
            if (check.IsChecked == true) disabled.Remove(ext);
            else disabled.Add(ext);

            ChoicesChanged = true;
        };

        return PhTableControl.WrapCell(check);
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
