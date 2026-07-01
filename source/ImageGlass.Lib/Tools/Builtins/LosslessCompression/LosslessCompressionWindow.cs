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
using Avalonia.Threading;
using ImageGlass.Common;
using ImageGlass.Common.Localization;
using ImageGlass.Common.Photoing;
using ImageGlass.UI.Windowing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Tools;

public partial class LosslessCompressionWindow : ModalWindow
{
    private CancellationTokenSource _cancel = new();
    private FileInfo _srcFileInfo;



    public LosslessCompressionWindow(string srcFilePath)
    {
        if (string.IsNullOrEmpty(srcFilePath)) throw new ArgumentNullException(nameof(srcFilePath));

        _srcFileInfo = new FileInfo(srcFilePath);

        ShowInTaskbar = true;
        Note = $"""
            {srcFilePath}

            {Core.Photos.CurrentMetadata?.FileSizeFormatted}
            """;
        NoteStyle = InfoBarSeverity.Info;
        Thumbnail = Core.Photos.Current?.GalleryThumbnail;

        IsButton1Visible = true;
        IsButton2Visible = true;
        IsButton3Visible = false;
        DefaultButton = DialogButton.Button1;
        DefaultFocus = DialogFocus.Button1;
    }



    #region Override Methods

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        Title = Core.Lang[LangId.Menu_MnuLosslessCompression];
        Heading = Core.Lang[LangId.Menu_MnuLosslessCompression_Confirm];
        Description = Core.Lang[LangId.Menu_MnuLosslessCompression_Description];
        Button1Text = Core.Lang[LangId._Yes];
        Button2Text = Core.Lang[LangId._No];
    }


    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        _ = RunAsync(_srcFileInfo);
    }


    protected override void OnDialogCancelled(DialogEventArgs e)
    {
        _cancel?.Cancel();
        _cancel?.Dispose();

        base.OnDialogCancelled(e);
    }


    protected override void OnDialogAborted()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();

        base.OnDialogAborted();
    }

    #endregion // Override Methods



    #region Private methods

    /// <summary>
    /// Performs lossless compression.
    /// </summary>
    private async Task RunAsync(FileInfo fi)
    {
        var oldFileLength = fi.Length;

        _btn1.IsEnabled = false;
        _btn2.Focus(Avalonia.Input.NavigationMethod.Tab);

        IsButton1Visible = false;
        IsButton2Visible = true;
        Button2Text = Core.Lang[LangId._Cancel];

        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;

        Heading = Core.Lang[LangId.Menu_MnuLosslessCompression_Compressing];
        Description = Core.Lang[LangId.Menu_MnuLosslessCompression_Description];
        Note = $"""
            {fi.FullName}

            {Core.Photos.CurrentMetadata?.FileSizeFormatted}
            """;

        _ = Task.Factory.StartNew(async () =>
        {
            // start compressing
            MagickCodec.LosslessCompress(fi.FullName);
            await Task.Delay(200); // make it feel slow for better UX

            // done, show stats
            Dispatcher.UIThread.Post(() =>
            {
                var newFi = new FileInfo(fi.FullName);
                var percent = Math.Round((1 - (newFi.Length * 1f / oldFileLength)) * 100f, 2);

                Button2Text = Core.Lang[LangId._Close];
                Heading = Core.Lang[LangId.Menu_MnuLosslessCompression_Done];
                Note = $"""
                    {newFi.FullName}

                    {Core.Photos.CurrentMetadata?.FileSizeFormatted} ⇒ {BHelper.FormatSize(newFi.Length)} (↓ {percent}%)
                    """;

                ProgressValue = 100;
                IsProgressIndeterminate = false;
                IsProgressVisible = false;
            });
        }, _cancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    #endregion // Private Methods


}
