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
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Types;
using ImageGlass.Plugins;
using ImageGlass.SDK.Plugins;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

public partial class PluginsSettingsView : SettingsPageView
{
    private const double NAME_MAX_WIDTH = 220;

    // file picker filter pattern for installable plugin packages
    private const string PLUGIN_PACKAGE_PATTERN = "*.igplugin.zip";

    // installed plugins discovered from the _plugins folder
    private readonly List<(PluginManifest Manifest, string Dir)> _plugins = [];


    public PluginsSettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public PluginsSettingsView(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
    {
        SetLocalizedText(PART_AddPlugin, LangId._Add);
        PART_AddPlugin.Click += async (_, _) => await AddPluginsAsync();

        SetLocalizedText(PART_OpenFolder, LangId.Settings_Plugins_OpenPluginFolder);
        PART_OpenFolder.Click += (_, _) => BHelper.OpenFolderPath(BHelper.GetRealPlatformConfigDir(Dir.Plugins));

        SetLocalizedText(PART_GetMorePlugins, LangId.Settings_Plugins_GetMorePlugins);
        PART_GetMorePlugins.Click += (_, _) =>
            _ = BHelper.OpenUrlAsync(this, "https://imageglass.org/plugins", "from_plugin_settings");

        ReloadPlugins();

        // rebuild on language change (also performs the initial render)
        AddLangRefresher(RebuildTable);

        RegisterSearchKey(PART_AddPlugin, LangId.Settings_Nav_Plugins, null, LangId.Settings_Nav_Plugins);

        // plugin trust is admin-managed: lock install + the per-row enable/disable/edit/delete table.
        // (plugin trust is applied live via PluginTrustPolicy, not staged, so disabling the UI is the
        // only lock point; disabling PART_Table propagates to rows rebuilt later.)
        DisableIfLocked(ConfigId.PluginTrust, PART_AddPlugin, PART_Table);
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
                new FilePickerFileType(Core.Lang[LangId.Settings_Nav_Plugins])
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
                if (InstallPackage(file, pluginsDir)) count++;
            }
            return count;
        });

        ReloadPlugins();
        RebuildTable();

        // Newly installed plugins land untrusted (disabled); the user must review and enable them.
        // Enabling hot-loads the plugin (no restart needed).
        if (installed > 0)
        {
            PART_InstallHint.Text = Core.Lang[LangId.Settings_Plugins_InstallSuccess]
                + ". " + Core.Lang[LangId.Settings_Plugins_EnableToLoad];
            PART_HintContainer.IsVisible = true;
        }
    }


    /// <summary>
    /// Safely installs one <c>*.igplugin.zip</c>: extracts to a temp staging folder, validates the
    /// manifest, then moves the plugin into its own <c>_plugins/&lt;id&gt;/</c> folder. Extracting to
    /// staging first prevents a malformed or hostile archive from scattering files across the
    /// <c>_plugins</c> root or overwriting sibling plugins.
    /// </summary>
    private static bool InstallPackage(string packageFile, string pluginsDir)
    {
        var staging = Path.Combine(Path.GetTempPath(), "ig_plugin_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(packageFile, staging, overwriteFiles: true);

            // locate + validate the manifest (archive root, or one directory below)
            var manifestPath = FindManifest(staging);
            if (manifestPath is null) return false;

            var manifest = ReadManifest(manifestPath);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id)) return false;

            var srcDir = Path.GetDirectoryName(manifestPath)!;
            var destDir = Path.Combine(pluginsDir, MakeSafeFolderName(manifest.Id));

            ReplaceInstall(srcDir, destDir, pluginsDir);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }


    /// <summary>
    /// Installs the staged plugin, moving any previous install aside into the trash stash first
    /// (a loaded .dll is locked, so an in-place delete would strip the manifest) and restoring it on failure.
    /// </summary>
    private static void ReplaceInstall(string srcDir, string destDir, string pluginsDir)
    {
        string? stash = null;
        if (Directory.Exists(destDir))
        {
            var trashRoot = Path.Combine(pluginsDir, PluginRegistry.TRASH_DIR_NAME);
            Directory.CreateDirectory(trashRoot);
            stash = Path.Combine(trashRoot, Guid.NewGuid().ToString("N"));
            Directory.Move(destDir, stash); // atomic; leaves the old install intact if it throws
        }

        try
        {
            MoveDirectory(srcDir, destDir);
        }
        catch
        {
            // roll back to the previous install
            try { if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true); } catch { }
            if (stash is not null && !Directory.Exists(destDir)) Directory.Move(stash, destDir);
            throw;
        }

        // stale copy; a loaded .dll stays locked and is reaped on a later run
        if (stash is not null)
        {
            try { Directory.Delete(stash, recursive: true); } catch { }
        }
    }


    /// <summary>
    /// Finds <c>igplugin.json</c> at the archive root or one directory below it.
    /// </summary>
    private static string? FindManifest(string root)
    {
        var direct = Path.Combine(root, PluginManifest.FILE_NAME);
        if (File.Exists(direct)) return direct;

        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            var candidate = Path.Combine(sub, PluginManifest.FILE_NAME);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }


    /// <summary>
    /// Deserializes a plugin manifest, returning null on any error.
    /// </summary>
    private static PluginManifest? ReadManifest(string manifestPath)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginManifest);
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// Moves a directory, falling back to a recursive copy when source and destination live on
    /// different volumes (temp vs. config).
    /// </summary>
    private static void MoveDirectory(string src, string dest)
    {
        try
        {
            Directory.Move(src, dest);
            return;
        }
        catch { }

        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(src, dir)));
        }
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            File.Copy(f, Path.Combine(dest, Path.GetRelativePath(src, f)), overwrite: true);
        }
    }


    /// <summary>
    /// Replaces characters invalid in a file name with underscores.
    /// </summary>
    private static string MakeSafeFolderName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
        }
        return new string(chars);
    }


    /// <summary>
    /// The enable/disable toggle cell: on for a trusted plugin. Toggling off disables the plugin
    /// directly; toggling on (or any non-trusted state) opens the edit window to run the trust flow.
    /// Disabled for a missing/broken plugin.
    /// </summary>
    private Border ToggleCell((PluginManifest Manifest, string Dir) plugin, PluginTrustPolicy.TrustState state)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = state == PluginTrustPolicy.TrustState.Trusted,
            IsEnabled = state != PluginTrustPolicy.TrustState.Missing,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            // no on/off label; Width = the 40px switch so the template's 12px content spacer isn't reserved
            OnContent = null,
            OffContent = null,
            Width = 40,
        };

        // Click (not IsCheckedChanged) so the initial IsChecked assignment doesn't fire it
        toggle.Click += async (_, _) =>
        {
            _ = state == PluginTrustPolicy.TrustState.Trusted
                ? await DisablePluginAsync(plugin)
                : await EditPluginAsync(plugin);

            // rebuild to reflect the real state (reverts the toggle if the user cancelled)
            RebuildTable();
        };

        return PhTableControl.WrapCell(toggle);
    }


    /// <summary>
    /// Scrolls to and highlights the row of the plugin with the given id (from the File type
    /// associations page's plugin-codec link).
    /// </summary>
    public void FocusPlugin(string pluginId) => PART_Table.FlashRow(pluginId);


    /// <summary>
    /// Opens the plugin info window; for a trusted plugin it offers [Disable], otherwise the
    /// trust-and-enable consent prompt (a missing plugin is view-only). Applies the chosen action
    /// live (hot load/unload) and returns <c>true</c> if the trust state changed.
    /// </summary>
    private async Task<bool> EditPluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        var state = PluginTrustPolicy.GetState(plugin.Manifest, plugin.Dir);
        var mode = state switch
        {
            PluginTrustPolicy.TrustState.Missing => PluginInfoWindowMode.View,
            PluginTrustPolicy.TrustState.Trusted => PluginInfoWindowMode.Disable,
            _ => PluginInfoWindowMode.Enable,
        };

        var win = new PluginInfoWindow(plugin.Manifest, plugin.Dir, mode,
            hashChanged: state == PluginTrustPolicy.TrustState.Changed);
        var result = await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow);

        // the window's "Delete" link runs the delete flow instead of the trust action
        if (win.DeleteRequested)
        {
            await DeletePluginAsync(plugin);
            return false;
        }

        if (result != DialogExitCode.OK) return false;

        return mode switch
        {
            PluginInfoWindowMode.Enable => await EnablePluginAsync(plugin),
            PluginInfoWindowMode.Disable => await DisablePluginAsync(plugin),
            _ => false,
        };
    }


    /// <summary>
    /// Confirms, then deletes the plugin: hot-unloads it (releases the locked library), removes its
    /// trust entry and folder, and re-renders the list.
    /// </summary>
    private async Task DeletePluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        var owner = TopLevel.GetTopLevel(this) as PhWindow;
        var m = plugin.Manifest;
        var name = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name;
        var desc = new List<string> { name + Environment.NewLine, m.Description ?? string.Empty };

        var confirm = await ModalWindow.ShowWarningAsync(owner, new ModalWindowOptions
        {
            Title = name,
            Heading = Core.Lang[LangId.Settings_Plugins_DeleteConfirm],
            Description = string.Join(Environment.NewLine, desc),
            Note = plugin.Dir,
            NoteStyle = InfoBarSeverity.Info,
        }, ModalWindowButton.Yes_No);
        if (confirm.ExitCode != DialogExitCode.OK) return;

        Core.DisablePlugin(plugin.Manifest.Id);
        await PluginTrustPolicy.RemoveAsync(plugin.Manifest.Id);
        DeletePluginFolder(plugin.Dir);

        ReloadPlugins();
        RebuildTable();
    }


    /// <summary>
    /// Deletes the plugin folder; if a file is still locked, stashes it in the trash for a later reap.
    /// </summary>
    private static void DeletePluginFolder(string pluginDir)
    {
        if (!Directory.Exists(pluginDir)) return;

        try
        {
            Directory.Delete(pluginDir, recursive: true);
            return;
        }
        catch { }

        try
        {
            var trashRoot = Path.Combine(BHelper.ConfigDir(Dir.Plugins), PluginRegistry.TRASH_DIR_NAME);
            Directory.CreateDirectory(trashRoot);
            Directory.Move(pluginDir, Path.Combine(trashRoot, Guid.NewGuid().ToString("N")));
        }
        catch { }
    }


    /// <summary>
    /// Trusts + enables the plugin, then hot-loads it so its codecs take effect without a restart.
    /// Returns <c>true</c> if the trust state changed.
    /// </summary>
    private static async Task<bool> EnablePluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        if (!await PluginTrustPolicy.TrustAsync(plugin.Manifest, plugin.Dir)) return false;

        await Core.EnablePluginAsync(plugin.Manifest, plugin.Dir);
        return true;
    }


    /// <summary>
    /// Rebuilds the plugins table from the working list (header + one row per plugin), toggling the empty state.
    /// </summary>
    private void RebuildTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = string.Empty }, // enable/disable toggle
            new() { Header = Core.Lang[LangId._Name] },
            new() { Header = Core.Lang[LangId._Type] },
            new() { Header = Core.Lang[LangId.Settings_Plugins_Status], Star = true },
        ];

        var rows = _plugins.Select(plugin =>
        {
            var m = plugin.Manifest;
            var state = PluginTrustPolicy.GetState(m, plugin.Dir);

            return new PhTableRow
            {
                Key = m.Id,
                Cells =
                [
                    ToggleCell(plugin, state),
                    NameCell(plugin),
                    PhTableControl.TextCell(m.Kind.ToString()),
                    StatusCell(state),
                ],
                Actions = BuildActions(plugin),
            };
        }).ToList();

        PART_Table.EmptyText = Core.Lang[LangId._Empty];
        PART_Table.Build(columns, rows);

        // keep the File type associations page's codec/plugin rows in sync with plugin changes
        this.FindAncestorOfType<SettingsWindowView>()?.NotifyPluginsChanged();
    }


    /// <summary>
    /// Builds the per-row hover actions: "Setting" (hidden for now), "Edit" (opens the plugin info
    /// window) and "Delete" (removes the plugin after a confirm).
    /// </summary>
    private List<PhTableAction> BuildActions((PluginManifest Manifest, string Dir) plugin)
    {
        return
        [
            new()
            {
                Icon = ResxIconId.IconSettings,
                Tooltip = Core.Lang[LangId.Menu_MnuSettings],
                IsVisible = false,
            },
            new()
            {
                Icon = ResxIconId.IconEdit,
                Tooltip = Core.Lang[LangId._Edit],
                Click = () => _ = EditAndRefreshAsync(plugin),
            },
            new()
            {
                Icon = ResxIconId.IconClose,
                Tooltip = Core.Lang[LangId._Delete],
                Click = () => _ = DeletePluginAsync(plugin),
            },
        ];
    }


    /// <summary>
    /// Runs the edit flow for the plugin (from the Edit action or the name link), then rebuilds the
    /// table. The enable/disable takes effect live, so no restart hint is shown.
    /// </summary>
    private async Task EditAndRefreshAsync((PluginManifest Manifest, string Dir) plugin)
    {
        _ = await EditPluginAsync(plugin);
        RebuildTable();
    }


    /// <summary>
    /// Disables the plugin and hot-unloads it so it stops taking effect without a restart.
    /// Returns <c>true</c> (the trust state changed).
    /// </summary>
    private static async Task<bool> DisablePluginAsync((PluginManifest Manifest, string Dir) plugin)
    {
        await PluginTrustPolicy.DisableAsync(plugin.Manifest.Id);
        Core.DisablePlugin(plugin.Manifest.Id);
        return true;
    }


    /// <summary>
    /// Builds the trust-status chip, colored by state via <see cref="PhChip"/>
    /// Returns <c>null</c> when there is nothing to show (missing/broken plugin).
    /// </summary>
    private static PhChip? StatusChip(PluginTrustPolicy.TrustState state)
    {
        var (key, variant) = state switch
        {
            PluginTrustPolicy.TrustState.Trusted => (LangId.Settings_Plugins_StatusEnabled, PhChipVariant.Success),
            PluginTrustPolicy.TrustState.Changed => (LangId.Settings_Plugins_StatusChanged, PhChipVariant.Warning),
            PluginTrustPolicy.TrustState.Disabled => (LangId.Settings_Plugins_StatusDisabled, PhChipVariant.Neutral),
            PluginTrustPolicy.TrustState.Untrusted => (LangId.Settings_Plugins_StatusUntrusted, PhChipVariant.Neutral),
            _ => ((LangId?)null, PhChipVariant.Neutral), // Missing -> nothing
        };

        if (key is null) return null;

        return new PhChip
        {
            Text = Core.Lang[key.Value],
            Variant = variant,
        };
    }


    /// <summary>
    /// The status cell: the trust-status chip (left-aligned), or an empty cell for a missing plugin.
    /// </summary>
    private static Control StatusCell(PluginTrustPolicy.TrustState state)
    {
        return StatusChip(state) is { } chip
            ? PhTableControl.WrapCell(chip)
            : PhTableControl.TextCell(string.Empty);
    }


    #region Table cell builders

    /// <summary>
    /// The name cell: the plugin name as a link button (opens the edit window), with the version
    /// below it when set.
    /// </summary>
    private Border NameCell((PluginManifest Manifest, string Dir) plugin)
    {
        var m = plugin.Manifest;
        var nameText = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name;

        var name = new PhButton
        {
            Text = nameText,
            Variant = PhButtonVariant.Link,
            MaxWidth = NAME_MAX_WIDTH,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        ToolTip.SetTip(name, nameText);
        name.Click += (_, _) => _ = EditAndRefreshAsync(plugin);

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

    #endregion // Table cell builders

}
