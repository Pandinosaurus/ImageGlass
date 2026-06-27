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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Photoing;
using ImageGlass.Common.ServiceProviders;
using ImageGlass.Common.Types;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

public partial class FileTypeAssociationsSettingsView : SettingsPageView
{
    private const string EXT_ICON_PACKS_URL = "https://imageglass.org/extension-icons";
    private const string DEFAULT_APPS_URI = "ms-settings:defaultapps?registeredAppUser=ImageGlass";

    // floor + bottom inset for fitting the formats table to the page viewport height
    private const double MIN_TABLE_HEIGHT = 220;
    private const double BOTTOM_GAP = 40;

    private static readonly FontFamily _codeFont = new(Const.FONT_CODE);

    // the hosting page's scroll viewer, used to size the table to the remaining viewport height
    private ScrollViewer? _pageScroll;

    // working copy of supported formats (always includes plugin formats); staged into the VM on change
    private readonly HashSet<string> _exts = new(StringComparer.OrdinalIgnoreCase);

    // extensions claimed by codec plugins: always shown and not removable by the user
    private readonly HashSet<string> _pluginExts = new(StringComparer.OrdinalIgnoreCase);

    // codec snapshot (ordered by decode priority, highest first) for the "Codec" column
    private IReadOnlyList<CodecInfo> _codecs = [];

