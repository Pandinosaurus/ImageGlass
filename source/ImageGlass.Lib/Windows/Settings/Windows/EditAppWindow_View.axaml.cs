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
using ImageGlass.Common.Types;
using ImageGlass.UI;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The content view of <see cref="EditAppWindow"/>: the file-extension, app-name, executable
/// (with a Browse button) and argument fields, plus a live command preview. Owns all field
/// behavior; the hosting window only collects the validated result.
/// </summary>
public partial class EditAppWindowView : PhControl
{
    // ".*" (all extensions), or one/more dot + alphanumeric extensions separated by ";"
    // (e.g. ".jpg;.png"); surrounding/inner whitespace is tolerated and normalized away on submit
    private const string EXT_PATTERN = @"^\s*\.(\*|[A-Za-z0-9]+)(\s*;\s*\.(\*|[A-Za-z0-9]+))*\s*$";


    public EditAppWindowView()
    {
        InitializeComponent();

        // the extension must be ".*" (all) or dot + alphanumeric (".jpg", ".jpg;.png")
        PART_Extension.AcceptValue = TextBoxAcceptValue.RegexPattern;
        PART_Extension.RegexPattern = EXT_PATTERN;

        PART_Executable.TextChanged += (_, _) => UpdateCommandPreview();
        PART_Argument.TextChanged += (_, _) => UpdateCommandPreview();
        PART_Browse.Click += async (_, _) => await BrowseExecutableAsync();
    }


    /// <summary>
    /// Gets the trimmed file-extension key entered by the user (e.g. <c>.jpg;.png</c>).
    /// </summary>
    public string ResultExtKey => PART_Extension.Text?.Trim() ?? string.Empty;


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        PART_Browse.Text = Core.Lang[LangId._Browse];
    }


    /// <summary>
    /// Loads the given app into the fields (defaulting the argument to the <c>&lt;file&gt;</c> macro
    /// for a new app), then refreshes the command preview.
    /// </summary>
    public void LoadData(string? extKey, EditingApp? app)
    {
        PART_Extension.Text = extKey ?? string.Empty;
        PART_AppName.Text = app?.AppName ?? string.Empty;
        PART_Executable.Text = app?.Executable ?? string.Empty;
        PART_Argument.Text = app?.Argument ?? Const.FILE_MACRO;

        UpdateCommandPreview();

        // setting Text above doesn't re-validate yet (handlers attach on load); clear the eager
        // errors raised when the required/regex rules were first applied so the window opens clean
        // (validation re-runs as the user edits and on submit)
        DataValidationErrors.ClearErrors(PART_Extension);
        DataValidationErrors.ClearErrors(PART_AppName);
        DataValidationErrors.ClearErrors(PART_Executable);
    }


    /// <summary>
    /// Validates the required fields (extension, app name, executable) and shows inline errors.
    /// When the extension is valid it is normalized in place (trimmed, lowercased, de-duplicated).
    /// </summary>
    public bool Validate()
    {
        var extOk = PART_Extension.ValidateAndShowError();
        var nameOk = PART_AppName.ValidateAndShowError();
        var exeOk = PART_Executable.ValidateAndShowError();

        if (extOk) PART_Extension.Text = NormalizeExtensions(PART_Extension.Text);

        return extOk & nameOk & exeOk;
    }


    /// <summary>
    /// Normalizes an extension key: trims each segment, lowercases it, drops empties/duplicates,
    /// and rejoins with <c>;</c> (e.g. <c>"  .JPG ;  .svg "</c> → <c>".jpg;.svg"</c>).
    /// </summary>
    private static string NormalizeExtensions(string? raw)
    {
        var segments = (raw ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.ToLowerInvariant())
            .Distinct();

        return string.Join(';', segments);
    }


    /// <summary>
    /// Builds the editing app from the current (trimmed) field values.
    /// </summary>
    public EditingApp BuildApp() => new(
        PART_AppName.Text?.Trim() ?? string.Empty,
        PART_Executable.Text?.Trim() ?? string.Empty,
        PART_Argument.Text?.Trim() ?? string.Empty);


    /// <summary>
    /// Feeds the current executable + argument into the command preview, which renders the
    /// expanded command itself.
    /// </summary>
    private void UpdateCommandPreview()
    {
        PART_CommandPreview.Executable = PART_Executable.Text;
        PART_CommandPreview.Argument = PART_Argument.Text;
    }


    /// <summary>
    /// Opens a file picker to choose the app executable.
    /// </summary>
    private async Task BrowseExecutableAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        PART_Executable.Text = path;
        UpdateCommandPreview();
    }

}
