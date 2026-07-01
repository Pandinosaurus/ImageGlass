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
using ImageGlass.Common.Localization;
using ImageGlass.Common.Photoing;
using ImageGlass.UI.Windowing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

public partial class ExportFramesWindow : ModalWindow
{
    private CancellationTokenSource _cancel = new();

    private string _srcFilePath;
    private string _destDirPath;
    private bool _isDone = false;


    public ExportFramesWindow(string srcFilePath, string destDirPath)
    {
        if (string.IsNullOrEmpty(srcFilePath)) throw new ArgumentNullException(nameof(srcFilePath));
        if (string.IsNullOrEmpty(destDirPath)) throw new ArgumentNullException(nameof(destDirPath));

        _srcFilePath = srcFilePath;
        _destDirPath = destDirPath;
        ShowInTaskbar = true;


        Description = srcFilePath;
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

        Title = Core.Lang[LangId._Title];
        Heading = Core.Lang[LangId._Title];
        Button1Text = Core.Lang[LangId._Start];
        Button2Text = Core.Lang[LangId._Cancel];
    }


    protected override void OnDialogSubmitted(DialogEventArgs e)
    {
        if (_isDone)
        {
            BHelper.OpenFolderPath(_destDirPath);
        }
        else
        {
            _ = RunAsync(_srcFilePath, _destDirPath);
        }
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
    /// Exports frames from the specified source file to the destination directory asynchronously.
    /// </summary>
    private async Task RunAsync(string srcFilePath, string destDirPath)
    {
        _isDone = false;
        _btn1.IsEnabled = false;
        _btn2.Focus(Avalonia.Input.NavigationMethod.Tab);

        IsButton1Visible = false;
        IsButton2Visible = true;
        Button2Text = Core.Lang[LangId._Cancel];

        IsProgressVisible = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;


        _ = Task.Factory.StartNew(async () =>
        {
            // start exporting frames
            await foreach (var info in MagickCodec.SaveFramesAsync(srcFilePath, destDirPath, _cancel.Token))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var percent = Math.Round((info.FrameNumber * 100f) / info.FrameCount, 0);

                    IsProgressIndeterminate = false;
                    ProgressValue = percent;
                    Title = $"{Core.Lang[LangId._Title]} ({percent}%)";

                    // done
                    if (info.FrameNumber == info.FrameCount)
                    {
                        _btn1.IsEnabled = true;
                        _btn1.Focus(Avalonia.Input.NavigationMethod.Tab);
                        IsButton1Visible = true;
                        Button1Text = Core.Lang[LangId._OpenOutputFolder];

                        Button2Text = Core.Lang[LangId._Close];
                        Description = string.Format(Core.Lang[LangId._ExportDone],
                            info.FrameNumber,
                            $"\"{destDirPath}\"");

                        IsProgressVisible = false;
                        _isDone = true;
                    }

                    // in progress
                    else
                    {
                        var frameFilePath = Path.Combine(destDirPath, info.FileName);

                        Description = string.Format(Core.Lang[LangId._Exporting],
                            info.FrameNumber,
                            info.FrameCount,
                            $"\"{frameFilePath}\"");
                    }
                });
            }
        }, _cancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    #endregion // Private Methods


}
