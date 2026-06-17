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
using Avalonia.Media;
using Avalonia.Threading;
using ImageGlass.Common.Localization;
using ImageGlass.UI;

namespace ImageGlass.Common.Windows;

/// <summary>
/// Base class for a single settings page (tab). Builds its content lazily and registers
/// its <see cref="SettingItem"/>s into the shared <see cref="SettingsIndex"/>.
/// </summary>
public abstract class SettingsPage : PhControl
{
    private bool _isBuilt;


    /// <summary>
    /// Gets the shared settings working-copy view model.
    /// </summary>
    protected SettingsViewModel VM { get; }

    /// <summary>
    /// Gets the unique nav id of this page (matches the sidebar item / <see cref="Config.LastOpenedSetting"/>).
    /// </summary>
    public string NavId { get; }

    /// <summary>
    /// Gets, sets the localization key of this page's sidebar label (used for search breadcrumbs).
    /// Assigned by the host before <see cref="EnsureBuilt"/>.
    /// </summary>
    public LangId? NavLabel { get; set; }


    protected SettingsPage(SettingsViewModel vm, string navId)
    {
        VM = vm;
        NavId = navId;
    }


    /// <summary>
    /// Builds the page content once and registers its setting items.
    /// </summary>
    public void EnsureBuilt()
    {
        if (_isBuilt) return;
        _isBuilt = true;

        Content = BuildContent();
    }


    /// <summary>
    /// Builds the page content. Subclasses use <see cref="SettingsBuilder"/> and register
    /// their setting rows into <see cref="VM"/>'s <see cref="SettingsIndex"/>.
    /// </summary>
    protected abstract Control BuildContent();


    /// <summary>
    /// Scrolls the given setting into view and focuses it (themed focus ring) so the user
    /// can spot where the search/config navigation landed.
    /// </summary>
    public virtual void ScrollToItem(SettingItem item)
    {
        var target = item.Target;
        if (target is null) return;

        // defer until the freshly shown page has completed a layout pass
        Dispatcher.UIThread.Post(() =>
        {
            target.BringIntoView();
            target.Focus(NavigationMethod.Tab); // shows the themed focus ring
        }, DispatcherPriority.Loaded);
    }
}



/// <summary>
/// Temporary placeholder page used until the real per-tab page is implemented.
/// The page title is shown in the window header, so this only shows a note.
/// </summary>
public sealed class PlaceholderSettingsPage : SettingsPage
{
    public PlaceholderSettingsPage(SettingsViewModel vm, string navId) : base(vm, navId)
    {
    }


    protected override Control BuildContent()
    {
        return new TextBlock
        {
            Text = "TODO",
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        };
    }
}
