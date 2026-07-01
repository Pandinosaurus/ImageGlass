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
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using ImageGlass.Common.Localization;
using ImageGlass.Common.ServiceProviders;
using ImageGlass.Common.Types;
using ImageGlass.Tools;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The "Tools" settings page.
/// </summary>
public partial class ToolsSettingsView : SettingsPageView
{
    private const double NAME_MAX_WIDTH = 220;
    private const double HOTKEY_MAX_WIDTH = 200;

    // working copy of the registered tools; staged into the VM on change
    private readonly List<ExternalTool> _tools = [];


    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public ToolsSettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public ToolsSettingsView(SettingsViewModel vm, string navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
    {
        // copy the staged/config value so edits don't mutate the live config before commit
        _tools.AddRange(VM.GetValue(ConfigId.Tools, new ObservableCollection<ExternalTool>()));

        SetLocalizedText(PART_AddTool, LangId._Add);
        PART_AddTool.Click += async (_, _) => await AddOrEditToolAsync(null);

        SetLocalizedText(PART_GetMoreTools, LangId.Menu_MnuGetMoreTools);
        PART_GetMoreTools.Click += (_, _) => AppAPIProvider.IG_GetMoreTools();

        // rebuild on language change (also performs the initial render)
        AddLangRefresher(RebuildTable);

        RegisterSearchKey(PART_AddTool, LangId.Settings_Nav_Tools, ConfigId.Tools, LangId.Settings_Nav_Tools);
    }


    /// <summary>
    /// Stages the current working copy of tools into the view model.
    /// </summary>
    private void StageTools() => VM.SetValue(ConfigId.Tools, new ObservableCollection<ExternalTool>(_tools));


    /// <summary>
    /// Opens <see cref="ToolEditWindow"/> to add a new tool (when <paramref name="existing"/> is null)
    /// or edit an existing one, then updates the working copy and re-renders.
    /// </summary>
    private async Task AddOrEditToolAsync(ExternalTool? existing)
    {
        var win = new ToolEditWindow(existing, CollectTakenIds(except: existing));
        if (await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (win.ResultTool is not { } tool) return;

        var index = existing is not null ? _tools.IndexOf(existing) : -1;
        if (index >= 0) _tools[index] = tool;
        else _tools.Add(tool);

        StageTools();
        RebuildTable();
    }


    /// <summary>
    /// Removes a tool from the working copy and re-renders.
    /// </summary>
    private void DeleteTool(ExternalTool tool)
    {
        if (!_tools.Remove(tool)) return;

        StageTools();
        RebuildTable();
    }


    /// <summary>
    /// Gets the tool ids already in use, excluding the given tool's own id (so editing doesn't clash with itself).
    /// </summary>
    private HashSet<string> CollectTakenIds(ExternalTool? except)
    {
        var set = new HashSet<string>(_tools.Select(t => t.ToolId), StringComparer.OrdinalIgnoreCase);
        if (except is not null) set.Remove(except.ToolId);
        return set;
    }


    /// <summary>
    /// Rebuilds the tools table from the working copy (header + one row per tool), toggling the empty state.
    /// </summary>
    private void RebuildTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = Core.Lang[LangId._Name] },
            new() { Header = Core.Lang[LangId._Executable], Star = true },
            new() { Header = Core.Lang[LangId._Hotkeys] },
        ];

        var rows = _tools.Select(tool => new PhTableRow
        {
            Cells =
            [
                NameCell(tool),
                PhTableControl.TextCell(tool.Executable, selectable: true),
                PhTableControl.TextCell(HotkeysText(tool), HOTKEY_MAX_WIDTH),
            ],
            Actions =
            [
                new() { Icon = ResxIconId.IconEdit, Tooltip = Core.Lang[LangId._Edit], Click = () => _ = AddOrEditToolAsync(tool) },
                new() { Icon = ResxIconId.IconClose, Tooltip = Core.Lang[LangId._Delete], Click = () => DeleteTool(tool) },
            ],
        }).ToList();

        PART_Table.EmptyText = Core.Lang[LangId._Empty];
        PART_Table.Build(columns, rows);
    }


    private static string HotkeysText(ExternalTool tool)
        => string.Join(", ", tool.Hotkeys.Select(h => h.KeyString));


    #region Table cell builders

    /// <summary>
    /// The name cell: the tool name (capped + truncated), with the "Integrated" badge below when set.
    /// </summary>
    private static Control NameCell(ExternalTool tool)
    {
        var name = new SelectableTextBlock
        {
            Text = string.IsNullOrWhiteSpace(tool.ToolName) ? tool.ToolId : tool.ToolName,
            MaxWidth = NAME_MAX_WIDTH,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTabStop = false,
        };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(name);

        if (tool.IsIntegrated)
        {
            var badge = new PhTextBlock
            {
                Text = Core.Lang[LangId.Settings_Tools_Integrated],
                FontSize = Const.FONT_SIZE_SMALL,
            };
            badge[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PhAccentFill");
            stack.Children.Add(badge);
        }

        return PhTableControl.WrapCell(stack);
    }

    #endregion // Table cell builders

}
