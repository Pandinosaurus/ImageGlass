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
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
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
    private static readonly Thickness CELL_PADDING = new(10, 6);
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

        SetLocalizedText(PART_GetMoreTools, LangId.FrmMain_MnuGetMoreTools);
        PART_GetMoreTools.Click += (_, _) => AppAPIProvider.IG_GetMoreTools();

        // rebuild on language change (also performs the initial render)
        AddLangRefresher(RebuildTable);

        RegisterSearchKey(PART_AddTool, LangId.FrmSettings_Nav_Tools, ConfigId.Tools, LangId.FrmSettings_Nav_Tools);
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
        PART_TableBody.Children.Clear();
        PART_TableBody.RowDefinitions.Clear();

        var hasTools = _tools.Count > 0;
        PART_Empty.IsVisible = !hasTools;
        PART_TableBorder.IsVisible = hasTools;
        if (!hasTools) return;

        // header row + underline spanning all columns
        PART_TableBody.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCell(HeaderCell(LangId._Name), 0, 0);
        AddCell(HeaderCell(LangId._Executable), 0, 1);
        AddCell(HeaderCell(LangId._Hotkeys), 0, 2);
        AddCell(new Panel(), 0, 3); // actions column (no header)
        AddCell(HLine(ResxId.IG_BorderControlBrush, VerticalAlignment.Bottom), 0, 0, 4);

        // data rows
        for (var i = 0; i < _tools.Count; i++)
        {
            var tool = _tools[i];
            var row = i + 1;
            PART_TableBody.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // separator above every row except the first
            if (i > 0) AddCell(HLine(ResxId.IG_BorderNeutralBrush, VerticalAlignment.Top), row, 0, 4);

            AddCell(NameCell(tool), row, 0);
            AddCell(TextCell(tool.Executable), row, 1);
            AddCell(TextCell(HotkeysText(tool), HOTKEY_MAX_WIDTH), row, 2);
            AddCell(ActionsCell(tool), row, 3);
        }
    }


    private static string HotkeysText(ExternalTool tool)
        => string.Join(", ", tool.Hotkeys.Select(h => h.KeyString));


    #region Table cell builders

    private void AddCell(Control content, int row, int col, int colSpan = 1)
    {
        Grid.SetRow(content, row);
        Grid.SetColumn(content, col);
        if (colSpan > 1) Grid.SetColumnSpan(content, colSpan);
        PART_TableBody.Children.Add(content);
    }


    /// <summary>
    /// Creates a 1px horizontal rule whose color follows the theme (via a dynamic resource binding).
    /// </summary>
    private static Border HLine(ResxId brushId, VerticalAlignment align)
    {
        var line = new Border { Height = 1, VerticalAlignment = align };
        line[!Border.BackgroundProperty] = Resx.CreateBinding(brushId);

        return line;
    }


    private static PhTextBlock HeaderCell(LangId key) => new()
    {
        LangKey = key,
        FontWeight = FontWeight.SemiBold,
        Padding = CELL_PADDING,
        VerticalAlignment = VerticalAlignment.Center,
    };


    /// <summary>
    /// A text cell that truncates with an ellipsis (optionally capped to <paramref name="maxWidth"/>),
    /// with a tooltip showing the full text.
    /// </summary>
    private static Border TextCell(string text, double maxWidth = 0)
    {
        var tb = new TextBlock
        {
            Text = text,
            Padding = CELL_PADDING,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (maxWidth > 0) tb.MaxWidth = maxWidth;
        if (!string.IsNullOrEmpty(text)) ToolTip.SetTip(tb, text);

        return new Border { Child = tb };
    }


    /// <summary>
    /// The name cell: the tool name (capped + truncated), with the "Integrated" badge below when set.
    /// </summary>
    private Control NameCell(ExternalTool tool)
    {
        var name = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(tool.ToolName) ? tool.ToolId : tool.ToolName,
            MaxWidth = NAME_MAX_WIDTH,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(name);

        if (tool.IsIntegrated)
        {
            var badge = new PhTextBlock
            {
                Text = Core.Lang[LangId.FrmSettings_Tools_Integrated],
                FontSize = Const.FONT_SIZE_SMALL,
            };
            badge[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PhAccentFill");
            stack.Children.Add(badge);
        }

        return new Border { Padding = CELL_PADDING, Child = stack };
    }


    /// <summary>
    /// The actions cell: Edit + Delete icon buttons.
    /// </summary>
    private Border ActionsCell(ExternalTool tool)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(IconButton(ResxIconId.IconEdit, LangId._Edit, () => _ = AddOrEditToolAsync(tool)));
        panel.Children.Add(IconButton(ResxIconId.IconClose, LangId._Delete, () => DeleteTool(tool)));

        return new Border { Padding = new Thickness(8, 2), Child = panel };
    }


    /// <summary>
    /// Builds a tool-button with a filled icon glyph and a tooltip.
    /// </summary>
    private static PhToolButton IconButton(ResxIconId icon, LangId tooltip, Action onClick)
    {
        var glyph = new Path
        {
            Width = Const.FONT_SIZE_BODY,
            Height = Const.FONT_SIZE_BODY,
            Data = Resx.GetIcon(icon),
            Stretch = Stretch.Uniform,
        };
        glyph[!Shape.FillProperty] = Resx.CreateBinding(ResxId.TextControlForeground);

        var btn = new PhToolButton
        {
            Padding = new Thickness(7),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            IsTabStop = false,
            Content = glyph,
        };
        ToolTip.SetTip(btn, Core.Lang[tooltip]);
        btn.Click += (_, _) => onClick();

        return btn;
    }

    #endregion // Table cell builders

}
