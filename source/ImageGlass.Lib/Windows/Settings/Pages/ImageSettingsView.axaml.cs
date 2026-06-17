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
using ImageGlass.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ImageGlass.Common.Windows;

/// <summary>
/// XAML UI for the "Image" settings page (image loading, preview, Image Booster cache,
/// color management). Wires its controls to the staging <see cref="SettingsViewModel"/>
/// and registers each row into the search index.
/// </summary>
public partial class ImageSettingsView : PhControl
{
    private readonly SettingsViewModel _vm = null!;
    private readonly string _navId = string.Empty;
    private readonly LangId? _pageLabel;

    // combo items whose Content text must refresh on language change (enum dropdowns)
    private readonly Dictionary<ComboBoxItem, LangId> _comboItemLabels = [];

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
        _vm = vm;
        _navId = navId;
        _pageLabel = pageLabel;
        Build();
    }



    #region Override Methods

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        // PhTextBlock labels refresh themselves; refresh the enum combo items + Browse button manually
        foreach (var (item, label) in _comboItemLabels)
        {
            item.Content = Core.Lang[label];
        }
        PART_BrowseColorProfile.Text = Core.Lang[LangId._Browse];
    }

    #endregion // Override Methods



    #region Methods

    private void Build()
    {
        // Image loading
        BindEnumDropdown(PART_OrderBy, ConfigId.ImageLoadingOrder, ImageOrderBy.Name,
            LangId.FrmSettings_ImageLoadingOrder, LangId.FrmSettings_ImageBrowsing);
        BindEnumDropdown(PART_OrderType, ConfigId.ImageLoadingOrderType, ImageOrderType.Asc,
            LangId.FrmSettings_ImageLoadingOrder, LangId.FrmSettings_ImageBrowsing);

        BindToggle(PART_ExplorerSortOrder, ConfigId.EnableExplorerSortOrder,
            LangId.FrmSettings_EnableExplorerSortOrder, LangId.FrmSettings_ImageBrowsing, true);
        BindToggle(PART_SubfoldersLoading, ConfigId.EnableSubfoldersLoading,
            LangId.FrmSettings_EnableSubfoldersLoading, LangId.FrmSettings_ImageBrowsing);
        BindToggle(PART_FolderGrouping, ConfigId.EnableImageFolderGrouping,
            LangId.FrmSettings_EnableImageFolderGrouping, LangId.FrmSettings_ImageBrowsing);
        BindToggle(PART_HiddenImages, ConfigId.EnableHiddenImagesLoading,
            LangId.FrmSettings_EnableHiddenImagesLoading, LangId.FrmSettings_ImageBrowsing);
        BindToggle(PART_LoopBack, ConfigId.EnableLoopBackNavigation,
            LangId.FrmSettings_EnableLoopBackNavigation, LangId.FrmSettings_ImageBrowsing, true);

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

        // Image Booster
        BindUIntInput(PART_CacheMaxMemory, ConfigId.CacheMaxMemoryInMb, 0u,
            LangId.FrmSettings_ImageBoosterCacheMaxMemoryInMb, LangId.FrmSettings_ImageBooster);
        BindUIntInput(PART_CacheMaxDimension, ConfigId.CacheMaxDimension, 8_000u,
            LangId.FrmSettings_ImageBoosterCacheMaxDimension, LangId.FrmSettings_ImageBooster);
        BindDoubleInput(PART_CacheMaxFileSize, ConfigId.CacheMaxFileSizeInMb, 100d,
            LangId.FrmSettings_ImageBoosterCacheMaxFileSizeInMb, LangId.FrmSettings_ImageBooster);

        // Color management
        BindToggle(PART_AlwaysApplyColorProfile, ConfigId.EnableAlwaysApplyColorProfile,
            LangId.FrmSettings_EnableAlwaysApplyColorProfile, LangId.FrmSettings_ColorManagement);
        BuildColorProfile();
    }


    /// <summary>
    /// Toggles the visibility of the minimum embedded-thumbnail size inputs: shown only
    /// when at least one "load only embedded thumbnail" option is enabled (matches v9).
    /// </summary>
    private void UpdatePreviewSizeVisibility()
    {
        PART_PreviewSizeSection.IsVisible =
            (PART_OnlyRawPreview.IsChecked ?? false) || (PART_OnlyNonRawPreview.IsChecked ?? false);
    }


    /// <summary>
    /// Binds a checkbox to a boolean config id (staged on change).
    /// </summary>
    private void BindToggle(CheckBox chk, ConfigId id, LangId label, LangId? section, bool defaultValue = false)
    {
        chk.IsChecked = _vm.GetValue(id, defaultValue);
        chk.IsCheckedChanged += (_, _) => _vm.SetValue(id, chk.IsChecked ?? false);

        Register(chk, label, id, section);
    }


    /// <summary>
    /// Binds a text box to an unsigned-integer config id (staged on valid change).
    /// </summary>
    private void BindUIntInput(PhTextBox box, ConfigId id, uint defaultValue, LangId label, LangId? section)
    {
        box.Text = _vm.GetValue(id, defaultValue).ToString(CultureInfo.InvariantCulture);
        box.TextChanged += (_, _) =>
        {
            if (uint.TryParse(box.Text, out var v)) _vm.SetValue(id, v);
        };

        Register(box, label, id, section);
    }


    /// <summary>
    /// Binds a text box to an integer config id (staged on valid change).
    /// </summary>
    private void BindIntInput(PhTextBox box, ConfigId id, LangId label, LangId? section)
    {
        box.Text = _vm.GetValue(id, 0).ToString(CultureInfo.InvariantCulture);
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var v)) _vm.SetValue(id, v);
        };

        Register(box, label, id, section);
    }


    /// <summary>
    /// Binds a text box to a double config id (staged on valid change).
    /// </summary>
    private void BindDoubleInput(PhTextBox box, ConfigId id, double defaultValue, LangId label, LangId? section)
    {
        box.Text = _vm.GetValue(id, defaultValue).ToString(CultureInfo.InvariantCulture);
        box.TextChanged += (_, _) =>
        {
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                _vm.SetValue(id, v);
        };

        Register(box, label, id, section);
    }


    /// <summary>
    /// Populates an enum dropdown with localized labels and binds the selection to a config id.
    /// Option display text comes from the <c>{EnumType}_{Value}</c> language key.
    /// </summary>
    private void BindEnumDropdown<TEnum>(ComboBox combo, ConfigId id, TEnum defaultValue,
        LangId label, LangId? section) where TEnum : struct, Enum
    {
        var current = _vm.GetValue(id, defaultValue);
        var selectedIndex = 0;

        var names = Enum.GetNames<TEnum>();
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var value = Enum.Parse<TEnum>(name);
            var itemLabel = Lang.GetKey($"{typeof(TEnum).Name}_{name}");

            var item = new ComboBoxItem
            {
                Content = itemLabel is { } lk ? Core.Lang[lk] : name,
                Tag = value,
            };
            if (itemLabel is { } key) _comboItemLabels[item] = key;

            combo.Items.Add(item);
            if (EqualityComparer<TEnum>.Default.Equals(value, current)) selectedIndex = i;
        }
        combo.SelectedIndex = selectedIndex;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: TEnum value }) _vm.SetValue(id, value);
        };

        Register(combo, label, id, section);
    }


    /// <summary>
    /// Builds the color-profile dropdown (the <see cref="ColorProfileOption"/> values) plus the
    /// Browse button + custom-file link. The <c>ColorProfile</c> config stores either an enum
    /// name or a custom file path (a value containing a '.').
    /// </summary>
    private void BuildColorProfile()
    {
        var current = _vm.GetValue(ConfigId.ColorProfile, nameof(ColorProfileOption.CurrentMonitorProfile));
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
            var itemLabel = Lang.GetKey($"{nameof(ColorProfileOption)}_{name}");

            var item = new ComboBoxItem
            {
                Content = itemLabel is { } lk ? Core.Lang[lk] : name,
                Tag = name,
            };
            if (itemLabel is { } key) _comboItemLabels[item] = key;
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

        PART_BrowseColorProfile.Text = Core.Lang[LangId._Browse];
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
            _vm.SetValue(ConfigId.ColorProfile,
                string.IsNullOrEmpty(_customProfilePath) ? selected : _customProfilePath);
        }
        else
        {
            _vm.SetValue(ConfigId.ColorProfile, selected);
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
    private async System.Threading.Tasks.Task BrowseColorProfileAsync()
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

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        _customProfilePath = path;
        PART_CustomColorProfile.Text = path;
        ToolTip.SetTip(PART_CustomColorProfile, path);

        StageColorProfile();
        UpdateColorProfileVisibility();
    }


    private void Register(Control target, LangId label, ConfigId? id, LangId? section)
    {
        _vm.Index.Register(new SettingItem
        {
            Id = id,
            Label = label,
            PageNavId = _navId,
            Page = _pageLabel,
            Section = section,
            Target = target,
        });
    }

    #endregion // Methods

}
