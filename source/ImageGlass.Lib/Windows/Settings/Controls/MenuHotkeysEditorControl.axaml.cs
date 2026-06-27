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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageGlass.Common.Localization;
using ImageGlass.Common.ServiceProviders;
using ImageGlass.Common.Types;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The searchable hotkeys table used by the Keyboard settings page.
/// </summary>
public partial class MenuHotkeysEditorControl : PhControl
{
    private const double MinListHeight = 220;
    private const double BottomGap = 40;
    private ScrollViewer? _pageScroll;

    // user overrides (clone of the staged config): absent key = default, empty array = no hotkey
    private Dictionary<LangId, Hotkey[]> _working = [];

    // every row in menu order; the ListBox shows a (possibly search-filtered) subset
    private readonly List<MenuHotkeyRowModel> _allRows = [];


    /// <summary>
    /// Raised after any edit (single row or reset all) so the host can re-stage the hotkeys.
    /// </summary>
    public event EventHandler? HotkeysChanged;


    public MenuHotkeysEditorControl()
    {
        InitializeComponent();

        PART_Search.TextChanged += (_, _) => ApplyFilter();
        PART_Reset.Click += (_, _) => ResetAll();

        PART_List.DoubleTapped += PART_List_DoubleTapped;
        PART_List.AddHandler(KeyDownEvent, PART_List_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }


    #region Public API

    /// <summary>
    /// Loads the user's menu-hotkey overrides (cloned into a working copy) and builds the table.
    /// </summary>
    public void LoadHotkeys(IReadOnlyDictionary<LangId, Hotkey[]> current)
    {
        _working = new Dictionary<LangId, Hotkey[]>(current);
        BuildRows();
        ApplyFilter();
    }


    /// <summary>
    /// Gets a clone of the current menu-hotkey overrides to stage into the config.
    /// </summary>
    public Dictionary<LangId, Hotkey[]> CurrentHotkeys => new(_working);

    #endregion // Public API


    #region Control events

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _pageScroll = this.FindAncestorOfType<ScrollViewer>();
        if (_pageScroll is not null) _pageScroll.PropertyChanged += PageScroll_PropertyChanged;

        // bounds are usually 0 at attach; size once layout settles
        Dispatcher.UIThread.Post(UpdateListMaxHeight, DispatcherPriority.Background);
    }


    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_pageScroll is not null) _pageScroll.PropertyChanged -= PageScroll_PropertyChanged;
        _pageScroll = null;
        base.OnDetachedFromVisualTree(e);
    }


    private void PageScroll_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty) UpdateListMaxHeight();
    }


    // Cap the list at the page viewport's remaining height (below the search/header chrome),
    // never below the floor; so it grows/shrinks with the window instead of a fixed height.
    private void UpdateListMaxHeight()
    {
        if (_pageScroll is null) return;

        var viewport = _pageScroll.Bounds.Height;
        if (viewport <= 0) return;

        if (PART_List.TranslatePoint(new Point(0, 0), _pageScroll) is not { } pt) return;

        var listTop = pt.Y + _pageScroll.Offset.Y;
        PART_List.MaxHeight = Math.Max(MinListHeight, viewport - listTop - BottomGap);
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        PART_Reset.Text = Core.Lang[LangId._ResetToDefault];
        PART_Search.PlaceholderText = Core.Lang[LangId._TypeToFilter];

        // re-localize paths in place (no rebuild) so reopening the page stays fast
        RelocalizeRows();
    }

    #endregion // Control events


    #region Rows

    /// <summary>
    /// Builds <see cref="_allRows"/> from the default menu actions, applying any working overrides.
    /// Paths/visibility come from the live main menu, so the menu tree isn't duplicated here.
    /// </summary>
    private void BuildRows()
    {
        _allRows.Clear();

        var (paths, allKeys) = MenuMap();

        foreach (var def in AppAPIProvider.DefaultMenuList)
        {
            var key = Lang.GetKey(def.LangKey);
            if (key is null) continue;

            string path;
            if (paths.TryGetValue(key.Value, out var p)) path = p;  // visible menu item
            else if (allKeys.Contains(key.Value)) continue;         // in the menu but hidden -> skip
            else path = Core.Lang[key.Value];                       // not a menu item (e.g. the menu button)

            var effective = _working.TryGetValue(key.Value, out var v) ? v : def.Hotkeys;
            _allRows.Add(new MenuHotkeyRowModel(key.Value, path, def.Hotkeys, effective));
        }

        UpdateConflicts();
    }


    /// <summary>
    /// Re-localizes the action paths of the existing rows in place (no row/container rebuild).
    /// </summary>
    private void RelocalizeRows()
    {
        if (_allRows.Count == 0) return;

        var (paths, _) = MenuMap();
        foreach (var row in _allRows)
        {
            row.ActionPath = paths.TryGetValue(row.MenuKey, out var p) ? p : Core.Lang[row.MenuKey];
        }
    }


    private static (Dictionary<LangId, string> Paths, HashSet<LangId> AllKeys) MenuMap()
        => App.MainWindow?.PART_MainView?.PART_Toolbar?.GetMenuActionMap()
            ?? ([], []);


    /// <summary>
    /// Flags every row whose effective hotkeys are shared with another row (duplicated assignment).
    /// </summary>
    private void UpdateConflicts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var row in _allRows)
        {
            foreach (var hk in row.Hotkeys)
                counts[hk.KeyString] = counts.GetValueOrDefault(hk.KeyString) + 1;
        }

        foreach (var row in _allRows)
        {
            row.IsConflict = row.Hotkeys.Any(hk => counts.GetValueOrDefault(hk.KeyString) > 1);
        }
    }


    /// <summary>
    /// Repopulates the ListBox with rows matching the search query (action path or hotkey text).
    /// </summary>
    private void ApplyFilter()
    {
        var query = PART_Search.Text?.Trim();
        var rows = string.IsNullOrEmpty(query)
            ? [.. _allRows]
            : _allRows.Where(r =>
                r.ActionPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.HotkeysText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        var selected = PART_List.SelectedItem;
        PART_List.ItemsSource = rows;
        PART_List.SelectedItem = selected;
        PART_Empty.IsVisible = rows.Count == 0;
        PART_TableBorder.IsVisible = rows.Count > 0;
    }

    #endregion // Rows


    #region Edit operations

    private void PART_List_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is MenuHotkeyRowModel row) _ = EditRowAsync(row);
    }


    // Enter edits the selected (or keyboard-focused) row; the edit button isn't a tab stop
    private void PART_List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        var row = PART_List.SelectedItem as MenuHotkeyRowModel
            ?? (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control)?.DataContext as MenuHotkeyRowModel;
        if (row is null) return;

        e.Handled = true;
        Dispatcher.UIThread.Post(() => _ = EditRowAsync(row));
    }


    private void EditBtn_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MenuHotkeyRowModel row) _ = EditRowAsync(row);
    }


    /// <summary>
    /// Opens the editor for a row; on success records the override (or removes it when the result
    /// matches the default), refreshes the row and notifies the host.
    /// </summary>
    private async Task EditRowAsync(MenuHotkeyRowModel row)
    {
        var win = new MenuHotkeyEditWindow(row.ActionPath, row.Hotkeys, row.DefaultHotkeys);
        if (await win.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (win.ResultHotkeys is not { } result) return;

        if (MenuHotkeyRowModel.HotkeysSetEqual(result, row.DefaultHotkeys))
        {
            _working.Remove(row.MenuKey);
        }
        else
        {
            _working[row.MenuKey] = result;
        }

        row.Hotkeys = result;
        UpdateConflicts();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>
    /// Clears all overrides, reverting every row to its default hotkeys.
    /// </summary>
    private void ResetAll()
    {
        _working.Clear();
        foreach (var row in _allRows) row.Hotkeys = row.DefaultHotkeys;
        UpdateConflicts();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion // Edit operations

}
