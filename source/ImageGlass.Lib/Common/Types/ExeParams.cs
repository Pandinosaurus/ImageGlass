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

namespace ImageGlass.Common.Types;

public static class ExeParams
{
    /// <summary>
    /// Single instance message
    /// </summary>
    public static string SINGLE_INSTANCE => "--ig-single-instance";

    /// <summary>
    /// Opt-in startup profiler (see StartupTrace); writes ig_startup_trace.log to the config dir
    /// </summary>
    public static string STARTUP_TRACE => "--ig-startup-trace";

    /// <summary>
    /// Opt-in photo-loading profiler (see PhotoTrace); writes ig_photo_trace.log to the config dir
    /// </summary>
    public static string PHOTO_TRACE => "--ig-photo-trace";

    /// <summary>
    /// Suppresses the forced startup Quick Setup for this launch. Passed by the app when it
    /// restarts out of the wizard, so the fresh instance goes straight to the main window (and so
    /// an admin-locked <c>QuickSetupVersion</c> can't cause an infinite wizard loop).
    /// </summary>
    public static string NO_QUICK_SETUP => "--ig-no-quick-setup";


    public static string SET_DEFAULT_PHOTO_VIEWER => "set-default-viewer";

    public static string REMOVE_DEFAULT_PHOTO_VIEWER => "remove-default-viewer";




    // UI result options
    public static string HIDE_ADMIN_REQUIRED_ERROR_UI => "--hide-admin-error-ui";


    //public static string SET_WALLPAPER => "set-wallpaper";
    //public static string SET_LOCK_SCREEN => "set-lock-screen";
    //public static string START_SLIDESHOW => "start-slideshow";
    //public static string EXPORT_FRAMES => "export-frames";
    //public static string LOSSLESS_COMPRESS => "lossless-compress";

    //public static string CHECK_FOR_UPDATE => "check-for-update";
    //public static string INSTALL_LANGUAGES => "install-languages";
    //public static string INSTALL_THEMES => "install-themes";
    //public static string UNINSTALL_THEME => "uninstall-theme";

}
