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
using Avalonia.Layout;
using Avalonia.Media;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Types;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The "Edit" settings page: saving options, clipboard options, and the editing-apps table.
/// Shared binding/registration logic lives in <see cref="SettingsPageView"/>; only the editing-apps
/// table (add/edit/delete via <see cref="EditAppWindow"/>) needs bespoke handling here.
/// </summary>
public partial class EditSettingsView : SettingsPageView
{
    private const int IMAGE_EDIT_QUALITY_MAX = 100;
    private static readonly Thickness CELL_PADDING = new(10, 6);

    // working copy of the editing apps (keyed by file-extension string); staged into the VM on change
    private readonly Dictionary<string, EditingApp> _apps = [];


    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public EditSettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public EditSettingsView(SettingsViewModel vm, string navId, LangId? pageLabel = null) : this()
    {
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
    {
        // Saving
        BindToggle(PART_DeleteConfirmation, ConfigId.EnableDeleteConfirmation,
            LangId.FrmSettings_EnableDeleteConfirmation, LangId.FrmSettings_Edit_Saving, true);
        BindToggle(PART_SaveConfirmation, ConfigId.EnableSaveConfirmation,
            LangId.FrmSettings_EnableSaveConfirmation, LangId.FrmSettings_Edit_Saving, true);
        BindToggle(PART_PreserveModifiedDate, ConfigId.EnablePreserveModifiedDate,
            LangId.FrmSettings_EnablePreserveModifiedDate, LangId.FrmSettings_Edit_Saving);
        BindToggle(PART_OpenSaveAsInCurrentFolder, ConfigId.EnableOpenSaveAsInCurrentFolder,
            LangId.FrmSettings_EnableOpenSaveAsInCurrentFolder, LangId.FrmSettings_Edit_Saving, true);

        BindUIntInput(PART_ImageEditQuality, ConfigId.ImageEditQuality,
            LangId.FrmSettings_ImageEditQuality, LangId.FrmSettings_Edit_Saving, 80u);
        // clamp to the 0–100 range when the user leaves the field
        PART_ImageEditQuality.LostFocus += (_, _) => ClampImageEditQuality();

        // Clipboard
        BindToggle(PART_CopyMultipleFiles, ConfigId.EnableCopyMultipleFiles,
            LangId.FrmSettings_EnableCopyMultipleFiles, LangId.FrmSettings_Clipboard, true);
        BindToggle(PART_CutMultipleFiles, ConfigId.EnableCutMultipleFiles,
            LangId.FrmSettings_EnableCutMultipleFiles, LangId.FrmSettings_Clipboard, true);

        // Image editing apps
        BindEnumDropdown(PART_AfterEditingAction, ConfigId.AfterEditingAction, AfterEditAppAction.Nothing,
            LangId.FrmSettings_AfterEditingAction, LangId.FrmSettings_EditApps);

        BuildEditApps();
    }


    /// <summary>
    /// Clamps the image-quality value to <c>0–100</c> (staging follows from the text change).
    /// </summary>
    private void ClampImageEditQuality()
    {
        if (!uint.TryParse(PART_ImageEditQuality.Text, out var v) || v <= IMAGE_EDIT_QUALITY_MAX) return;
        PART_ImageEditQuality.Text = IMAGE_EDIT_QUALITY_MAX.ToString(CultureInfo.InvariantCulture);
    }


    /// <summary>
    /// Loads the working copy of the editing apps and wires the Add button + table.
    /// </summary>
    private void BuildEditApps()
    {
        // copy the staged/config value so edits don't mutate the live config before commit
        var stored = VM.GetValue(ConfigId.EditApps, new Dictionary<string, EditingApp?>());
        foreach (var (ext, app) in stored)
        {
            if (app is not null) _apps[ext] = app;
        }

        SetLocalizedText(PART_AddApp, LangId._Add);
        PART_AddApp.Click += async (_, _) => await AddOrEditAppAsync(null);

        // rebuild on language change (also performs the initial render)
        AddLangRefresher(RebuildAppsTable);

        Register(PART_AddApp, LangId.FrmSettings_EditApps, ConfigId.EditApps, LangId.FrmSettings_EditApps);
    }


    /// <summary>
    /// Stages the current working copy of editing apps into the view model.
    /// </summary>
    private void StageEditApps()
    {
        var value = _apps.ToDictionary(kv => kv.Key, kv => (EditingApp?)kv.Value);
        VM.SetValue(ConfigId.EditApps, value);
    }


    /// <summary>
    /// Opens <see cref="EditAppWindow"/> to add a new app (when <paramref name="extKey"/> is null)
    /// or edit an existing one, then updates the working copy and re-renders.
    /// </summary>
    private async Task AddOrEditAppAsync(string? extKey)
    {
        var existing = extKey is not null ? _apps.GetValueOrDefault(extKey) : null;
        var window = new EditAppWindow(extKey, existing);

        if (await window.ShowAsync(TopLevel.GetTopLevel(this) as PhWindow) != DialogExitCode.OK) return;
        if (string.IsNullOrEmpty(window.ResultExtKey)) return;

        // editing may rename the extension key → drop the old entry first
        if (extKey is not null) _apps.Remove(extKey);
        _apps[window.ResultExtKey] = window.ResultApp;

        StageEditApps();
        RebuildAppsTable();
    }


    /// <summary>
    /// Removes an app from the working copy and re-renders.
    /// </summary>
    private void DeleteApp(string extKey)
    {
        if (!_apps.Remove(extKey)) return;

        StageEditApps();
        RebuildAppsTable();
    }


    /// <summary>
    /// Rebuilds the editing-apps table from the working copy (header + one row per app,
    /// each with Edit/Delete actions). Shows the empty note when there are no apps.
    /// </summary>
    private void RebuildAppsTable()
    {
        PART_AppsTableBody.Children.Clear();
        PART_AppsTableBody.RowDefinitions.Clear();

        var hasApps = _apps.Count > 0;
        PART_AppsEmpty.IsVisible = !hasApps;
        PART_AppsTable.IsVisible = hasApps;
        if (!hasApps) return;


        // 1. header row + underline spanning all columns
        PART_AppsTableBody.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCell(HeaderCell(LangId._FileExtension), 0, 0);
        AddCell(HeaderCell(LangId.FrmSettings_EditApps_AppName), 0, 1);
        AddCell(HeaderCell(LangId._Executable), 0, 2);
        AddCell(HeaderCell(LangId._Argument), 0, 3);
        AddCell(new TextBlock(), 0, 4); // actions column (no header)
        AddCell(HLine(ResxId.IG_BorderControlBrush, VerticalAlignment.Bottom), 0, 0, 5);


        // 2. data rows (sorted by extension; the ".*" catch-all is forced to the bottom)
        var extKeys = _apps.Keys
            .OrderBy(IsWildcardKey) // false (specific) sorts before true (catch-all)
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var i = 0; i < extKeys.Count; i++)
        {
            var extKey = extKeys[i];
            var app = _apps[extKey];
            var row = i + 1;
            PART_AppsTableBody.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // separator above every row except the first
            if (i > 0) AddCell(HLine(ResxId.IG_BorderNeutralBrush, VerticalAlignment.Top), row, 0, 5);

            var extCell = TextCell(extKey, maxWidth: 160);
            extCell.FontFamily = Const.FONT_CODE;
            AddCell(extCell, row, 0);

            AddCell(TextCell(app.AppName, maxWidth: 160), row, 1);
            AddCell(TextCell(app.Executable), row, 2);
            AddCell(string.IsNullOrEmpty(app.Argument) ? EmptyCell() : TextCell(app.Argument, maxWidth: 180), row, 3);
            AddCell(ActionsCell(extKey), row, 4);
        }
    }


