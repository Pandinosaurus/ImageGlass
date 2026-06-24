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
using ImageGlass.Common.Types;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

/// <summary>
/// Working copy of the settings being edited, decoupled from <see cref="Core.Config"/>.
/// <para>
/// Edits are staged in <see cref="_pending"/> and only written to <see cref="Core.Config"/>
/// (and persisted to disk) when the user presses OK or Apply via <see cref="CommitAsync"/>.
/// Switching pages keeps pending edits; Cancel calls <see cref="Discard"/>.
/// </para>
/// </summary>
public sealed class SettingsViewModel : PhReactive
{
    // only changed values live here; everything else reads through to Core.Config
    private readonly Dictionary<ConfigId, object?> _pending = [];


    /// <summary>
    /// Gets the shared index of all setting rows (for search + navigate-by-config).
    /// </summary>
    public SettingsIndex Indexing { get; } = new();


    /// <summary>
    /// Gets whether there are uncommitted edits.
    /// </summary>
    public bool IsDirty => _pending.Count > 0;


    /// <summary>
    /// Gets the staged value for <paramref name="id"/>, falling back to the current
    /// <see cref="Core.Config"/> value (or <paramref name="defaultValue"/>) when not staged.
    /// </summary>
    public T GetValue<T>(ConfigId id, T defaultValue)
    {
        if (_pending.TryGetValue(id, out var v) && v is T staged) return staged;
        return Core.Config.Get(id, defaultValue);
    }


    /// <summary>
    /// Stages a value for <paramref name="id"/> and raises a change notification
    /// (so live UI inside the dialog can react). Does NOT touch <see cref="Core.Config"/>.
    /// </summary>
    public void SetValue(ConfigId id, object? value)
    {
        _pending[id] = value;
        _ = OnPropertyChanged(value, null, id.ToString());
    }


    /// <summary>
    /// Writes all staged edits into <see cref="Core.Config"/>, persists to disk,
    /// then clears the staging store and runs any post-apply actions.
    /// </summary>
    public async Task CommitAsync()
    {
        if (_pending.Count == 0) return;

        // 1. push staged values into the live config (raises PropertyChanged → live UI updates)
        var changedIds = _pending.Keys.ToList();
        foreach (var (id, value) in _pending)
        {
            if (value is not null) Core.Config.Set(id, value);
        }
        _pending.Clear();

        // 2. persist to disk
        await Core.Config.SaveAsync();

        // 3. run settings that need an explicit apply step (theme/language/list reload)
        RunApplyActions(changedIds);
    }


    /// <summary>
    /// Drops all staged edits.
    /// </summary>
    public void Discard() => _pending.Clear();


    /// <summary>
    /// Runs explicit follow-up actions for committed settings that don't propagate on
    /// their own via <see cref="Core.Config"/> bindings.
    /// </summary>
    private static void RunApplyActions(IReadOnlyList<ConfigId> changedIds)
    {
        // changes require reloading photo list
        var reloadList = changedIds.Any(static id => id
            is ConfigId.ImageLoadingOrder
            or ConfigId.ImageLoadingOrderType
            or ConfigId.EnableExplorerSortOrder
            or ConfigId.EnableSubfoldersLoading
            or ConfigId.EnableImageFolderGrouping
            or ConfigId.EnableHiddenImagesLoading);

        // changes require reloading current photo
        var reloadPhoto = changedIds.Any(static id => id
            is ConfigId.EnableOnlyLoadRawPreview
            or ConfigId.EnableOnlyLoadNonRawPreview
            or ConfigId.EnableAlwaysApplyColorProfile
            or ConfigId.ColorProfile
            or ConfigId.EnableVectorRenderer);

        // the new color profile must reach Core.DestColorProfile before the photo re-decodes
        if (changedIds.Contains(ConfigId.ColorProfile))
        {
            Core.UpdateDestColorProfile();
        }

        // updated hotkeys after toolbar buttons edited
        if (changedIds.Contains(ConfigId.ToolbarButtons))
        {
            Core.API.RegisterHotkeys();
        }

        // theme pack changed
        if (changedIds.Any(static id => id is ConfigId.DarkTheme or ConfigId.LightTheme)
            && Avalonia.Application.Current is App app)
        {
            _ = app.ApplyThemePackAsync(Core.IsSystemDarkMode, Core.AccentColor);
        }


        if (reloadList) Core.API.IG_ReloadList();
        if (reloadPhoto) Core.API.IG_Reload();
    }
}
