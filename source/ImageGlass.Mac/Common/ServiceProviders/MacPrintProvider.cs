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
using ImageGlass.Common.Photoing;
using ImageGlass.Common.ServiceProviders;
using ImageGlass.Common.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ImageGlass.Mac.Common.ServiceProviders;

internal class MacPrintProvider : IPrintProvider
{
    private readonly HashSet<string> _nativeMacFormats = [".bmp", ".jpg", ".jpeg", ".png", ".gif", ".tif", ".tiff", ".heic", ".pdf"];


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async Task OpenPrintAsync(string filePath, PhotoMetadata? meta, bool isClipboardFile)
    {
        var fileToPrint = filePath;
        var srcFilePath = meta?.FilePath ?? string.Empty;
        var ext = meta?.FileExtension.ToLowerInvariant() ?? string.Empty;
        var isNativeSingleFrameFormat = meta?.FrameCount == 1 && !_nativeMacFormats.Contains(ext);


        // print clipboard image or convert non-native single-frame formats
        if (Core.ClipboardImage != null || isNativeSingleFrameFormat)
        {
            fileToPrint = await Core.SavePhotoAsTempFileAsync();
        }
        // rename .fax -> .tiff for multi-frame printing
        else if (ext.Equals(".fax", StringComparison.OrdinalIgnoreCase))
        {
            fileToPrint = BHelper.ConfigDir(Dir.Temporary, Path.GetFileNameWithoutExtension(srcFilePath) + ".tiff");
            File.Copy(srcFilePath, fileToPrint, true);
        }
        else if (meta?.FrameCount > 1
            && !ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
        {
            fileToPrint = await Core.SavePhotoAsTempFileAsync();
        }

        if (string.IsNullOrEmpty(fileToPrint)) return;


        // open the macOS system print dialog via Preview's print command
        MacShellProvider.RunAppleScript(
            $"set theFile to POSIX file \"{fileToPrint}\"\n" +
            "tell application \"Preview\"\n" +
            "open theFile\n" +
            "activate\n" +
            "tell application \"System Events\" to keystroke \"p\" using command down\n" +
            "end tell");

    }
}
