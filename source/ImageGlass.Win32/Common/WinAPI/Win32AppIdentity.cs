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
using System;
using System.IO;
using Windows.Win32;

namespace ImageGlass.Win32.Common;

/// <summary>
/// Runtime MSIX package-identity probe.
/// </summary>
public static class Win32AppIdentity
{
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    private static readonly Lazy<bool> _isPackaged = new(DetectPackaged);
    private static readonly Lazy<bool> _isUnvirtualizedResources = new(DetectUnvirtualizedResources);


    /// <summary>
    /// Whether the process runs with MSIX package identity (packaged / Store install).
    /// </summary>
    public static bool IsPackaged => _isPackaged.Value;


    /// <summary>
    /// Whether this packaged build opted out of resources virtualization (signed GitHub flavour).
    /// </summary>
    public static bool IsUnvirtualizedResources => _isUnvirtualizedResources.Value;


    private static bool DetectPackaged()
    {
        // null buffer -> ERROR_INSUFFICIENT_BUFFER when identity exists, APPMODEL_ERROR_NO_PACKAGE otherwise
        uint length = 0;
        return (int)PInvoke.GetCurrentPackageFullName(ref length, default) == ERROR_INSUFFICIENT_BUFFER;
    }


    private static bool DetectUnvirtualizedResources()
    {
        if (!_isPackaged.Value) return false;

        try
        {
            // deployed manifest is one level above the app dir (<packageRoot>\AppxManifest.xml)
            var manifest = Path.GetFullPath(Path.Combine(BHelper.BasePath, "..", "AppxManifest.xml"));
            return File.Exists(manifest)
                && File.ReadAllText(manifest).Contains("unvirtualizedResources", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

}
