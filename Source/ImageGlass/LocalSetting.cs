/*
ImageGlass Project - Image viewer for Windows
Copyright (C) 2019 DUONG DIEU PHAP
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

using ImageGlass.Services.Configuration;
using System.Collections.Generic;
using System.Threading;

namespace ImageGlass
{
    public static class LocalSetting
    {
        private static frmSetting _fSetting;
        private static frmColorPicker _fColorPicker;
        private static Theme.Theme _theme;


        #region Auto Properties
        /// <summary>
        /// Check if frmColorPicker is opening.
        /// This is for toggle Color Picker menu in frmMain
        /// </summary>
        public static bool IsColorPickerToolOpening { get; set; } = false;


        /// <summary>
        /// Check if frmColorPicker is not closed by user (toggle the menu / press ESC on frmColorPicker form). 
        /// This is for auto open Color Picker tool when startup
        /// </summary>
        public static bool IsShowColorPickerOnStartup { get; set; } = false;


        /// <summary>
        /// Gets, sets value if image data was modified
        /// </summary>
        public static string ImageModifiedPath { get; set; } = "";


        /// <summary>
        /// Gets, sets the 0-based index of the last view of Settings dialog tab.
        /// </summary>
        public static int SettingsTabLastView { get; set; } = 0;


        /// <summary>
        /// Gets, sets active value whenever hover on picturebox
        /// </summary>
        public static MainFormForceUpdateAction ForceUpdateActions { get; set; } = MainFormForceUpdateAction.NONE;


        /// <summary>
        /// Gets, sets copied filename collection (multi-copy)
        /// </summary>
        public static List<string> StringClipboard { get; set; } = new List<string>();


        /// <summary>
        /// Gets, sets value indicating that the image we are processing is memory data (clipboard / screenshot,...) or not
        /// </summary>
        public static bool IsTempMemoryData { get; set; } = false;


        /// <summary>
        /// The current "initial" path (file or dir) we're viewing. Used when the user changes the sort settings: we need to rebuild the image list, but otherwise we don't know what image/folder we started with.
        /// 
        /// Here's what happened: I opened a folder with subfolders (child folders enabled). I was going through the images, and decided I wanted to change the sort order. Since the _current_ image was in a sub-folder, after a rescan of the image list, only the _sub_-folders images were re-loaded!
        ///
        /// But if we reload the list using the original image, then the original folder's images, and the sub-folders, are reloaded.
        /// </summary>
        public static string InitialInputPath { get; set; } = "";


        /// <summary>
        /// The 'current' image sorting order. A reconciliation between the user's Settings selection and the sorting order from Windows Explorer, to be used to sort the active image list.
        /// </summary>
        public static ImageOrderBy ActiveImageLoadingOrder { get; set; }


        /// <summary>
        /// The 'current' image sorting direction. A reconciliation between the user's Settings selection and the sorting direction from Windows Explorer, to be used to sort the active image list.
        /// </summary>
        public static ImageOrderType ActiveImageLoadingOrderType { get; set; }


        /// <summary>
        /// Remember for this session the last-used "Save As" extension. When the user is iterating
        /// through a set of images and using "Save As" to always save to the same file type, this
        /// memory prevents them from having to manually re-select their desired extension.
        /// </summary>
        public static int SaveAsFilterIndex { get; set; } = 0;


        /// <summary>
        /// Gets, sets color channel of image
        /// </summary>
        public static ColorChannels Channels { get; set; } = ColorChannels.All;

        #endregion



        #region LazyInitializer Properties
        /// <summary>
        /// Form frmSetting
        /// </summary>
        public static frmSetting FSetting
        {
            get { return LazyInitializer.EnsureInitialized(ref _fSetting); }
            set { _fSetting = value; }
        }

        /// <summary>
        /// Form frmColorPicker
        /// </summary>
        public static frmColorPicker FColorPicker
        {
            get { return LazyInitializer.EnsureInitialized(ref _fColorPicker); }
            set { _fColorPicker = value; }
        }


        /// <summary>
        /// Gets, sets current app theme
        /// </summary>
        public static Theme.Theme Theme
        {
            get => LazyInitializer.EnsureInitialized(ref _theme, () => new Theme.Theme());
            set => _theme = value;
        }


        #endregion


    }
}