    // current table filter query (matches extension or codec name)
    private string _filter = string.Empty;


    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public FileTypeAssociationsSettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public FileTypeAssociationsSettingsView(SettingsViewModel vm, string navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
    {
        BuildExtensionIcons();
        BuildDefaultPhotoViewer();
        BuildFileFormats();
    }


    #region Fit table to viewport height

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _pageScroll = this.FindAncestorOfType<ScrollViewer>();
        if (_pageScroll is not null) _pageScroll.PropertyChanged += PageScroll_PropertyChanged;

        // bounds are usually 0 at attach; size once layout settles
        Dispatcher.UIThread.Post(UpdateTableMaxHeight, DispatcherPriority.Background);
    }


    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_pageScroll is not null) _pageScroll.PropertyChanged -= PageScroll_PropertyChanged;
        _pageScroll = null;
        base.OnDetachedFromVisualTree(e);
    }


    private void PageScroll_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty) UpdateTableMaxHeight();
    }


    /// <summary>
    /// Caps the formats table at the page viewport's remaining height (below the chrome above it),
    /// never below the floor, so it grows/shrinks with the window and scrolls its rows internally.
    /// </summary>
    private void UpdateTableMaxHeight()
    {
        if (_pageScroll is null) return;

        var viewport = _pageScroll.Bounds.Height;
        if (viewport <= 0) return;

        if (PART_Table.TranslatePoint(new Point(0, 0), _pageScroll) is not { } pt) return;

        var tableTop = pt.Y + _pageScroll.Offset.Y;
        PART_Table.MaxHeight = Math.Max(MIN_TABLE_HEIGHT, viewport - tableTop - BOTTOM_GAP);
    }

    #endregion // Fit table to viewport height


    #region File extension icons

    /// <summary>
    /// Wires the "File extension icons" group (open the icon folder, get icon packs online).
    /// </summary>
    private void BuildExtensionIcons()
    {
        SetLocalizedText(PART_OpenExtIconFolder, LangId.FrmSettings_OpenExtensionIconFolder);
        PART_OpenExtIconFolder.Click += (_, _) =>
            BHelper.OpenFolderPath(BHelper.ConfigDir(Dir.ExtIcons));

        // the description references the open-folder button name via its {0} placeholder
        AddLangRefresher(() => PART_ExtIconsDesc.LangParams = Core.Lang[LangId.FrmSettings_OpenExtensionIconFolder]);

        SetLocalizedText(PART_GetExtIconPacks, LangId.FrmSettings_GetExtensionIconPacks);
        PART_GetExtIconPacks.Click += async (_, _) =>
            await BHelper.OpenUrlAsync(this, EXT_ICON_PACKS_URL, "from_ext_icons");

        RegisterSearchKey(PART_OpenExtIconFolder, LangId.FrmSettings_FileExtensionIcons, null,
            LangId.FrmSettings_FileExtensionIcons);
    }

    #endregion // File extension icons


    #region Default photo viewer

    /// <summary>
    /// Wires the "Default photo viewer" group (make/remove default, the unmanaged-setting warning,
    /// and the shortcut to the Windows Default apps settings).
    /// </summary>
    private void BuildDefaultPhotoViewer()
    {
        SetLocalizedText(PART_MakeDefault, LangId.FrmSettings_MakeDefault);
        AddLangRefresher(() => ToolTip.SetTip(PART_MakeDefault, Core.Lang[LangId.FrmSettings_UnmanagedSettingReminder]));
        PART_MakeDefault.Click += async (_, _) => await AppAPIProvider.IG_SetDefaultPhotoViewerAsync();

        SetLocalizedText(PART_RemoveDefault, LangId.FrmSettings_RemoveDefault);
        PART_RemoveDefault.Click += async (_, _) => await AppAPIProvider.IG_RemoveDefaultPhotoViewerAsync();

        SetLocalizedText(PART_OpenDefaultApps, LangId.FrmSettings_OpenDefaultAppsSetting);
        PART_OpenDefaultApps.Click += async (_, _) => await BHelper.OpenUrlAsync(this, DEFAULT_APPS_URI, "from_default_apps");

        RegisterSearchKey(PART_MakeDefault, LangId.FrmSettings_DefaultPhotoViewer, null, LangId.FrmSettings_DefaultPhotoViewer);
    }

    #endregion // Default photo viewer


    #region File formats

    /// <summary>
    /// Loads the working copy of supported formats (config + plugin formats) and wires
    /// the Add / Reset buttons and the formats table.
    /// </summary>
    private void BuildFileFormats()
    {
        // codec snapshot + plugin-claimed extensions (always shown, not removable)
        _codecs = Core.CodecRegistry.GetCodecInfos();
        foreach (var codec in _codecs)
        {
            if (!codec.IsPlugin) continue;
            foreach (var ext in codec.SupportedExtensions) _pluginExts.Add(ext);
        }

        // copy the staged/config formats so edits don't mutate the live config before commit,
        // then always merge in the plugin formats
        var stored = VM.GetValue(ConfigId.FileFormats,
            new HashSet<string>(Config.DefaultFileFormats, StringComparer.OrdinalIgnoreCase));
        foreach (var ext in stored) _exts.Add(ext);
        foreach (var ext in _pluginExts) _exts.Add(ext);

        PART_Table.MinHeight = MIN_TABLE_HEIGHT;

        SetLocalizedText(PART_AddFormat, LangId._Add);
        PART_AddFormat.Click += async (_, _) => await AddExtensionAsync();

        SetLocalizedText(PART_ResetFormats, LangId._ResetToDefault);
        PART_ResetFormats.Click += (_, _) => ResetFormats();

        // filter rows by extension or codec name
        AddLangRefresher(() => PART_Search.PlaceholderText = Core.Lang[LangId._TypeToFilter]);
        PART_Search.TextChanged += (_, _) =>
        {
            _filter = PART_Search.Text ?? string.Empty;
            RebuildTable();
        };

        // rebuild on language change (also performs the initial render)
        AddLangRefresher(RebuildTable);

        RegisterSearchKey(PART_AddFormat, LangId.FrmSettings_FileFormats, ConfigId.FileFormats,
            LangId.FrmSettings_FileFormats);
    }


    /// <summary>
    /// Stages the current working copy of formats into the view model.
    /// </summary>
    private void StageFormats() => VM.SetValue(ConfigId.FileFormats, new HashSet<string>(_exts, StringComparer.OrdinalIgnoreCase));


    /// <summary>
    /// Shows an input dialog to add a new extension, then scrolls to + flashes its row.
    /// A duplicate isn't added; the existing row is flashed instead.
    /// </summary>
    private async Task AddExtensionAsync()
    {
        var win = TopLevel.GetTopLevel(this) as PhWindow;
        var result = await ModalWindow.ShowInputAsync(win, new ModalWindowOptions
        {
            Title = Core.Lang[LangId.FrmSettings_AddNewFileExtension],
            Description = Core.Lang[LangId._FileExtension],
            InputPlaceholder = ".jpg",
            AcceptValue = TextBoxAcceptValue.FileExtensionValueOnly,
        });
        if (result.ExitCode != DialogExitCode.OK) return;

        // normalize to a lowercase value with a leading dot (e.g. "PSD" -> ".psd")
        var raw = result.InputValue?.Trim().ToLowerInvariant() ?? string.Empty;
        if (raw.Length == 0) return;
        var ext = raw.StartsWith('.') ? raw : "." + raw;

        if (_exts.Add(ext))
        {
            StageFormats();
            RebuildTable();
        }

        // scroll to + flash the row (newly added or the existing duplicate)
        PART_Table.FlashRow(ext);
    }


    /// <summary>
    /// Removes a (non-plugin) extension from the working copy and re-renders.
    /// </summary>
    private void DeleteExtension(string ext)
    {
        if (_pluginExts.Contains(ext)) return;
        if (!_exts.Remove(ext)) return;

        StageFormats();
        RebuildTable();
    }


    /// <summary>
    /// Resets the formats to the built-in defaults, always keeping the plugin formats.
    /// </summary>
    private void ResetFormats()
    {
        _exts.Clear();
        foreach (var ext in Config.DefaultFileFormats) _exts.Add(ext);
        foreach (var ext in _pluginExts) _exts.Add(ext);

        StageFormats();
        RebuildTable();
    }


    /// <summary>
    /// Rebuilds the formats table (order number, extension, codec + a Delete action for non-plugin
    /// formats), sorted by extension, and updates the total count.
    /// </summary>
    private void RebuildTable()
    {
        PhTableColumn[] columns =
        [
            new() { Header = string.Empty },
            new() { Header = Core.Lang[LangId._FileExtension], MinWidth = 160 },
            new() { Header = Core.Lang[LangId._Codec], Star = true },
        ];

        // filtered (by extension or codec name) + sorted by extension ascending
        var q = _filter.Trim();
        var sorted = _exts
            .Where(e => q.Length == 0
                || e.Contains(q, StringComparison.OrdinalIgnoreCase)
                || CodecNameFor(e).Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<PhTableRow>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var ext = sorted[i];
            var key = ext; // capture for the action closure

            // plugin-claimed formats can't be removed -> no Delete action
            PhTableAction[] actions = _pluginExts.Contains(ext) ? [] : [
                new() {
                    Icon = ResxIconId.IconClose,
                    Tooltip = Core.Lang[LangId._Delete],
                    Click = () => DeleteExtension(key),
                }
            ];

            rows.Add(new PhTableRow
            {
                Key = ext,
                Cells =
                [
                    PhTableControl.TextCell((i + 1).ToString()),
                    PhTableControl.TextCell(ext, selectable: true, font: _codeFont),
                    PhTableControl.TextCell(CodecNameFor(ext)),
                ],
                Actions = actions,
            });
        }

        PART_Table.EmptyText = Core.Lang[LangId._Empty];
        PART_Table.Build(columns, rows);

        PART_TotalFormats.LangParams = _exts.Count;
    }


    /// <summary>
    /// Returns the friendly name of the codec that would decode the given extension: the
    /// highest-priority codec that claims it, falling back to the catch-all codec (Magick.NET).
    /// </summary>
    private string CodecNameFor(string ext)
    {
        // _codecs is ordered by decode priority (highest first)
        foreach (var codec in _codecs)
        {
            if (codec.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return codec.CodecName;
        }

        // not explicitly claimed -> the catch-all codec (empty extension list)
        return _codecs.FirstOrDefault(c => c.SupportedExtensions.Count == 0)?.CodecName ?? string.Empty;
    }

    #endregion // File formats

}
