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
using Avalonia.Controls.Primitives;
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

    protected override int MIN_WIDTH => 500;
    protected override int MAX_WIDTH => 500;


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
        // "Update" button opens the update URL, falling back to the changelog URL
        var release = _result?.Release;
        var url = !string.IsNullOrWhiteSpace(release?.UpdateUrl)
            ? release.UpdateUrl
            : release?.ChangelogUrl;
        if (!string.IsNullOrWhiteSpace(url))
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
            Text = Core.Lang[LangId.Menu_MnuCheckForUpdate_SkipVersion],
            Variant = PhButtonVariant.Link,
        };
        btnSkip.Click += (_, _) =>
        {
            var version = _result?.Release?.Version;
            if (!string.IsNullOrWhiteSpace(version))
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
    /// Creates the footer left content with a "Learn more" link that opens the changelog.
    /// </summary>
    private PhButton CreateChangelogButton()
    {
        var btn = new PhButton
        {
            Text = Core.Lang[LangId._LearnMore],
            Variant = PhButtonVariant.Link,
        };
        btn.Click += (_, _) => OpenChangeLog();

        return btn;
    }


    /// <summary>
    /// Opens release changelog url.
    /// </summary>
    private void OpenChangeLog()
    {
        var url = _result?.Release?.ChangelogUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            _ = BHelper.OpenUrlAsync(this, url, "from_update_dialog");
        }
    }


    /// <summary>
    /// Creates the latest-release info card: title, version, published date, and release notes.
    /// </summary>
    private Border CreateReleaseCard(UpdateReleaseInfo release)
    {
        // primary title (fall back to the version label when the release has no title)
        var lnkTitle = new PhButton
        {
            Variant = PhButtonVariant.Link,
            Text = !string.IsNullOrWhiteSpace(release.Title)
                ? release.Title
                : Core.Lang[LangId.Menu_MnuCheckForUpdate_LatestVersion, release.Version],
            FontSize = Const.FONT_SIZE_SUBTITLE,
            FontWeight = FontWeight.SemiBold,
        };
        lnkTitle.Click += (_, _) => OpenChangeLog();

        // muted metadata: version, then published date
        var metaPanel = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 5, 0, 0),
        };
        metaPanel.Children.Add(new SelectableTextBlock
        {
            Text = Core.Lang[LangId.Menu_MnuCheckForUpdate_LatestVersion, release.Version],
            Opacity = 0.75,
        });
        if (!string.IsNullOrWhiteSpace(release.PublishedDate))
        {
            metaPanel.Children.Add(new SelectableTextBlock
            {
                Text = Core.Lang[LangId.Menu_MnuCheckForUpdate_PublishedDate, release.PublishedDate],
                Opacity = 0.75,
            });
        }

        var content = new StackPanel();
        content.Children.Add(lnkTitle);
        content.Children.Add(metaPanel);

        // release notes: selectable + scrollable, separated from the header
        if (!string.IsNullOrWhiteSpace(release.Description))
        {
            content.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 14),
                [!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.TextControlBorderBrush),

            });
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 200,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new SelectableTextBlock
                {
                    Text = release.Description,
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }

        return new Border
        {
            Padding = new Thickness(14, 8),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            [!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius),
            [!Border.BorderBrushProperty] = Resx.CreateBinding(ResxId.IG_BorderNeutralBrush),
            [!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BackgroundNeutralBrush),
            Child = content,
        };
    }

    #endregion // Private Methods



    #region Public Methods

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

        // shared defaults: a single [Close] button, no extra content
        Note = null;
        ModalExtraContent = null!;
        DialogFooterLeftContent = null!;
        IsButton1Visible = false;
        IsButton3Visible = false;
        IsButton2Visible = true;
        Button2Text = Core.Lang[LangId._Close];
        DefaultButton = DialogButton.Button2;
        DefaultFocus = DialogFocus.Button2;

        var release = result.Release;

        if (result.Status == UpdateCheckStatus.UpdateAvailable && release is not null)
        {
            Heading = Core.Lang[LangId.Menu_MnuCheckForUpdate_NewVersion];
            Description = Core.Lang[LangId.Menu_MnuCheckForUpdate_CurrentVersion, Core.BuildInfo.AppVersion];
            ModalExtraContent = CreateReleaseCard(release);

            // "Skip this version" link + [Update] [Close]
            DialogFooterLeftContent = CreateSkipButton();
            Button1Text = Core.Lang[LangId._Update];
            IsButton1Visible = true;
            DefaultButton = DialogButton.Button1;
            DefaultFocus = DialogFocus.Button1;
        }
        else if (result.Status == UpdateCheckStatus.CheckFailed)
        {
            Heading = Core.Lang[LangId.Menu_MnuCheckForUpdate_Failed];
            Description = result.ErrorMessage;
        }
        else
        {
            // NoUpdate: always show the latest release info when we have it
            Heading = Core.Lang[LangId.Menu_MnuCheckForUpdate_NoUpdate];
            Description = Core.Lang[LangId.Menu_MnuCheckForUpdate_CurrentVersion, Core.BuildInfo.AppVersion];

            if (release is not null)
            {
                ModalExtraContent = CreateReleaseCard(release);

                // offer the changelog even when already up-to-date
                if (!string.IsNullOrWhiteSpace(release.ChangelogUrl))
                {
                    DialogFooterLeftContent = CreateChangelogButton();
                }
            }
        }
    }

    #endregion // Public Methods


}
