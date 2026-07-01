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
using ImageGlass.Common;
using ImageGlass.Common.Localization;
using ImageGlass.Common.ServiceProviders.Update;
using ImageGlass.Common.Types;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;

namespace ImageGlass.Windows;

public partial class UpdateWindow : ModalWindow
{
    private UpdateCheckResult? _result;


    /// <summary>
    /// Gets whether the user chose to skip this version.
    /// </summary>
    public bool IsSkipped { get; private set; }


    public UpdateWindow()
    {
        ShowInTaskbar = true;
        Title = Core.Lang[LangId._CheckForUpdate];
        Description = Core.Lang[LangId.Menu_MnuCheckForUpdate_CurrentVersion, Core.BuildInfo.AppVersion];
    }


    #region Override Methods

    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        // "Get Update" button opens changelog URL
        var url = _result?.Release?.ChangelogUrl;
        if (!string.IsNullOrEmpty(url))
        {
            _ = BHelper.OpenUrlAsync(this, url, "from_update_dialog");
        }
    }

    #endregion // Override Methods



    #region Private Methods

    /// <summary>
    /// Creates the footer left content with "Skip this version" link button.
    /// </summary>
    private PhButton CreateSkipButton()
    {
        var btnSkip = new PhButton
        {
            Content = Core.Lang[LangId.Menu_MnuCheckForUpdate_SkipVersion],
            Padding = new Thickness(6, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            [!PhButton.ForegroundProperty] = Resx.CreateBinding(ResxId.IG_TextAccentColor),
        };
        btnSkip.Click += (_, _) =>
        {
            var version = _result?.Release?.Version;
            if (!string.IsNullOrEmpty(version))
            {
                Core.Config.UpdateSkippedVersion = version;
                IsSkipped = true;
            }

            DialogResult = DialogExitCode.Cancel;
            Close();
        };

        return btnSkip;
    }


    /// <summary>
    /// Creates the <see cref="ModalExtraContent"/> panel for displaying update details.
    /// </summary>
    private static Border CreateUpdateAvailableContent(UpdateReleaseInfo release)
    {
        // header: version + date
        var lblVersion = new TextBlock
        {
            Text = Core.Lang[LangId.Menu_MnuCheckForUpdate_LatestVersion, release.Version],
            FontWeight = FontWeight.SemiBold,
        };
        var lblDate = new TextBlock
        {
            Text = Core.Lang[LangId.Menu_MnuCheckForUpdate_PublishedDate, release.PublishedDate],
            FontSize = Const.FONT_SIZE_SMALL,
            Opacity = 0.55,
        };

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8),
            [!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BorderNeutralBrush),
        };

        // release title
        var lblTitle = new TextBlock
        {
            Text = release.Title,
            FontSize = Const.FONT_SIZE_SUBTITLE,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };

        // release description — scrollable
        var releaseNotes = new ScrollViewer
        {
            MaxHeight = 200,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = release.Description,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Opacity = 0.8,
            },
        };

        var content = new StackPanel { Spacing = 2 };
        content.Children.AddRange([lblVersion, lblDate, separator, lblTitle, releaseNotes]);

        return new Border
        {
            Padding = new Thickness(12),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            [!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius),
            [!Border.BorderBrushProperty] = Resx.CreateBinding(ResxId.IG_BorderNeutralBrush),
            Child = content,
        };
    }

    #endregion // Private Methods



    /// <summary>
    /// Configures the window to show "Checking for update..." with an indeterminate progress bar.
    /// </summary>
    public void SetCheckingState()
    {
        _result = null;
        Heading = Core.Lang[LangId.Menu_MnuCheckForUpdate_Checking];

        IsProgressVisible = true;
        IsProgressIndeterminate = true;

        IsButton1Visible = false;
        IsButton2Visible = true;
        IsButton3Visible = false;
        Button2Text = Core.Lang[LangId._Close];
        DefaultFocus = DialogFocus.Button2;

        DialogFooterLeftContent = null!;
        ModalExtraContent = null!;
    }


    /// <summary>
    /// Transitions the window to display the update check result.
    /// </summary>
    public void SetResultState(UpdateCheckResult result)
    {
        _result = result;

        IsProgressVisible = false;
        IsProgressIndeterminate = false;

        if (result.Status == UpdateCheckStatus.UpdateAvailable)
        {
            var release = result.Release!;

            Heading = Core.Lang[LangId.Menu_MnuCheckForUpdate_NewVersion];
            Note = null;
            ModalExtraContent = CreateUpdateAvailableContent(release);

            // Footer left: "Skip this version"
            DialogFooterLeftContent = CreateSkipButton();

            // Button1 = Get Update, Button2 = Close
            Button1Text = Core.Lang[LangId._Update];
            IsButton1Visible = true;
            Button2Text = Core.Lang[LangId._Close];
            IsButton2Visible = true;
            IsButton3Visible = false;

            DefaultButton = DialogButton.Button1;
            DefaultFocus = DialogFocus.Button1;
        }
        else if (result.Status == UpdateCheckStatus.CheckFailed)
        {
            Heading = Core.Lang[LangId.Menu_MnuCheckForUpdate_Failed];
            Description = result.ErrorMessage;
            Note = null;

            IsButton1Visible = false;
            IsButton2Visible = true;
            IsButton3Visible = false;
            Button2Text = Core.Lang[LangId._Close];

            DialogFooterLeftContent = null!;
            ModalExtraContent = null!;
        }
        else
        {
            // NoUpdate
            Heading = Core.Lang[LangId.Menu_MnuCheckForUpdate_NoUpdate];
            Note = null;

            IsButton1Visible = false;
            IsButton2Visible = true;
            IsButton3Visible = false;
            Button2Text = Core.Lang[LangId._Close];

            DialogFooterLeftContent = null!;
            ModalExtraContent = null!;
        }
    }


}
