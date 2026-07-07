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
using ImageGlass.Common;
using ImageGlass.Common.Types;
using Microsoft.Win32;
using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace ImageGlass.Win32.Common;

public static class Win32DefaultAppApi
{
    /// <summary>
    /// Registers or unregisters the app as the default photo viewer for the specified file extensions.
    /// Returns the registry scope (per-user vs per-machine) that was used.
    /// </summary>
    public static async Task<DefaultAppScope> SetDefaultPhotoViewerAsync(string[] extensions, bool enable)
    {
        var scope = GetScope();
        var root = scope == DefaultAppScope.LocalMachine
            ? Registry.LocalMachine
            : Registry.CurrentUser;

        try
        {
            if (enable)
            {
                RegisterAppAndExtensions(root, extensions);
            }
            else
            {
                UnregisterAppAndExtensions(root, extensions);
            }

            NotifyShellAssocChanged();
        }
        catch (UnauthorizedAccessException)
        {
            // per-machine (HKLM) writes need admin; relaunch elevated to finish the job
            await RelaunchElevatedAsync(extensions, enable);
        }
        catch (SecurityException)
        {
            await RelaunchElevatedAsync(extensions, enable);
        }

        return scope;
    }


    /// <summary>
    /// Determines the registry scope from the install location: per-machine when the app runs
    /// from a system location (e.g. Program Files), otherwise per-user (portable install).
    /// </summary>
    public static DefaultAppScope GetScope()
    {
        var exeDir = Path.GetDirectoryName(BHelper.AppExePath);
        if (string.IsNullOrEmpty(exeDir)) return DefaultAppScope.CurrentUser;

        string[] machineRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];

        foreach (var machineRoot in machineRoots)
        {
            if (!string.IsNullOrEmpty(machineRoot)
                && exeDir.StartsWith(machineRoot, StringComparison.OrdinalIgnoreCase))
            {
                return DefaultAppScope.LocalMachine;
            }
        }

