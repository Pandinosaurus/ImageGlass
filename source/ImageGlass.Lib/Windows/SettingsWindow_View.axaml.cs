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
using Avalonia.Input;
using ImageGlass.Common.Localization;
using ImageGlass.UI;
using System.Collections.Generic;
using System.Linq;

namespace ImageGlass.Common.Windows;

public partial class SettingsWindowView : PhControl
{
    private SettingsViewModel _vm = null!;
    private List<SettingsNavItem> _navItems = [];
    private readonly Dictionary<string, SettingsPage> _pages = [];


    /// <summary>
    /// Gets the nav id of the currently shown page.
    /// </summary>
    public string CurrentNavId { get; private set; } = string.Empty;



    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public SettingsWindowView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the settings view bound to the given working-copy view model.
    /// </summary>
    public SettingsWindowView(SettingsViewModel vm) : this()
    {
        InitSettingsPage(vm);
    }



    #region Override Methods

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        if (PART_Search is not null)
        {
            PART_Search.PlaceholderText = Core.Lang[LangId.FrmSettings_SearchPlaceholder];
        }

        // re-template the sidebar so the localized labels refresh
        if (PART_Sidebar is not null)
        {
            PART_Sidebar.ItemsSource = null;
            PART_Sidebar.ItemsSource = _navItems;
            NavigateTo(CurrentNavId);
        }
    }

    #endregion // Override Methods



    #region Control Events

    private void Sidebar_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PART_Sidebar.SelectedItem is SettingsNavItem nav) ShowPage(nav);
    }


    private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var results = _vm.Index.Search(PART_Search.Text).Take(25).ToList();
        PART_SearchResults.ItemsSource = results;
        PART_SearchPopup.IsOpen = results.Count > 0;
    }


    private void TxtSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        // Enter jumps to the first search result (if any)
        if (e.Key != Key.Enter || !PART_SearchPopup.IsOpen) return;

        if (PART_SearchResults.Items.Count > 0 && PART_SearchResults.Items[0] is SettingItem item)
        {
            e.Handled = true;
            JumpToSetting(item);
        }
    }


    private void SearchResults_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PART_SearchResults.SelectedItem is SettingItem item) JumpToSetting(item);
    }

    #endregion // Control Events



    #region Methods

    private void InitSettingsPage(SettingsViewModel vm)
    {
        _vm = vm;
        _navItems = SettingsNavItem.CreateDefaultList();

        // Build every page up front so the search index and navigate-by-config see all
        // settings (not just visited pages). Pages are lightweight.
        foreach (var navItem in _navItems)
        {
            var page = navItem.CreatePage(_vm);
            page.EnsureBuilt();
            _pages[navItem.NavId] = page;
        }

        // search box
        PART_Search.PlaceholderText = Core.Lang[LangId.FrmSettings_SearchPlaceholder];
        PART_Search.TextChanged += TxtSearch_TextChanged;
        PART_Search.KeyDown += TxtSearch_KeyDown;
        PART_SearchResults.SelectionChanged += SearchResults_SelectionChanged;

        // sidebar
        PART_Sidebar.ItemsSource = _navItems;
        PART_Sidebar.SelectionChanged += Sidebar_SelectionChanged;

        // default selection: restore the last opened page, else the first item
        var restoreId = _navItems.Any(i => i.NavId == Core.Config.LastOpenedSetting)
            ? Core.Config.LastOpenedSetting
            : _navItems[0].NavId;
        NavigateTo(restoreId);
    }


    #region Navigation

    /// <summary>
    /// Moves keyboard focus to the search box.
    /// </summary>
    public void FocusSearch() => PART_Search?.Focus();


    /// <summary>
    /// Selects the sidebar item with the given nav id (shows its page).
    /// </summary>
    public void NavigateTo(string navId)
    {
        var item = _navItems.FirstOrDefault(i => i.NavId == navId);
        if (item is null) return;

        PART_Sidebar.SelectedItem = item; // raises SelectionChanged → ShowPage
    }


    /// <summary>
    /// Navigates to the page hosting the given config id and scrolls to it.
    /// No-op when the config id is unknown / not registered.
    /// </summary>
    public void NavigateToConfig(string? configId)
    {
        var item = _vm.Index.FindByConfigId(configId);
        if (item is null) return;

        JumpToSetting(item);
    }


    private void ShowPage(SettingsNavItem nav)
    {
        if (!_pages.TryGetValue(nav.NavId, out var page)) return;

        PART_ContentHost.Content = page;
        PART_Title.Text = nav.LabelText;
        CurrentNavId = nav.NavId;

        // remember the last viewed page (persisted to disk on OK/Apply/app exit)
        Core.Config.LastOpenedSetting = nav.NavId;
    }


    private void JumpToSetting(SettingItem item)
    {
        PART_SearchPopup.IsOpen = false;
        PART_SearchResults.SelectedItem = null;

        NavigateTo(item.PageNavId);
        if (_pages.TryGetValue(item.PageNavId, out var page))
        {
            page.ScrollToItem(item);
        }
    }

    #endregion // Navigation


    #endregion // Methods


}
