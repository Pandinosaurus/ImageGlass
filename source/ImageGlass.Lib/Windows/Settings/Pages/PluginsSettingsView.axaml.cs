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
using Avalonia.Platform.Storage;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Types;
using ImageGlass.Plugins;
using ImageGlass.SDK.Plugins;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The "Plugins" settings page. Lists the native plugins installed in the <c>_plugins</c> folder
/// (read from each plugin's manifest) and lets the user add or inspect them.
/// </summary>
public partial class PluginsSettingsView : SettingsPageView
{
    private const double NAME_MAX_WIDTH = 220;
    private const int MAX_DESC_CHARS = 250;

    // file picker filter pattern for installable plugin packages
    private const string PLUGIN_PACKAGE_PATTERN = "*.igplugin.zip";

    // installed plugins discovered from the _plugins folder
    private readonly List<(PluginManifest Manifest, string Dir)> _plugins = [];


    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public PluginsSettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public PluginsSettingsView(SettingsViewModel vm, string navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
    {
        SetLocalizedText(PART_AddPlugin, LangId._Add);
        PART_AddPlugin.Click += async (_, _) => await AddPluginsAsync();

        SetLocalizedText(PART_OpenFolder, LangId.FrmSettings_Plugins_OpenPluginFolder);
        PART_OpenFolder.Click += (_, _) => BHelper.OpenFolderPath(BHelper.ConfigDir(Dir.Plugins));

        SetLocalizedText(PART_GetMorePlugins, LangId.FrmSettings_Plugins_GetMorePlugins);
        PART_GetMorePlugins.Click += (_, _) =>
            _ = BHelper.OpenUrlAsync(this, "https://imageglass.org/plugins", "from_plugin_settings");

        ReloadPlugins();

        // rebuild on language change (also performs the initial render)
        AddLangRefresher(RebuildTable);

        RegisterSearchKey(PART_AddPlugin, LangId.FrmSettings_Nav_Plugins, null, LangId.FrmSettings_Nav_Plugins);
    }


    /// <summary>
    /// Re-reads the installed plugin manifests from the <c>_plugins</c> folder into the working list.
    /// </summary>
    private void ReloadPlugins()
    {
        _plugins.Clear();
        _plugins.AddRange(PluginRegistry.DiscoverManifests(BHelper.ConfigDir(Dir.Plugins)));
    }


    /// <summary>
    /// Opens a file picker for <c>*.igplugin.zip</c> packages and extracts each into the
    /// <c>_plugins</c> folder, then reloads and re-renders the list.
    /// </summary>
    private async Task AddPluginsAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Core.Lang[LangId.FrmSettings_Nav_Plugins])
                {
                    Patterns = [PLUGIN_PACKAGE_PATTERN],
                },
            ],
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();
        if (paths.Count == 0) return;

        var pluginsDir = BHelper.ConfigDir(Dir.Plugins);
        var installed = await Task.Run(() =>
        {
            var count = 0;
            foreach (var file in paths)
            {
                if (!File.Exists(file)) continue;
                try
                {
                    ZipFile.ExtractToDirectory(file, pluginsDir, overwriteFiles: true);
                    count++;
                }
                catch { }
            }
            return count;
        });

        ReloadPlugins();
        RebuildTable();

        // installed plugins are only loaded at startup -> hint the user to restart
        if (installed > 0)
        {
            PART_InstallHint.Text = Core.Lang[LangId.FrmSettings_Plugins_InstallSuccess]
                + ". " + Core.Lang[LangId.FrmSettings_Plugins_RestartRequired];
            PART_InstallHint.IsVisible = true;
        }
    }


    /// <summary>
    /// Opens a read-only window showing the full manifest metadata of the given plugin.
    /// </summary>
    private async Task ViewPluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        var win = new PluginInfoWindow(plugin.Manifest, plugin.Dir);
        await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow);
    }


    /// <summary>
    /// Rebuilds the plugins table from the working list (header + one row per plugin), toggling the empty state.
    /// </summary>
    private void RebuildTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = Core.Lang[LangId._Type] },
            new() { Header = Core.Lang[LangId._Name] },
            new() { Header = Core.Lang[LangId._Description], Star = true },
        ];

        var rows = _plugins.Select(plugin =>
        {
            var m = plugin.Manifest;
            return new PhTableRow
            {
                Key = m.Id,
                Cells =
                [
                    PhTableControl.TextCell(m.Kind.ToString()),
                    NameCell(m),
                    DescriptionCell(m.Description),
                ],
                Actions =
                [
                    new() { Icon = ResxIconId.IconInfo, Tooltip = Core.Lang[LangId._View], Click = () => _ = ViewPluginAsync(plugin) },
                ],
            };
        }).ToList();

        PART_Table.EmptyText = Core.Lang[LangId._Empty];
        PART_Table.Build(columns, rows);
    }


    #region Table cell builders

    /// <summary>
    /// The name cell: the plugin name (capped + truncated), with the version below it when set.
    /// </summary>
    private static Border NameCell(PluginManifest m)
    {
        var name = new SelectableTextBlock
        {
            Text = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name,
            MaxWidth = NAME_MAX_WIDTH,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTabStop = false,
        };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(name);

        if (!string.IsNullOrWhiteSpace(m.Version))
        {
            stack.Children.Add(new TextBlock
            {
                Text = m.Version,
                FontSize = Const.FONT_SIZE_SMALL,
                Opacity = 0.6,
            });
        }

        return PhTableControl.WrapCell(stack);
    }


    /// <summary>
    /// The description cell: wraps to at most 2 lines, truncated with an ellipsis, full text in a tooltip.
    /// </summary>
    private static Control DescriptionCell(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return PhTableControl.TextCell(string.Empty);

        var full = text.Trim();
        var display = full.Length > MAX_DESC_CHARS ? full[..MAX_DESC_CHARS] + "…" : full;

        var tb = new TextBlock
        {
            Text = display,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsTabStop = false,
        };
        ToolTip.SetTip(tb, full);

        return PhTableControl.WrapCell(tb);
    }

    #endregion // Table cell builders

}
