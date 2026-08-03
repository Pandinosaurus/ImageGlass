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
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ImageGlass.Common.Types;


public static class SavingExts
{
    /// <summary>
    /// The formats the app itself offers, in a hand-tuned order the picker keeps.
    /// </summary>
    private static IReadOnlyCollection<KeyValuePair<string, string>> BuiltInExtensions =>
    [
        new(".png",   "PNG"),
        new(".jpg",   "JPG"),
        new(".jxl",   "JXL"),
        new(".webp",  "WEBP"),
        new(".avif",  "AVIF"),

        new(".bmp",   "BMP"),
        new(".gif",   "GIF"),
        new(".tiff",  "TIFF"),

        new(".emf",   "EMF"),
        new(".exif",  "EXIF"),
        new(".ico",   "ICO"),
        new(".wmf",   "WMF"),
        new(".b64",   "Base64"),
        new(".txt",   "Base64 text"),
    ];


    /// <summary>
    /// Gets the file picker choices for save file dialog, including the formats contributed by
    /// loaded codec plugins. Rebuilt on every get, so enabling a plugin takes effect immediately.
    /// </summary>
    public static ImmutableList<FilePickerFileType> FilePickerFileTypeChoices => BuildChoices();


    /// <summary>
    /// Gets, sets the last extensions used for saving.
    /// </summary>
    public static FilePickerFileType? LastSavedFileType { get; set; }


    /// <summary>
    /// Builds the picker list: the built-ins in their curated order, then any plugin-only format,
    /// highest encode priority first. Exactly one entry per extension.
    /// </summary>
    private static ImmutableList<FilePickerFileType> BuildChoices()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<FilePickerFileType>(BuiltInExtensions.Count + 8);

        foreach (var (ext, label) in BuiltInExtensions)
        {
            if (!seen.Add(ext)) continue;
            list.Add(new FilePickerFileType(label) { Patterns = [$"*{ext}"] });
        }

        foreach (var plugin in GetPluginExtensions())
        {
            // a built-in already owns this slot; who actually writes it is decided at save time
            if (!seen.Add(plugin.Ext)) continue;
            list.Add(new FilePickerFileType(plugin.Ext.TrimStart('.').ToUpperInvariant())
            {
                Patterns = [$"*{plugin.Ext}"],
            });
        }

        return list.ToImmutableList();
    }


    /// <summary>
    /// Plugin-contributed writable extensions, ordered by encode priority then name.
    /// </summary>
    private static IEnumerable<(string Ext, int EncodePriority)> GetPluginExtensions()
    {
        try
        {
            return Core.CodecRegistry.GetEncodingExtensions()
                .Where(e => e.IsPlugin)
                .Select(e => (e.Ext, e.EncodePriority))
                .OrderByDescending(e => e.EncodePriority)
                .ThenBy(e => e.Ext, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

}
