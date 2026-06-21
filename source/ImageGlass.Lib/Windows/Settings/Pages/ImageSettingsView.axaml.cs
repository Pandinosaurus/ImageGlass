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
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Photoing;
using ImageGlass.Common.Types;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The "Image" settings page: image browsing/order, preview &amp; embedded thumbnails,
/// Image Booster cache, and color management. Shared binding/registration logic lives in
/// <see cref="SettingsPageView"/>; only the color-profile widget (custom file path + Browse)
/// needs bespoke handling here.
/// </summary>
public partial class ImageSettingsView : SettingsPageView
{
    // current custom .icc/.icm profile path (used when the dropdown is on "Custom")
    private string _customProfilePath = string.Empty;


    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public ImageSettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public ImageSettingsView(SettingsViewModel vm, string navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
    {
        // Browsing
        BindEnumDropdown(PART_OrderBy, ConfigId.ImageLoadingOrder, ImageOrderBy.Name,
            LangId.FrmSettings_ImageLoadingOrder, LangId.FrmSettings_Browsing);
        BindEnumDropdown(PART_OrderType, ConfigId.ImageLoadingOrderType, ImageOrderType.Asc,
            LangId.FrmSettings_ImageLoadingOrder, LangId.FrmSettings_Browsing);

        BindToggle(PART_ExplorerSortOrder, ConfigId.EnableExplorerSortOrder,
            LangId.FrmSettings_EnableExplorerSortOrder, LangId.FrmSettings_Browsing, true);
        BindToggle(PART_SubfoldersLoading, ConfigId.EnableSubfoldersLoading,
            LangId.FrmSettings_EnableSubfoldersLoading, LangId.FrmSettings_Browsing);
        BindToggle(PART_FolderGrouping, ConfigId.EnableImageFolderGrouping,
            LangId.FrmSettings_EnableImageFolderGrouping, LangId.FrmSettings_Browsing);
        BindToggle(PART_HiddenImages, ConfigId.EnableHiddenImagesLoading,
            LangId.FrmSettings_EnableHiddenImagesLoading, LangId.FrmSettings_Browsing);
        BindToggle(PART_LoopBack, ConfigId.EnableLoopBackNavigation,
            LangId.FrmSettings_EnableLoopBackNavigation, LangId.FrmSettings_Browsing, true);

        // Image preview
        BindToggle(PART_ImagePreview, ConfigId.EnableImagePreview,
            LangId.FrmSettings_EnableImagePreview, LangId.FrmSettings_ImagePreview, true);
        BindToggle(PART_OnlyRawPreview, ConfigId.EnableOnlyLoadRawPreview,
            LangId.FrmSettings_EnableOnlyLoadRawPreview, LangId.FrmSettings_ImagePreview);
        BindToggle(PART_OnlyNonRawPreview, ConfigId.EnableOnlyLoadNonRawPreview,
            LangId.FrmSettings_EnableOnlyLoadNonRawPreview, LangId.FrmSettings_ImagePreview);

        // the minimum-size inputs only matter when an embedded-thumbnail option is on
        PART_OnlyRawPreview.IsCheckedChanged += (_, _) => UpdatePreviewSizeVisibility();
        PART_OnlyNonRawPreview.IsCheckedChanged += (_, _) => UpdatePreviewSizeVisibility();
        UpdatePreviewSizeVisibility();

        BindIntInput(PART_PreviewMinWidth, ConfigId.PreviewMinWidth,
            LangId.FrmSettings_MinEmbeddedThumbnailSize, LangId.FrmSettings_ImagePreview);
        BindIntInput(PART_PreviewMinHeight, ConfigId.PreviewMinHeight,
            LangId.FrmSettings_MinEmbeddedThumbnailSize, LangId.FrmSettings_ImagePreview);

        // File watcher
        BindToggle(PART_FileWatcher, ConfigId.EnableFileWatcher,
            LangId.FrmSettings_EnableFileWatcher, LangId.FrmSettings_FileWatcher, true);
        BindToggle(PART_AutoOpenNewAddedImage, ConfigId.EnableAutoOpenNewAddedImage,
            LangId.FrmSettings_EnableAutoOpenNewAddedImage, LangId.FrmSettings_FileWatcher);

        // Color management
        BindToggle(PART_AlwaysApplyColorProfile, ConfigId.EnableAlwaysApplyColorProfile,
            LangId.FrmSettings_EnableAlwaysApplyColorProfile, LangId.FrmSettings_ColorManagement);
        BuildColorProfile();

        // Caching
        BindUIntInput(PART_CacheMaxMemory, ConfigId.CacheMaxMemoryInMb,
            LangId.FrmSettings_ImageBoosterCacheMaxMemoryInMb, LangId.FrmSettings_Caching);
        BindUIntInput(PART_CacheMaxDimension, ConfigId.CacheMaxDimension,
            LangId.FrmSettings_ImageBoosterCacheMaxDimension, LangId.FrmSettings_Caching, 8_000u);
        BindDoubleInput(PART_CacheMaxFileSize, ConfigId.CacheMaxFileSizeInMb,
            LangId.FrmSettings_ImageBoosterCacheMaxFileSizeInMb, LangId.FrmSettings_Caching, 100d);
    }


    /// <summary>
    /// Toggles the visibility of the minimum embedded-thumbnail size inputs: shown only
    /// when at least one "load only embedded thumbnail" option is enabled.
    /// </summary>
    private void UpdatePreviewSizeVisibility()
    {
        PART_PreviewSizeSection.IsVisible =
            (PART_OnlyRawPreview.IsChecked ?? false) || (PART_OnlyNonRawPreview.IsChecked ?? false);
    }


    /// <summary>
    /// Builds the color-profile dropdown (the <see cref="ColorProfileOption"/> values) plus the
    /// Browse button + custom-file link. The <c>ColorProfile</c> config stores either an enum
    /// name or a custom file path (a value containing a '.').
    /// </summary>
    private void BuildColorProfile()
    {
        var current = VM.GetValue(ConfigId.ColorProfile, nameof(ColorProfileOption.CurrentMonitorProfile));
        var isCustomPath = current.Contains('.', StringComparison.Ordinal);
        if (isCustomPath)
        {
            _customProfilePath = current;
            PART_CustomColorProfile.Text = current;
            ToolTip.SetTip(PART_CustomColorProfile, current);
        }

        // populate options from the enum; localized labels where a key exists, else the raw name
        var names = Enum.GetNames<ColorProfileOption>();
        var selectedIndex = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var item = new ComboBoxItem { Tag = name };

            BindComboItemText(item, Lang.GetKey($"{nameof(ColorProfileOption)}_{name}"), name);
            PART_ColorProfile.Items.Add(item);

            var match = isCustomPath
                ? name == nameof(ColorProfileOption.Custom)
                : name == current;
            if (match) selectedIndex = i;
        }
        PART_ColorProfile.SelectedIndex = selectedIndex;

        PART_ColorProfile.SelectionChanged += (_, _) =>
        {
            StageColorProfile();
            UpdateColorProfileVisibility();
        };

        SetLocalizedText(PART_BrowseColorProfile, LangId._Browse);
        PART_BrowseColorProfile.Click += async (_, _) => await BrowseColorProfileAsync();
        PART_CustomColorProfile.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_customProfilePath)) BHelper.OpenFilePath(_customProfilePath);
        };

        UpdateColorProfileVisibility();

        Register(PART_ColorProfile, LangId.FrmSettings_ColorProfile,
            ConfigId.ColorProfile, LangId.FrmSettings_ColorManagement);
    }


    /// <summary>
    /// Stages the color-profile value: the custom file path when "Custom" is selected
    /// (else the chosen enum name).
    /// </summary>
    private void StageColorProfile()
    {
        var selected = SelectedColorProfileName();
        if (selected == nameof(ColorProfileOption.Custom))
        {
            VM.SetValue(ConfigId.ColorProfile,
                string.IsNullOrEmpty(_customProfilePath) ? selected : _customProfilePath);
        }
        else
        {
            VM.SetValue(ConfigId.ColorProfile, selected);
        }
    }


    /// <summary>
    /// Shows the Browse button + custom-file link only for "Custom", and the monitor-profile
    /// note only for "CurrentMonitorProfile".
    /// </summary>
    private void UpdateColorProfileVisibility()
    {
        var selected = SelectedColorProfileName();
        var isCustom = selected == nameof(ColorProfileOption.Custom);

        PART_BrowseColorProfile.IsVisible = isCustom;
        PART_CustomColorProfile.IsVisible = isCustom && !string.IsNullOrEmpty(_customProfilePath);
        PART_MonitorProfileNote.IsVisible = selected == nameof(ColorProfileOption.CurrentMonitorProfile);
    }


    private string SelectedColorProfileName()
    {
        return PART_ColorProfile.SelectedItem is ComboBoxItem { Tag: string name }
            ? name
            : nameof(ColorProfileOption.CurrentMonitorProfile);
    }


    /// <summary>
    /// Opens a file picker for an .icc/.icm color profile and stages the chosen path.
    /// </summary>
    private async Task BrowseColorProfileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ICC/ICM") { Patterns = ["*.icc", "*.icm"] },
                FilePickerFileTypes.All,
            ],
        });

        var path = (files.Count > 0 ? files[0] : null)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        _customProfilePath = path;
        PART_CustomColorProfile.Text = path;
        ToolTip.SetTip(PART_CustomColorProfile, path);

        StageColorProfile();
        UpdateColorProfileVisibility();
    }

}
