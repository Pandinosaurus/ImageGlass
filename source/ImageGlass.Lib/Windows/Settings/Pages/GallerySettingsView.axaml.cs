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
using ImageGlass.Common.Localization;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The "Gallery" settings page.
/// </summary>
public partial class GallerySettingsView : SettingsPageView
{
    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public GallerySettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public GallerySettingsView(SettingsViewModel vm, string navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
    {
        // Appearance
        BindToggle(PART_ShowGalleryInFullscreen, ConfigId.ShowGalleryInFullscreen,
            LangId.FrmSettings_ShowGalleryInFullscreen, LangId.FrmSettings_Appearance);
        BindToggle(PART_ShowGalleryFileName, ConfigId.ShowGalleryFileName,
            LangId.FrmSettings_ShowGalleryFileName, LangId.FrmSettings_Appearance, true);
        BindToggle(PART_ShellThumbnail, ConfigId.EnableGalleryShellThumbnail,
            LangId.FrmSettings_EnableGalleryShellThumbnail, LangId.FrmSettings_Appearance, true);

        BindUIntSlider(PART_GalleryColumns, ConfigId.GalleryColumns,
            LangId.FrmSettings_GalleryColumns, LangId.FrmSettings_Appearance, 3u, PART_GalleryColumnsLabel);
        BindUIntSlider(PART_ThumbnailSize, ConfigId.ThumbnailSize,
            LangId.FrmSettings_ThumbnailSize, LangId.FrmSettings_Appearance, 70u, PART_ThumbnailSizeLabel);

        BindUIntInput(PART_GalleryCacheSize, ConfigId.GalleryCacheSizeInMb,
            LangId.FrmSettings_GalleryCacheSizeInMb, LangId.FrmSettings_Appearance, 100u);
    }
}
