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
using Avalonia.Layout;
using Avalonia.Media;
using ImageGlass.Common.Actions;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Types;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The "Mouse" settings page.
/// </summary>
public partial class MouseSettingsView : SettingsPageView
{
    private static readonly Thickness CELL_PADDING = new(10, 6);

    private readonly Dictionary<MouseWheelEvent, MouseWheelAction> _wheelActions = [];
    private readonly Dictionary<MouseClickEvent, SingleAction> _clickActions = [];


    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public MouseSettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public MouseSettingsView(SettingsViewModel vm, string navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
    {
        BuildWheelActions();
        BuildClickActions();
    }


    #region Mouse wheel actions

    /// <summary>
    /// Loads the working copy of the wheel actions and wires the table + reset link.
    /// </summary>
    private void BuildWheelActions()
    {
        // seed from defaults (effective values), then overlay the stored config
        foreach (var (evt, action) in Config.DefaultMouseWheelActions) _wheelActions[evt] = action;
        foreach (var (evt, action) in VM.GetValue(ConfigId.MouseWheelActions,
            new Dictionary<MouseWheelEvent, MouseWheelAction>()))
        {
            _wheelActions[evt] = action;
        }

        SetLocalizedText(PART_ResetWheel, LangId._ResetToDefault);
        PART_ResetWheel.Click += (_, _) => ResetWheelActions();

        AddLangRefresher(RebuildWheelTable);

        RegisterSearchKey(PART_ResetWheel, LangId.FrmSettings_MouseWheelAction,
            ConfigId.MouseWheelActions, LangId.FrmSettings_MouseWheelAction);
    }


    /// <summary>
    /// Stages the current working copy of wheel actions into the view model.
    /// </summary>
    private void StageWheelActions()
        => VM.SetValue(ConfigId.MouseWheelActions,
            new Dictionary<MouseWheelEvent, MouseWheelAction>(_wheelActions));


    /// <summary>
    /// Restores the default wheel actions and re-renders.
    /// </summary>
    private void ResetWheelActions()
    {
        _wheelActions.Clear();
        foreach (var (evt, action) in Config.DefaultMouseWheelActions) _wheelActions[evt] = action;

        StageWheelActions();
        RebuildWheelTable();
    }


    /// <summary>
    /// Rebuilds the wheel rows: each event shows its label above an action dropdown.
    /// </summary>
    private void RebuildWheelTable()
    {
        PART_WheelTable.Children.Clear();

        foreach (var evt in Enum.GetValues<MouseWheelEvent>())
        {
            var row = new StackPanel { Spacing = 5 };
            row.Children.Add(new PhTextBlock { Text = EnumLabel("MouseWheelEvent", evt) });
            row.Children.Add(BuildWheelCombo(evt));

            PART_WheelTable.Children.Add(row);
        }
    }


    /// <summary>
    /// Builds the action dropdown for a wheel event, bound to the working copy.
    /// </summary>
    private ComboBox BuildWheelCombo(MouseWheelEvent evt)
    {
        var combo = new ComboBox
        {
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var current = _wheelActions.GetValueOrDefault(evt, MouseWheelAction.DoNothing);
        var selectedIndex = 0;

        var actions = Enum.GetValues<MouseWheelAction>();
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            combo.Items.Add(new ComboBoxItem
            {
                Tag = action,
                Content = EnumLabel("MouseWheelAction", action),
            });
            if (action == current) selectedIndex = i;
        }
        combo.SelectedIndex = selectedIndex;

        // subscribe after the initial selection so loading doesn't stage
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: MouseWheelAction action })
            {
                _wheelActions[evt] = action;
                StageWheelActions();
            }
        };

        return combo;
    }

    #endregion // Mouse wheel actions



    #region Mouse click actions

    /// <summary>
    /// Loads the working copy of the click actions and wires the table + reset link.
    /// </summary>
    private void BuildClickActions()
    {
        // seed from defaults (effective values), then overlay the stored config
        foreach (var (evt, action) in Config.DefaultMouseClickActions) _clickActions[evt] = action;
        foreach (var (evt, action) in VM.GetValue(ConfigId.MouseClickActions,
            new Dictionary<MouseClickEvent, SingleAction>()))
        {
            _clickActions[evt] = action;
        }

        SetLocalizedText(PART_ResetClick, LangId._ResetToDefault);
        PART_ResetClick.Click += (_, _) => ResetClickActions();

        AddLangRefresher(RebuildClickTable);

        RegisterSearchKey(PART_ResetClick, LangId.FrmSettings_MouseClickAction,
            ConfigId.MouseClickActions, LangId.FrmSettings_MouseClickAction);
    }


    /// <summary>
    /// Stages the current working copy of click actions into the view model.
    /// </summary>
    private void StageClickActions()
        => VM.SetValue(ConfigId.MouseClickActions,
            new Dictionary<MouseClickEvent, SingleAction>(_clickActions));


    /// <summary>
    /// Restores the default click actions and re-renders.
    /// </summary>
    private void ResetClickActions()
    {
        _clickActions.Clear();
        foreach (var (evt, action) in Config.DefaultMouseClickActions) _clickActions[evt] = action;

        StageClickActions();
        RebuildClickTable();
    }


    /// <summary>
    /// Rebuilds the click table: one row per event with a label, the current action, and an Edit link.
    /// </summary>
    private void RebuildClickTable()
    {
        PART_ClickTableBody.Children.Clear();
        PART_ClickTableBody.RowDefinitions.Clear();

        var events = Enum.GetValues<MouseClickEvent>();
        for (var i = 0; i < events.Length; i++)
        {
            var evt = events[i];
            PART_ClickTableBody.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            if (i > 0) AddCell(HLine(ResxId.IG_BorderNeutralBrush), i, 0, 3);

            AddCell(new PhTextBlock
            {
                Text = EnumLabel("MouseClickEvent", evt),
                Padding = CELL_PADDING,
                VerticalAlignment = VerticalAlignment.Center,
            }, i, 0);
            AddCell(ActionCell(evt), i, 1);
            AddCell(EditCell(evt), i, 2);
        }
    }


    /// <summary>
    /// The action-summary cell: the executable to run, or "Do nothing" when the event is unbound.
    /// </summary>
    private TextBlock ActionCell(MouseClickEvent evt)
    {
        var exe = _clickActions.GetValueOrDefault(evt)?.Executable?.Trim();
        var isEmpty = string.IsNullOrEmpty(exe);

        var tb = new TextBlock
        {
            Text = isEmpty ? Core.Lang[LangId.MouseWheelAction_DoNothing] : exe,
            Padding = CELL_PADDING,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 280,
            FontStyle = isEmpty ? FontStyle.Italic : FontStyle.Normal,
            Opacity = isEmpty ? 0.6 : 1,
        };
        if (!isEmpty) ToolTip.SetTip(tb, exe);

        return tb;
    }


    /// <summary>
    /// The Edit link cell that opens the action editor for a click event.
    /// </summary>
    private Border EditCell(MouseClickEvent evt)
    {
        var btnEdit = new PhButton { Variant = PhButtonVariant.Link, Text = Core.Lang[LangId._Edit] };
        btnEdit.Click += async (_, _) => await EditClickActionAsync(evt);

        return new Border
        {
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = btnEdit,
        };
    }


    /// <summary>
    /// Opens the editor for a click event, updates the working copy (an empty executable unbinds it)
    /// and re-renders.
    /// </summary>
    private async Task EditClickActionAsync(MouseClickEvent evt)
    {
        var existing = _clickActions.GetValueOrDefault(evt);
        var window = new MouseClickActionEditWindow(EnumLabel("MouseClickEvent", evt), existing);

        if (await window.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (window.ResultAction is not { } result) return;

        if (string.IsNullOrEmpty(result.Executable)) _clickActions.Remove(evt);
        else _clickActions[evt] = result;

        StageClickActions();
        RebuildClickTable();
    }

    #endregion // Mouse click actions



    #region Helpers

    /// <summary>
    /// Gets the localized label of an enum value via the <c>{EnumType}_{Value}</c> key.
    /// </summary>
    private static string EnumLabel<TEnum>(string enumName, TEnum value) where TEnum : struct, Enum
        => Lang.GetKey($"{enumName}_{value}") is { } key ? Core.Lang[key] : value.ToString();


    /// <summary>
    /// Places a control into the click-table grid.
    /// </summary>
    private void AddCell(Control content, int row, int col, int colSpan = 1)
    {
        Grid.SetRow(content, row);
        Grid.SetColumn(content, col);
        if (colSpan > 1) Grid.SetColumnSpan(content, colSpan);
        PART_ClickTableBody.Children.Add(content);
    }


    /// <summary>
    /// Creates a 1px top-aligned horizontal rule whose color follows the theme.
    /// </summary>
    private static Border HLine(ResxId brushId)
    {
        var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
        line[!Border.BackgroundProperty] = Resx.CreateBinding(brushId);

        return line;
    }

    #endregion // Helpers

}
