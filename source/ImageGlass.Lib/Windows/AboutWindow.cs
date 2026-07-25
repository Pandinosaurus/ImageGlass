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
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using ImageGlass.Common.AppThemes;
using ImageGlass.Common.Localization;
using ImageGlass.Common.ServiceProviders;
using ImageGlass.Common.Types;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using ImageMagick;
using System;
using System.Runtime.InteropServices;

namespace ImageGlass.Common.Windows;

public partial class AboutWindow : DialogWindow
{
    private Image _imgLogo = null!;
    private TextBlock _lblSlogan = null!;
    private SelectableTextBlock _lblVersion = null!;
    private SelectableTextBlock _lblCopyright = null!;
    private TextBlock _lblCredits = null!;
    private PhButton _btnWebsite = null!;
    private PhButton _btnGitHub = null!;
    private PhButton _btnEula = null!;
    private PhButton _btnPrivacy = null!;
    private PhButton _btnDonate = null!;
    private PhButton _btnCheckForUpdate = null!;


    private const string _creditContent = $"""
        ◍ Avalonia                                      MIT licence 
          https://github.com/AvaloniaUI/Avalonia
          Copyright (c) AvaloniaUI OÜ All Rights Reserved

        ◍ Magick.NET                             Apache-2.0 license
          https://github.com/dlemstra/Magick.NET
          Copyright (c) 2013-2026 Dirk Lemstra

        ◍ SkiaSharp                                     MIT licence
          https://github.com/mono/SkiaSharp
          Copyright (c) 2015-2016 Xamarin, Inc
          Copyright (c) 2017-2018 Microsoft Corporation

        ◍ Svg.Skia                                      MIT licence
          https://github.com/wieslawsoltes/Svg.Skia
          Copyright (c) 2020 Wiesław Šoltés

        ◍ NativeMemoryArray                             MIT licence
          https://github.com/Cysharp/NativeMemoryArray
          Copyright (c) 2021 Cysharp, Inc.

        ◍ CsWin32                                       MIT licence
          https://github.com/microsoft/CsWin32
          Copyright (c) Microsoft Corporation

        ◍ D2Phap.EggShell                        Apache-2.0 license
          Copyright (c) 2024-2026 Dương Diệu Pháp

        ◍ D2Phap.FileWatcherEx                          MIT licence
          https://github.com/d2phap/FileWatcherEx
          Copyright (c) 2018-2026 Dương Diệu Pháp

        ◍ ImageGlass logo idea
          Nguyễn Quốc Tuấn
        """;


    public AboutWindow()
    {
        IsButton1Visible = true;
        IsButton2Visible = false;
        IsButton3Visible = false;

        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;
        ShowInTaskbar = true;

        DialogContent = CreateDialogContentElement();
        DialogFooterLeftContent = CreateDialogFooterLeftContentElement();
    }


