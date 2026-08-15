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
using Avalonia.Platform;
using ImageGlass.Common.Types;
using NetCoreAudio;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common;


public partial class BHelper
{
    private const string NOTIFICATION_SOUND_ASSET = "avares://ImageGlass.Lib/Assets/Sounds/notification.wav";
    private static readonly Lock _notificationSoundLock = new();
    private static string? _notificationSoundPath;
    private static Player? _notificationPlayer;


    /// <summary>
    /// Plays the app notification sound. Failures are silent:
    /// a missed notification sound must never interrupt what triggered it.
    /// </summary>
    public static async Task PlayNotificationSoundAsync()
    {
        try
        {
            var filePath = GetNotificationSoundPath();
            if (filePath is null) return;

            var player = GetNotificationPlayer();
            if (player is null) return;

            // returns as soon as playback starts; a new call replaces the sound still playing
            await player.Play(filePath).ConfigureAwait(false);
        }
        catch { }
    }


    /// <summary>
    /// Gets the shared player, built on first use because its constructor throws on an OS
    /// <see cref="Player"/> has no backend for, which a static initializer could not contain.
    /// </summary>
    private static Player? GetNotificationPlayer()
    {
        lock (_notificationSoundLock)
        {
            try
            {
                _notificationPlayer ??= new Player();
            }
            catch { }

            return _notificationPlayer;
        }
    }


    /// <summary>
    /// Extracts the bundled notification WAV into the temp dir once per session: every platform
    /// plays it by file path, but the asset only exists inside the assembly.
    /// </summary>
    private static string? GetNotificationSoundPath()
    {
        lock (_notificationSoundLock)
        {
            if (_notificationSoundPath is not null && File.Exists(_notificationSoundPath))
            {
                return _notificationSoundPath;
            }

            try
            {
                var filePath = ConfigDir(Dir.Temporary, "ig_notification.wav");

                using (var assetStream = AssetLoader.Open(new Uri(NOTIFICATION_SOUND_ASSET)))
                using (var fileStream = File.Create(filePath))
                {
                    assetStream.CopyTo(fileStream);
                }

                _notificationSoundPath = filePath;
            }
            catch
            {
                _notificationSoundPath = null;
            }

            return _notificationSoundPath;
        }
    }

}
