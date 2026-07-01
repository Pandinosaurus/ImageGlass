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
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageGlass.Common;
using ImageGlass.Common.Localization;
using ImageGlass.Common.ServiceProviders;
using ImageGlass.UI;
using ImageGlass.UI.Viewer;
using System;

namespace ImageGlass.Tools;

public partial class FrameNavToolControl : PhControl, IToolControl
{

    public static string TOOL_ID => "Tool_FrameNav";
    public string ToolId => TOOL_ID;
    public bool HasSettingsUI => false;
    public object? Settings { get; } = null;
    public ViewerControl Viewer { get; set; } = null!;


    #region Public Properties 

    /// <summary>
    /// Gets value indicating if the current photo has embedding video.
    /// </summary>
    public bool IsLivePhoto
    {
        get => GetValue(IsLivePhotoProperty);
        private set => SetValue(IsLivePhotoProperty, value);
    }
    public static readonly StyledProperty<bool> IsLivePhotoProperty =
        AvaloniaProperty.Register<FrameNavToolControl, bool>(nameof(IsLivePhoto));


    /// <summary>
    /// Gets value indicating if the current photo has multiple frames.
    /// </summary>
    public bool HasMultiFrames
    {
        get => GetValue(HasMultiFramesProperty);
        private set => SetValue(HasMultiFramesProperty, value);
    }
    public static readonly StyledProperty<bool> HasMultiFramesProperty =
        AvaloniaProperty.Register<FrameNavToolControl, bool>(nameof(HasMultiFrames));


    /// <summary>
    /// Gets value indicating if the current photo can animate.
    /// </summary>
    public bool CanPlay
    {
        get => GetValue(CanPlayProperty);
        private set => SetValue(CanPlayProperty, value);
    }
    public static readonly StyledProperty<bool> CanPlayProperty =
        AvaloniaProperty.Register<FrameNavToolControl, bool>(nameof(CanPlay));


    /// <summary>
    /// Gets value indicating if the current photo is animating.
    /// </summary>
    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        private set => SetValue(IsPlayingProperty, value);
    }
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<FrameNavToolControl, bool>(nameof(IsPlaying));


    /// <summary>
    /// Gets frame text info.
    /// </summary>
    public string FrameTextInfo
    {
        get => GetValue(FrameTextInfoProperty);
        private set => SetValue(FrameTextInfoProperty, value);
    }
    public static readonly StyledProperty<string> FrameTextInfoProperty =
        AvaloniaProperty.Register<FrameNavToolControl, string>(nameof(FrameTextInfo));

    #endregion // Public Properties


    public FrameNavToolControl()
    {
        InitializeComponent();
    }



    #region Control Events

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateFrameInfo();

        Viewer.PhotoFrameChanged += Viewer_PhotoFrameChanged;

        PART_BtnViewFirstFrame.Click += PART_BtnViewFirstFrame_Click;
        PART_BtnViewPreviousFrame.Click += PART_BtnViewPreviousFrame_Click;
        PART_BtnToggleAnimation.Click += PART_BtnToggleAnimation_Click;
        PART_BtnViewNextFrame.Click += PART_BtnViewNextFrame_Click;
        PART_BtnViewLastFrame.Click += PART_BtnViewLastFrame_Click;
        PART_BtnExportFrame.Click += PART_BtnExportFrame_Click;
    }


    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        Viewer.PhotoFrameChanged -= Viewer_PhotoFrameChanged;

        PART_BtnViewFirstFrame.Click -= PART_BtnViewFirstFrame_Click;
        PART_BtnViewPreviousFrame.Click -= PART_BtnViewPreviousFrame_Click;
        PART_BtnToggleAnimation.Click -= PART_BtnToggleAnimation_Click;
        PART_BtnViewNextFrame.Click -= PART_BtnViewNextFrame_Click;
        PART_BtnViewLastFrame.Click -= PART_BtnViewLastFrame_Click;
        PART_BtnExportFrame.Click -= PART_BtnExportFrame_Click;
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();
        UpdateHotkeyTooltip();
    }


    private void Viewer_PhotoFrameChanged(ViewerControl sender, PhotoFrameChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateFrameInfo(e);
        });
    }


    private async void PART_BtnViewFirstFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ViewFirstFrame);
    }


    private async void PART_BtnViewPreviousFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ViewPreviousFrame);
    }


    private async void PART_BtnToggleAnimation_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ToggleImageAnimation);
    }


    private async void PART_BtnViewNextFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ViewNextFrame);
    }


    private async void PART_BtnViewLastFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ViewLastFrame);
    }


    private async void PART_BtnExportFrame_Click(object? sender, RoutedEventArgs e)
    {
        await Core.API!.RunApiAsync(API.IG_ExportImageFrames);
    }


    #endregion // Control Events



    #region Control Methods

    /// <summary>
    /// Updates the frame-related information for the current photo.
    /// </summary>
    private void UpdateFrameInfo(PhotoFrameChangedEventArgs? e = null)
    {
        var frameCount = e?.FrameCount ?? Viewer.Photo?.Metadata?.FrameCount ?? 0;
        var isAnimatedFormat = e?.CanAnimate ?? Viewer.Photo?.Metadata?.CanAnimate ?? false;

        HasMultiFrames = frameCount > 1;
        IsLivePhoto = e?.IsLivePhoto ?? Viewer.Photo?.Metadata?.IsLivePhoto ?? false;
        CanPlay = IsLivePhoto || isAnimatedFormat;
        IsPlaying = !IsLivePhoto && (e?.IsAnimating ?? false);

        if (frameCount > 0)
        {
            var currentFrame = Math.Max(0, Viewer.Photo?.FrameIndex ?? 0) + 1;
            var frameInfo = $"{currentFrame} / {frameCount}";

            FrameTextInfo = frameInfo;
        }
        else
        {
            FrameTextInfo = string.Empty;
        }
    }


    /// <summary>
    /// Updates the hotkey text for all buttons.
    /// </summary>
    private void UpdateHotkeyTooltip()
    {
        PART_BtnViewFirstFrame.VM.Text = nameof(LangId.Menu_MnuViewFirstFrame);
        PART_BtnViewFirstFrame.VM.HotkeyText
            = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuViewFirstFrame);

        PART_BtnViewPreviousFrame.VM.Text = nameof(LangId.Menu_MnuViewPreviousFrame);
        PART_BtnViewPreviousFrame.VM.HotkeyText
            = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuViewPreviousFrame);

        PART_BtnToggleAnimation.VM.Text = nameof(LangId.Menu_MnuToggleImageAnimation);
        PART_BtnToggleAnimation.VM.HotkeyText
            = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuToggleImageAnimation);

        PART_BtnViewNextFrame.VM.Text = nameof(LangId.Menu_MnuViewNextFrame);
        PART_BtnViewNextFrame.VM.HotkeyText
            = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuViewNextFrame);

        PART_BtnViewLastFrame.VM.Text = nameof(LangId.Menu_MnuViewLastFrame);
        PART_BtnViewLastFrame.VM.HotkeyText
            = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuViewLastFrame);

        PART_BtnExportFrame.VM.Text = nameof(LangId.Menu_MnuExportFrames);
        PART_BtnExportFrame.VM.HotkeyText
            = AppAPIProvider.GetMenuHotkeyText(LangId.Menu_MnuExportFrames);

    }

    #endregion // Control Methods


}

