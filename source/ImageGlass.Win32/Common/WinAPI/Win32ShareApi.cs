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
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using ImageGlass.Common;
using ImageGlass.UI.Windowing;
using System;
using System.Collections.Generic;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ImageGlass.Win32.Common;

public static class Win32ShareApi
{
    // declare datapackage
    private static DataTransferManager? _manager;
    private static readonly List<string> _filePaths = [];


    /// <summary>
    /// Shows window Share dialog.
    /// </summary>
    public static void ShowShare(nint windowHandle, string[] filePaths)
    {
        if (filePaths.Length == 0) return;
        _filePaths.Clear();
        _filePaths.AddRange(filePaths);

        _manager?.DataRequested -= DataTransferManager_DataRequested;
        _manager ??= DataTransferManagerInterop.GetForWindow(windowHandle);

        // set datapackage to dtm
        _manager.DataRequested += DataTransferManager_DataRequested;

        // show window
        DataTransferManagerInterop.ShowShareUIForWindow(windowHandle);
    }


    private static async void DataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs e)
    {
        if (_filePaths.Count == 0) return;
        var deferral = e.Request.GetDeferral();

        // create datapackage
        var dp = e.Request.Data;

        // create List to hold all files to share
        var filesToShare = new List<IStorageItem>();


        // Set properties of shareUI
        dp.Properties.Title = BHelper.AppName;

        try
        {
            if (_filePaths.Count == 1)
            {
                // only 1 photo is being shared
                dp.Properties.Description = _filePaths[0];
            }
            else
            {
                dp.Properties.Description = string.Join("\r\n", _filePaths);
            }

            for (var i = 0; i < _filePaths.Count; i++)
            {
                var imageFile = await StorageFile.GetFileFromPathAsync(_filePaths[i]);
                filesToShare.Add(imageFile);
            }

            dp.SetStorageItems(filesToShare);
        }
        catch (Exception ex)
        {
            await ModalWindow.ShowErrorAsync(null, new ModalWindowOptions
            {
                Title = Core.Lang[ImageGlass.Common.Localization.LangId.Menu_MnuShare],
                Heading = ex.Message,
                Details = ex.ToString(),
            });
        }
        finally
        {
            deferral.Complete();
        }
    }

}