    /// <summary>
    /// Whether an extension key includes the <c>.*</c> catch-all segment.
    /// </summary>
    private static bool IsWildcardKey(string extKey) => extKey
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Contains(EditingApp.ALL_EXTENSIONS);


    #region Table cell builders

    private void AddCell(Control content, int row, int col, int colSpan = 1)
    {
        Grid.SetRow(content, row);
        Grid.SetColumn(content, col);
        if (colSpan > 1) Grid.SetColumnSpan(content, colSpan);
        PART_AppsTableBody.Children.Add(content);
    }


    /// <summary>
    /// Creates a 1px horizontal rule whose color follows the theme (via a dynamic resource binding).
    /// </summary>
    private static Border HLine(ResxId brushId, VerticalAlignment align)
    {
        var line = new Border { Height = 1, VerticalAlignment = align };
        line[!Border.BackgroundProperty] = Resx.CreateBinding(brushId);

        return line;
    }


    private static PhTextBlock HeaderCell(LangId key) => new()
    {
        LangKey = key,
        FontWeight = FontWeight.SemiBold,
        Padding = CELL_PADDING,
        VerticalAlignment = VerticalAlignment.Center,
    };


    private static SelectableTextBlock TextCell(string text, double maxWidth = 0)
    {
        var tb = new SelectableTextBlock
        {
            Text = text,
            Padding = CELL_PADDING,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTabStop = false,
        };
        if (maxWidth > 0) tb.MaxWidth = maxWidth;
        if (!string.IsNullOrEmpty(text)) ToolTip.SetTip(tb, text);

        return tb;
    }


    private static TextBlock EmptyCell() => new()
    {
        Text = Core.Lang[LangId._Empty],
        Padding = CELL_PADDING,
        FontStyle = FontStyle.Italic,
        Opacity = 0.6,
        VerticalAlignment = VerticalAlignment.Center,
    };


    private Border ActionsCell(string extKey)
    {
        var btnEdit = new PhButton { Variant = PhButtonVariant.Link, Text = Core.Lang[LangId._Edit] };
        btnEdit.Click += async (_, _) => await AddOrEditAppAsync(extKey);

        var btnDelete = new PhButton { Variant = PhButtonVariant.Link, Text = Core.Lang[LangId._Delete] };
        btnDelete.Click += (_, _) => DeleteApp(extKey);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.AddRange([btnEdit, btnDelete]);

        return new Border
        {
            Padding = new Thickness(8, 2),
            Child = panel,
        };
    }

    #endregion // Table cell builders

}
