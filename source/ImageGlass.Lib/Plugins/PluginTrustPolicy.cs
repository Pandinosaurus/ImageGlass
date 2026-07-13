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
using ImageGlass.Common;
using ImageGlass.Common.Types;
using ImageGlass.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ImageGlass.Plugins;


/// <summary>
/// Central policy that decides whether a native plugin is allowed to load.
/// A plugin runs only after the user explicitly enables it (consent), which pins the
/// SHA-256 of its native library in <c>Config.PluginTrust</c>. If the library later
/// changes, the pinned hash no longer matches and trust is withheld until the user
/// re-approves - this defends against a trusted plugin's binary being swapped.
/// </summary>
public static class PluginTrustPolicy
{
    /// <summary>
    /// Trust state of a plugin, used for both enforcement and UI display.
    /// </summary>
    public enum TrustState
    {
        /// <summary>The plugin library is missing or its manifest path is invalid.</summary>
        Missing,
        /// <summary>No trust entry exists; the plugin has never been enabled.</summary>
        Untrusted,
        /// <summary>A trust entry exists but is disabled by the user.</summary>
        Disabled,
        /// <summary>Enabled and the on-disk library matches the pinned hash.</summary>
        Trusted,
        /// <summary>Enabled but the library hash no longer matches the pin (needs re-consent).</summary>
        Changed,
    }


    /// <summary>
    /// Resolves and containment-validates the plugin's native library path,
    /// reusing the loader's path checks. Returns <c>null</c> if invalid.
    /// </summary>
    public static string? ResolveLibraryPath(PluginManifest manifest, string pluginDir)
    {
        return PluginRegistry.TryResolvePluginLibraryPath(manifest.Executable, pluginDir, out var path)
            ? path
            : null;
    }


    /// <summary>
    /// Computes the lowercase hex SHA-256 of a file, or <c>null</c> on any I/O error.
    /// </summary>
    public static string? ComputeSha256(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// Enforcement gate used by the loader: returns <c>true</c> only when the plugin is
    /// enabled AND the on-disk library still matches the pinned SHA-256.
    /// </summary>
    public static bool IsTrusted(string pluginId, string libraryPath)
    {
        if (!Core.Config.PluginTrust.TryGetValue(pluginId, out var info) || info is null || !info.Enabled)
            return false;

        var hash = ComputeSha256(libraryPath);
        return hash is not null && string.Equals(hash, info.Hash, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Returns <c>true</c> if the plugin is explicitly trusted to outrank built-in codecs
    /// for core formats (via <see cref="PluginTrustInfo.AllowOverrideBuiltins"/>).
    /// </summary>
    public static bool AllowsBuiltinOverride(string pluginId)
    {
        return Core.Config.PluginTrust.TryGetValue(pluginId, out var info)
            && info is not null && info.AllowOverrideBuiltins;
    }


    /// <summary>
    /// Computes the current <see cref="TrustState"/> of a plugin for UI display.
    /// </summary>
    public static TrustState GetState(PluginManifest manifest, string pluginDir)
    {
        var libraryPath = ResolveLibraryPath(manifest, pluginDir);
        if (libraryPath is null || !File.Exists(libraryPath)) return TrustState.Missing;

        if (!Core.Config.PluginTrust.TryGetValue(manifest.Id, out var info) || info is null)
            return TrustState.Untrusted;

        if (!info.Enabled) return TrustState.Disabled;

        var hash = ComputeSha256(libraryPath);
        return hash is not null && string.Equals(hash, info.Hash, StringComparison.OrdinalIgnoreCase)
            ? TrustState.Trusted
            : TrustState.Changed;
    }


    /// <summary>
    /// Enables the plugin and pins the current library hash, then persists the config.
    /// Returns <c>false</c> if the library could not be resolved or hashed.
    /// </summary>
    public static async Task<bool> TrustAsync(PluginManifest manifest, string pluginDir)
    {
        var libraryPath = ResolveLibraryPath(manifest, pluginDir);
        if (libraryPath is null) return false;

        var hash = ComputeSha256(libraryPath);
        if (hash is null) return false;

        // reassign a fresh dictionary so the Config setter records the change
        var trust = new Dictionary<string, PluginTrustInfo>(Core.Config.PluginTrust, StringComparer.Ordinal)
        {
            [manifest.Id] = new PluginTrustInfo { Enabled = true, Hash = hash },
        };
        Core.Config.PluginTrust = trust;

        await Core.Config.SaveAsync();
        return true;
    }


    /// <summary>
    /// Disables the plugin (keeps a disabled entry), then persists the config.
    /// </summary>
    public static async Task DisableAsync(string pluginId)
    {
        Core.Config.PluginTrust.TryGetValue(pluginId, out var existing);

        var trust = new Dictionary<string, PluginTrustInfo>(Core.Config.PluginTrust, StringComparer.Ordinal)
        {
            [pluginId] = new PluginTrustInfo { Enabled = false, Hash = existing?.Hash ?? string.Empty },
        };
        Core.Config.PluginTrust = trust;

        await Core.Config.SaveAsync();
    }


    /// <summary>
    /// Drops the plugin's trust entry (used when the plugin is deleted), then persists the config.
    /// </summary>
    public static async Task RemoveAsync(string pluginId)
    {
        if (!Core.Config.PluginTrust.ContainsKey(pluginId)) return;

        var trust = new Dictionary<string, PluginTrustInfo>(Core.Config.PluginTrust, StringComparer.Ordinal);
        trust.Remove(pluginId);
        Core.Config.PluginTrust = trust;

        await Core.Config.SaveAsync();
    }
}
