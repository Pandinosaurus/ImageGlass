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
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace ImageGlass.Plugins;


/// <summary>
/// Clamps a plugin codec below the built-in ceiling for built-in formats, so a plugin can only
/// win formats no built-in handles (unless trusted with AllowOverrideBuiltins).
/// </summary>
internal static class PluginCodecPolicy
{
    /// <summary>
    /// The app's built-in image formats (from <see cref="Const.IMAGE_FORMATS"/>) a plugin must not outrank.
    /// </summary>
    public static readonly FrozenSet<string> CoreFormats =
        Const.IMAGE_FORMATS.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// Built-in selection ceiling (Skia decode = 100); a core-format plugin is clamped below it.
    /// </summary>
    public const int BUILTIN_PRIORITY_CEILING = 100;


    /// <summary>
    /// Whether any extension (normalized to a leading dot) is a built-in image format.
    /// </summary>
    public static bool ClaimsCoreFormat(IEnumerable<string> extensions)
    {
        foreach (var ext in extensions)
        {
            if (string.IsNullOrEmpty(ext)) continue;
            var normalized = ext.StartsWith('.') ? ext : "." + ext;
            if (CoreFormats.Contains(normalized)) return true;
        }
        return false;
    }


    /// <summary>
    /// Clamps a plugin priority to strictly below the built-in ceiling.
    /// </summary>
    public static int ClampToBuiltinCeiling(int reportedPriority)
    {
        return Math.Min(reportedPriority, BUILTIN_PRIORITY_CEILING - 1);
    }
}