        return DefaultAppScope.CurrentUser;
    }


    /// <summary>
    /// Registers file type associations and app capabilities to the registry
    /// under the given <paramref name="root"/> hive (HKCU or HKLM).
    /// </summary>
    private static void RegisterAppAndExtensions(RegistryKey root, string[] extensions)
    {
        var capabilitiesPath = $@"Software\{BHelper.AppName}\Capabilities";
        var classesKey = root.OpenSubKey(@"Software\Classes", writable: true);

        // 1. register the application:
        // <root>\Software\RegisteredApplications
        using (var key = root.OpenSubKey(@"Software\RegisteredApplications", writable: true))
        {
            key?.SetValue(BHelper.AppName, capabilitiesPath);
        }


        // 2. register application information:
        // <root>\Software\ImageGlass\Capabilities
        using (var key = root.CreateSubKey(capabilitiesPath, writable: true))
        {
            key.SetValue("ApplicationName", BHelper.AppDisplayName);
            key.SetValue("ApplicationIcon", $"\"{BHelper.AppExePath}\", 0");
            key.SetValue("ApplicationDescription", "A Fast, Seamless Photo Viewer");

            // register file type associations:
            // HKCU\Software\ImageGlass\Capabilities\FileAssociations
            using var faKey = key.CreateSubKey("FileAssociations", writable: true);
            foreach (var ext in extensions)
            {
                var extNoDot = ext.TrimStart('.').ToUpperInvariant();
                var progId = $"{BHelper.AppName}.AssocFile.{extNoDot}";
                faKey.SetValue(ext, progId);

                // HKCU\Software\Classes\...
                RegisterProgId(classesKey, progId, extNoDot);
                AssociateExtensionDefault(classesKey, ext, progId);
                ClearUserChoice(ext);
            }
        }

        classesKey?.Dispose();
    }


    /// <summary>
    /// Registers a ProgId under the <c>Software\Classes</c> subkey of the active hive.
    /// </summary>
    private static void RegisterProgId(RegistryKey? classesKey, string progId, string extNoDot)
    {
        if (classesKey is null) return;

        // <root>\Software\Classes\ImageGlass.AssocFile.<EXT>
        using var progIdKey = classesKey.CreateSubKey(progId, writable: true);
        progIdKey.SetValue("", BHelper.AppDisplayName);

        // 1. DefaultIcon
        // get extension icon
        var iconPath = BHelper.ConfigDir(Dir.ExtIcons, $"{extNoDot}.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = BHelper.BaseDir(Dir.ExtIcons, $"{extNoDot}.ico");

            if (!File.Exists(iconPath))
            {
                iconPath = string.Empty;
            }
        }

        // set extension icon
        if (!string.IsNullOrEmpty(iconPath))
        {
            // HKCU\Software\Classes\ImageGlass.AssocFile.<EXT>\DefaultIcon
            using var iconKey = progIdKey.CreateSubKey("DefaultIcon", writable: true);
            iconKey.SetValue("", iconPath);
        }


        // 2. HKCU\Software\Classes\ImageGlass.AssocFile.<EXT>\shell\open
        using var shellKey = progIdKey.CreateSubKey("shell", writable: true);
        using var openKey = shellKey.CreateSubKey("open", writable: true);
        openKey.SetValue("FriendlyAppName", BHelper.AppDisplayName);


        // 3. HKCU\Software\Classes\ImageGlass.AssocFile.<EXT>\shell\open\command
        using var commandKey = openKey.CreateSubKey("command", writable: true);
        commandKey.SetValue("", $"\"{BHelper.AppExePath}\" \"%1\"");
    }


    /// <summary>
    /// Sets our ProgId as the extension's classic default and adds it to <c>OpenWithProgids</c>.
    /// </summary>
    private static void AssociateExtensionDefault(RegistryKey? classesKey, string ext, string progId)
    {
        if (classesKey is null) return;

        // <root>\Software\Classes\.<EXT>  (default + OpenWithProgids)
        using var extKey = classesKey.CreateSubKey(ext, writable: true);
        extKey.SetValue("", progId);

        using var openWith = extKey.CreateSubKey("OpenWithProgids", writable: true);
        openWith.SetValue(progId, string.Empty);
    }


    /// <summary>
    /// Removes the current user's hash-protected UserChoice (always HKCU, both scopes) so the
    /// classic default applies; optionally only when it points to <paramref name="onlyIfProgId"/>.
    /// </summary>
    private static void ClearUserChoice(string ext, string? onlyIfProgId = null)
    {
        try
        {
            using var fileExts = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}", writable: true);
            if (fileExts is null) return;

            if (onlyIfProgId is not null)
            {
                using var uc = fileExts.OpenSubKey("UserChoice");
                if (uc?.GetValue("ProgId") as string != onlyIfProgId) return;
            }

            // reg.exe / Remove-Item are denied on UserChoice; DeleteSubKey via the parent is allowed
            fileExts.DeleteSubKey("UserChoice", throwOnMissingSubKey: false);
        }
        catch { }
    }


    /// <summary>
    /// Unregisters file type associations and app information from the registry
    /// under the given <paramref name="root"/> hive (HKCU or HKLM).
    /// </summary>
    private static void UnregisterAppAndExtensions(RegistryKey root, string[] extensions)
    {
        // 1. unregister the application:
        // <root>\Software\RegisteredApplications\ImageGlass
        using (var key = root.OpenSubKey(@"Software\RegisteredApplications", writable: true))
        {
            key?.DeleteValue(BHelper.AppName, throwOnMissingValue: false);
        }

        // 2. delete application information:
        // <root>\Software\ImageGlass\*
        using (var key = root.OpenSubKey("Software", writable: true))
        {
            key?.DeleteSubKeyTree(BHelper.AppName, throwOnMissingSubKey: false);
        }

        // 3. delete ProgIds and OpenWithProgids entries:
        // <root>\Software\Classes\...
        using var classesKey = root.OpenSubKey(@"Software\Classes", writable: true);
        if (classesKey is null) return;

        foreach (var ext in extensions)
        {
            var extNoDot = ext.TrimStart('.').ToUpperInvariant();
            var progId = $"{BHelper.AppName}.AssocFile.{extNoDot}";

            // remove HKCU\Software\Classes\ImageGlass.AssocFile.<EXT>\*
            classesKey.DeleteSubKeyTree(progId, throwOnMissingSubKey: false);

            using var extKey = classesKey.OpenSubKey(ext, writable: true);
            if (extKey is not null)
            {
                // clear the default if it points to us
                if (extKey.GetValue("") as string == progId)
                {
                    extKey.DeleteValue("", throwOnMissingValue: false);
                }

                using var openWith = extKey.OpenSubKey("OpenWithProgids", writable: true);
                openWith?.DeleteValue(progId, throwOnMissingValue: false);
            }

            // drop our UserChoice so Explorer re-picks a default
            ClearUserChoice(ext, progId);
        }
    }


    /// <summary>
    /// Notifies the shell that file associations have changed.
    /// </summary>
    private static unsafe void NotifyShellAssocChanged()
    {
        PInvoke.SHChangeNotify(
            SHCNE_ID.SHCNE_ASSOCCHANGED,
            SHCNF_FLAGS.SHCNF_IDLIST,
            null, null);
    }


    /// <summary>
    /// Re-launches the current process with admin elevation to perform
    /// the file association change, then waits for it to finish.
    /// </summary>
    private static async Task RelaunchElevatedAsync(string[] extensions, bool enable)
    {
        var cmd = enable
            ? AppCmds.SET_DEFAULT_PHOTO_VIEWER
            : AppCmds.REMOVE_DEFAULT_PHOTO_VIEWER;
        var extArg = string.Join(";", extensions);

        // reuse the shared elevating launcher (UAC prompt + cancellation handled there)
        await BHelper.RunExeAsync(BHelper.AppExePath, [cmd, extArg], asAdmin: true, waitForExit: true);
    }

}