    #region Override Methods

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        UpdateLogo();
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Menu_MnuAbout];
        Button1Text = Core.Lang[LangId._Close];

        _lblSlogan.Text = Core.Lang[LangId._Slogan];
        _lblCredits.Text = Core.Lang[LangId._Credits];
        _btnWebsite.Text = Core.Lang[LangId._Homepage];
        _btnGitHub.Text = "GitHub";
        _btnEula.Text = Core.Lang[LangId._License];
        _btnPrivacy.Text = Core.Lang[LangId._Privacy];
        _btnDonate.Text = "❤️ " + Core.Lang[LangId._Donate];
        _btnCheckForUpdate.Text = Core.Lang[LangId._CheckForUpdate];

        UpdateVersionText();
    }

    #endregion // Override Methods



    #region Private Methods

    private void UpdateVersionText()
    {
        var magickVersion = MagickNET.Version;
        var dotnetVersion = RuntimeInformation.FrameworkDescription;

        _lblVersion.Text = $"""
            {Core.Lang[LangId._AboutVersion]} {Core.BuildInfo.FullVersion}
            {magickVersion}
            {dotnetVersion}
            """;
    }


    /// <summary>
    /// Creates dialog content element.
    /// </summary>
    private StackPanel CreateDialogContentElement()
    {
        // 1. App logo
        _imgLogo = new Image
        {
            Width = 96,
            Height = 96,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        UpdateLogo();


        // 2. App name
        var lblAppName = new TextBlock
        {
            Text = "ImageGlass 10",
            FontSize = 26,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            [!TextBlock.ForegroundProperty] = Resx.CreateBinding(ResxId.IG_TextAccentColor),
        };


        // 3. Slogan
        _lblSlogan = new TextBlock
        {
            FontSize = Const.FONT_SIZE_SUBTITLE,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };


        // 4. Version info
        _lblVersion = new SelectableTextBlock
        {
            FontSize = Const.FONT_SIZE_SMALL,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };


        var separator1 = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 16, 0, 12),
            [!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BorderNeutralBrush),
        };


        // 5. Link buttons (Website, GitHub, EULA, Privacy)
        _btnWebsite = CreateLinkButton(() => _ = BHelper.OpenUrlAsync(this, "https://imageglass.org", "from_about"));
        _btnGitHub = CreateLinkButton(() => _ = BHelper.OpenUrlAsync(this, "https://github.com/d2phap/ImageGlass", "from_about"));
        _btnEula = CreateLinkButton(() => _ = BHelper.OpenUrlAsync(this, "https://imageglass.org/license", "from_about"));
        _btnPrivacy = CreateLinkButton(() => _ = BHelper.OpenUrlAsync(this, "https://imageglass.org/privacy", "from_about"));

        var linksPanel = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        linksPanel.Children.AddRange([_btnWebsite, _btnGitHub, _btnEula, _btnPrivacy]);


        // 6. Copyright
        _lblCopyright = new SelectableTextBlock
        {
            Text = $"Copyright © 2010-{DateTime.UtcNow.Year} Dương Diệu Pháp",
            FontSize = Const.FONT_SIZE_SMALL,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };


        var separator2 = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 16),
            [!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BorderNeutralBrush),
        };


        // 7. Credits
        _lblCredits = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };

        var lblDetails = new Border
        {
            ClipToBounds = true,
            [!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius),
            Child = new ScrollViewer
            {
                MaxHeight = 100,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new SelectableTextBlock
                {
                    Padding = new Thickness(6),
                    FontSize = Const.FONT_SIZE_SMALL,
                    FontFamily = Const.FONT_CODE,
                    FontWeight = Avalonia.Media.FontWeight.SemiLight,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Text = _creditContent,
                    [!SelectableTextBlock.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BackgroundNeutralBrush),
                },
            }
        };


        // 8. Root layout
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Top,
            Spacing = 4,
        };

        root.Children.AddRange([
            _imgLogo,
            lblAppName,
            _lblSlogan,
            _lblVersion,
            separator1,
            linksPanel,
            _lblCopyright,
            separator2,
            _lblCredits,
            lblDetails,
        ]);


        // 8. Footer left content: Donate + Check for Update
        _btnDonate = new PhButton
        {
            MinWidth = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _btnDonate.Click += (_, _) =>
        {
            _ = BHelper.OpenUrlAsync(this, "https://imageglass.org/donate", "from_about");
        };

        return root;
    }


    private StackPanel CreateDialogFooterLeftContentElement()
    {
        _btnCheckForUpdate = new PhButton
        {
            MinWidth = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _btnCheckForUpdate.Click += async (_, _) =>
        {
            _ = await Core.API.RunApiAsync(API.IG_CheckForUpdate, "true");
        };

        var footerLeftPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        footerLeftPanel.Children.AddRange([_btnDonate, _btnCheckForUpdate]);

        return footerLeftPanel;
    }


    private static PhButton CreateLinkButton(Action clickFn)
    {
        var btn = new PhButton
        {
            // link appearance (transparent bg, accent text, hand cursor) comes from the Link variant
            Variant = PhButtonVariant.Link,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(1),
            FontSize = Const.FONT_SIZE_SMALL,
        };

        btn.Click += (_, _) => clickFn();

        return btn;
    }


    private void UpdateLogo()
    {
        if (_imgLogo is null) return;

        // 1.1 try load theme logo
        try
        {
            var iconPath = Core.Theme.GetIconPath(IgThemeIcon.AppLogo);
            _imgLogo.Source = new SvgImage
            {
                Source = SvgSource.Load(iconPath),
            };
        }
        catch { }

        // 1.2 load the default logo
        if (_imgLogo.Source is null)
        {
            using var stream = Resx.GetDefaultWindowIconAsStream();
            if (stream is not null)
            {
                _imgLogo.Source = Bitmap.DecodeToHeight(stream, 256);
            }
        }
    }

    #endregion // Private Methods



}
