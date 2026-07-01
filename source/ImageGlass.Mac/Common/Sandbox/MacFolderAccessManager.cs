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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageGlass.Common;
using ImageGlass.Common.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Mac.Common.Sandbox;


/// <summary>
/// Coordinates macOS App Sandbox folder access so ImageGlass can browse sibling
/// files in a folder. For each folder it needs to enumerate, it:
/// <list type="number">
///   <item>returns immediately if not sandboxed, or if access already held;</item>
///   <item>uses an existing OS grant (the opened file's powerbox grant) if it
///         already covers the folder, persisting a bookmark for next time;</item>
///   <item>resolves a previously saved security-scoped bookmark;</item>
///   <item>otherwise auto-prompts a folder picker (once per folder per session),
///         then persists a bookmark from the granted folder.</item>
/// </list>
/// Resolved-bookmark scopes are held open for the process lifetime so plain
/// <c>System.IO</c> enumeration and the file watcher keep working.
/// </summary>
internal sealed class MacFolderAccessManager
{
    private readonly MacFolderBookmarkStore _store = new();
    private readonly Lock _lock = new();

    // Folders we currently hold access to. Value is the native scope handle to
    // release on shutdown, or 0 when access comes from a session powerbox grant
    // (no explicit security scope to release).
    private readonly Dictionary<string, nint> _activeScopes = new(StringComparer.OrdinalIgnoreCase);

    // Folders the user declined this session — don't nag again until next launch.
    private readonly HashSet<string> _deniedThisSession = new(StringComparer.OrdinalIgnoreCase);


    public MacFolderAccessManager()
    {
        if (MacSandbox.IsSandboxed) _store.Load();
    }


    /// <summary>
    /// Ensures the app can enumerate <paramref name="folderPath"/>, prompting the
    /// user once if necessary. Returns <c>true</c> if access is available (always
    /// <c>true</c> when not sandboxed).
    /// </summary>
    public async Task<bool> EnsureAccessAsync(string folderPath)
    {
        if (!MacSandbox.IsSandboxed) return true;
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return true;

        folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));

        lock (_lock)
        {
            if (_activeScopes.ContainsKey(folderPath)) return true;
        }

        // 1. The folder may already be reachable via the opened file's powerbox
        //    grant (Launch Services / drag-drop / earlier picker). If so, persist
        //    a bookmark now so future launches skip the prompt.
        if (CanList(folderPath))
        {
            MarkActive(folderPath, 0);
            await TryPersistBookmarkAsync(folderPath).ConfigureAwait(false);
            return true;
        }

        // 2. Try a previously saved bookmark (exact or ancestor grant).
        if (_store.TryGetCovering(folderPath, out var base64))
        {
            var resolved = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try { return MacSandbox.ResolveAndStartAccess(Convert.FromBase64String(base64)); }
                catch { return (Path: (string?)null, Handle: (nint)0, IsStale: false); }
            });

            if (resolved.Handle != 0 && CanList(folderPath))
            {
                MarkActive(folderPath, resolved.Handle);
                return true;
            }

            // bookmark no longer valid — release and forget it
            if (resolved.Handle != 0) MacSandbox.StopAccess(resolved.Handle);
            if (resolved.Path is not null) _store.Remove(resolved.Path);
        }

        // 3. Already declined this session.
        lock (_lock)
        {
            if (_deniedThisSession.Contains(folderPath)) return false;
        }

        // 4. Auto-prompt a folder picker on the UI thread.
        var granted = await Dispatcher.UIThread.InvokeAsync(() => PromptForFolderAsync(folderPath));
        if (!granted)
        {
            lock (_lock) { _deniedThisSession.Add(folderPath); }
            return false;
        }

        return true;
    }


    /// <summary>
    /// Releases all held security scopes. Call on app shutdown.
    /// </summary>
    public void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (var handle in _activeScopes.Values)
            {
                if (handle != 0) MacSandbox.StopAccess(handle);
            }
            _activeScopes.Clear();
        }
    }


    #region Helpers

    /// <summary>
    /// Shows a folder picker defaulted to <paramref name="folderPath"/>, and on a
    /// covering grant persists a security-scoped bookmark. Runs on the UI thread.
    /// </summary>
    private async Task<bool> PromptForFolderAsync(string folderPath)
    {
        var top = GetMainTopLevel();
        if (top?.StorageProvider is not { CanPickFolder: true } sp) return false;

        IStorageFolder? startLocation = null;
        try { startLocation = await sp.TryGetFolderFromPathAsync(folderPath); }
        catch { }

        IReadOnlyList<IStorageFolder> picked;
        try
        {
            picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Core.Lang[LangId._FolderAccessPrompt],
                AllowMultiple = false,
                SuggestedStartLocation = startLocation,
            });
        }
        catch
        {
            return false;
        }

        var grantedPath = picked?.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(grantedPath)) return false;

        grantedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(grantedPath));

        // The grant must cover the folder we actually need to enumerate.
        if (!MacFolderBookmarkStore.IsSameOrAncestor(grantedPath, folderPath))
        {
            return false;
        }

        // The picker grant is now active for this session (path-based). Persist a
        // bookmark from the granted folder so later launches don't re-prompt.
        var bookmark = MacSandbox.CreateFolderBookmark(grantedPath);
        if (bookmark is not null)
        {
            _store.Set(grantedPath, Convert.ToBase64String(bookmark));
        }

        // No explicit native scope to release for a session powerbox grant.
        MarkActive(folderPath, 0);
        return true;
    }


    /// <summary>
    /// Best-effort: create and persist a bookmark for a folder we can already access.
    /// </summary>
    private async Task TryPersistBookmarkAsync(string folderPath)
    {
        if (_store.TryGetCovering(folderPath, out _)) return;

        var bookmark = await Dispatcher.UIThread.InvokeAsync(() => MacSandbox.CreateFolderBookmark(folderPath));
        if (bookmark is not null)
        {
            _store.Set(folderPath, Convert.ToBase64String(bookmark));
        }
    }


    private void MarkActive(string folderPath, nint handle)
    {
        lock (_lock) { _activeScopes[folderPath] = handle; }
    }


    /// <summary>
    /// Probes whether the directory can be listed. Returns <c>false</c> only when
    /// access is denied; an empty (but accessible) folder returns <c>true</c>.
    /// </summary>
    private static bool CanList(string folderPath)
    {
        try
        {
            using var e = Directory.EnumerateFileSystemEntries(folderPath).GetEnumerator();
            e.MoveNext();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }


    private static TopLevel? GetMainTopLevel()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        return lifetime?.MainWindow;
    }

    #endregion // Helpers

}
