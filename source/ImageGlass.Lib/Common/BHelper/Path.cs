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
using ImageGlass.Common.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;


namespace ImageGlass.Common;

public partial class BHelper
{
    private static string Win32ShortcutExtension => ".lnk";


    /// <summary>
    /// Gets the base dir path.
    /// </summary>
    public static string BasePath => AppDomain.CurrentDomain.BaseDirectory;


    /// <summary>
    /// Gets the config dir path.
    /// </summary>
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);



    /// <summary>
    /// Computes the full path based on the installed folder.
    /// </summary>
    public static string BaseDir(params string[] paths)
    {
        var newPaths = paths.ToList();
        newPaths.Insert(0, BasePath);
        var path = Path.Combine([.. newPaths]);

        return path;
    }


    /// <summary>
    /// Computes the full path based on the config folder.
    /// Also, auto-create the built-in directory if not exist.
    /// </summary>
    public static string ConfigDir(params string[] paths)
    {
        var isDirCreated = false;

        // 1. auto-create built-in directory if not exist
        if (paths.Length > 0)
        {
            var firstPath = paths[0];
            var isBuiltinDir = firstPath.Equals(Dir.Themes, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.ExtIcons, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Language, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Plugins, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Cache, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Temporary, StringComparison.OrdinalIgnoreCase)
                || firstPath.Equals(Dir.Logs, StringComparison.OrdinalIgnoreCase);

            // create the built-in directory if not exist
            if (isBuiltinDir)
            {
                var builtinConfigPath = Path.Combine(ConfigPath, firstPath);
                Directory.CreateDirectory(builtinConfigPath);
                isDirCreated = true;
            }
        }


        // 2. create the config directory if not exist
        if (!isDirCreated)
        {
            Directory.CreateDirectory(ConfigPath);
        }


        // 3. build the complete path
        var newPaths = paths.ToList();
        newPaths.Insert(0, ConfigPath);
        var path = Path.Combine([.. newPaths]);

        return path;
    }


    /// <summary>
    /// Like <see cref="ConfigDir(string[])"/> but resolves to the real physical path (MSIX may
    /// redirect config to the package container); use it to show/open config in the file explorer.
    /// </summary>
    public static string GetRealPlatformConfigDir(params string[] paths)
    {
        var configPath = ConfigDir(paths);
        return Core.ShellProvider?.GetActualConfigDirPath(configPath) ?? configPath;
    }


    /// <summary>
    /// Check if the given path (file or directory) is writable. 
    /// </summary>
    /// <param name="type">Indicates if the given path is either file or directory</param>
    /// <param name="path">Full path of file or directory</param>
    public static bool CheckPathWritable(PathType type, string path)
    {
        try
        {
            // If path is file
            if (type == PathType.File)
            {
                using (File.OpenWrite(path)) { }
            }

            // if path is directory
            else
            {
                var isDirExist = Directory.Exists(path);

                if (!isDirExist)
                {
                    Directory.CreateDirectory(path);
                }

                var sampleFile = Path.Combine(path, "test_write_file.temp");

                using (File.Create(sampleFile)) { }
                File.Delete(sampleFile);

                if (!isDirExist)
                {
                    Directory.Delete(path, true);
                }
            }


            return true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Checks type of the path.
    /// </summary>
    public static PathType CheckPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return PathType.Unknown;

        try
        {
            var attrs = File.GetAttributes(path);

            if (attrs.HasFlag(FileAttributes.Directory))
            {
                return PathType.Dir;
            }

            return PathType.File;
        }
        catch { }

        return PathType.Unknown;
    }


    /// <summary>
    /// Checks whether <paramref name="path"/> resolves to a location inside
    /// <paramref name="root"/> (or equals it). Both are resolved with
    /// <see cref="System.IO.Path.GetFullPath(string)"/> first, so <c>..</c>
    /// segments and absolute paths cannot escape the root.
    /// </summary>
    public static bool IsPathContainedIn(string? path, string? root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;

        string fullPath, fullRoot;
        try
        {
            fullPath = Path.GetFullPath(path);
            fullRoot = Path.GetFullPath(root);
        }
        catch
        {
            return false;
        }

        // Trailing separator on root so a sibling like "_pluginsEvil" can't prefix-match "_plugins".
        var sep = Path.DirectorySeparatorChar;
        if (!fullRoot.EndsWith(sep)) fullRoot += sep;

        // Windows and macOS default to case-insensitive filesystems; Linux is case-sensitive.
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return (fullPath + sep).StartsWith(fullRoot, comparison);
    }


    /// <summary>
    /// Get distinct directories list from paths list.
    /// </summary>
    public static (List<string> DirPaths, List<string> FilePaths) GetDistinctDirsFromPaths(IEnumerable<string> pathList)
    {
        if (!pathList.Any()) return ([], []);

        var hashedDirsList = new HashSet<string>();
        var hashedFilesList = new HashSet<string>();

        foreach (var path in pathList)
        {
            var pathType = BHelper.CheckPath(path);
            if (pathType == PathType.Unknown) continue;

            if (pathType == PathType.Dir)
            {
                hashedDirsList.Add(path);
            }
            else
            {
                string? dir;

                if (string.Equals(Path.GetExtension(path), Win32ShortcutExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (Core.ShellProvider is null) continue;

                    var shortcutPath = Core.ShellProvider.GetTargetPathFromShortcut(path);
                    if (string.IsNullOrEmpty(shortcutPath)) continue;

                    var shortcutPathType = BHelper.CheckPath(shortcutPath);
                    if (shortcutPathType == PathType.Unknown) continue;

                    // get the DIR path of shortcut target
                    if (shortcutPathType == PathType.Dir)
                    {
                        dir = shortcutPath;
                    }
                    else
                    {
                        hashedFilesList.Add(shortcutPath);
                        dir = Path.GetDirectoryName(shortcutPath) ?? "";
                    }
                }
                else
                {
                    hashedFilesList.Add(path);
                    dir = Path.GetDirectoryName(path) ?? null;
                }


                if (string.IsNullOrEmpty(dir)) continue;
                hashedDirsList.Add(dir);
            }
        }

        return ([.. hashedDirsList], [.. hashedFilesList]);
    }


    /// <summary>
    /// Gets the next (<paramref name="direction"/> = <c>+1</c>) or previous (<c>-1</c>) sibling
    /// directory relative to <paramref name="currentPath"/> that directly contains at least one
    /// image with an allowed extension. Empty/unreadable siblings are skipped.
    /// </summary>
    /// <param name="currentPath">A directory path, or an image file path (its folder is used).</param>
    /// <param name="direction"><c>+1</c> for next, <c>-1</c> for previous.</param>
    /// <param name="allowedExtensions">Allowed extensions with a leading dot (e.g. <c>.jpg</c>).</param>
    /// <returns>Full path of the sibling directory, or <c>null</c> if none is found.</returns>
    public static string? GetSiblingDir(string? currentPath, int direction, ICollection<string> allowedExtensions)
    {
        if (string.IsNullOrEmpty(currentPath)) return null;

        // accept a file path too: use its containing folder
        var currentDir = CheckPath(currentPath) == PathType.File
            ? Path.GetDirectoryName(currentPath)
            : currentPath;
        if (string.IsNullOrEmpty(currentDir)) return null;

        currentDir = currentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDir = Directory.GetParent(currentDir)?.FullName;
        if (string.IsNullOrEmpty(parentDir)) return null;

        try
        {
            var siblingDirs = Directory.GetDirectories(parentDir)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentIndex = siblingDirs.FindIndex(d =>
                string.Equals(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    currentDir, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0) return null;

            for (var i = currentIndex + direction; i >= 0 && i < siblingDirs.Count; i += direction)
            {
                if (DirContainsImage(siblingDirs[i], allowedExtensions)) return siblingDirs[i];
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return null;
    }


    /// <summary>
    /// Checks whether <paramref name="dir"/> directly contains a file whose extension is in
    /// <paramref name="allowedExtensions"/> (extensions include the leading dot, e.g. <c>.jpg</c>).
    /// </summary>
    public static bool DirContainsImage(string? dir, ICollection<string> allowedExtensions)
    {
        if (string.IsNullOrEmpty(dir)) return false;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (allowedExtensions.Contains(Path.GetExtension(file))) return true;
            }
        }
        catch { }

        return false;
    }


    /// <summary>
    /// Returns the image file paths (matching <paramref name="allowedExtensions"/>) directly inside
    /// <paramref name="dir"/>, ordered by name (case-insensitive). Empty on error.
    /// </summary>
    public static List<string> GetImageFilesInDir(string? dir, ICollection<string> allowedExtensions)
    {
        if (string.IsNullOrEmpty(dir)) return [];

        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return []; }
    }


    /// <summary>
    /// Resolves a relative/protocol/link path to absolute path,
    /// including <c>.app</c> bundle on macOS.
    /// </summary>
    public static string ResolvePath(string? inputPath)
    {
        if (string.IsNullOrEmpty(inputPath))
            return inputPath ?? string.Empty;

        var path = inputPath;
        const string protocol = Const.APP_PROTOCOL + ":";

        // if inputPath is URI Scheme
        if (path.StartsWith(protocol))
        {
            // Retrieve the real path
            path = Uri.UnescapeDataString(path)[protocol.Length..];
        }

        // if path is wrapped by quotes
        if (path.Length > 2 && path.StartsWith('"') && path.EndsWith('"'))
        {
            path = path[1..^1];
        }

        // parse environment vars to absolute path
        path = Environment.ExpandEnvironmentVariables(path);

        if (string.Equals(Path.GetExtension(inputPath), Win32ShortcutExtension, StringComparison.OrdinalIgnoreCase))
        {
            path = Core.ShellProvider?.GetTargetPathFromShortcut(path) ?? path;
        }

        // macOS: a .app is a directory, so resolve to its inner executable for direct launching
        if (OS == OSType.Mac
            && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(path))
        {
            // Prefer CFBundleExecutable from Info.plist; fall back to the bundle name.
            var exeName = GetMacOsAppExecutableName(path)
                ?? Path.GetFileNameWithoutExtension(path.TrimEnd('/'));

            var innerExe = Path.Combine(path, "Contents", "MacOS", exeName);
            if (File.Exists(innerExe)) path = innerExe;
        }

        return path;
    }


    /// <summary>
    /// Reads <c>CFBundleExecutable</c> from a macOS app bundle's <c>Contents/Info.plist</c>.
    /// Returns <c>null</c> if the plist is missing or the key is absent.
    /// </summary>
    private static string? GetMacOsAppExecutableName(string appBundlePath)
    {
        var plistPath = Path.Combine(appBundlePath, "Contents", "Info.plist");
        if (!File.Exists(plistPath)) return null;

        try
        {
            // Info.plist is a <dict> of alternating <key>/<value> siblings.
            var doc = System.Xml.Linq.XDocument.Load(plistPath);
            var dict = doc.Root?.Element("dict");
            if (dict is null) return null;

            var elements = dict.Elements().ToList();
            for (var i = 0; i < elements.Count - 1; i++)
            {
                if (elements[i].Name.LocalName == "key"
                    && elements[i].Value == "CFBundleExecutable"
                    && elements[i + 1].Name.LocalName == "string")
                {
                    var name = elements[i + 1].Value.Trim();
                    return string.IsNullOrEmpty(name) ? null : name;
                }
            }
        }
        catch { }

        return null;
    }


    /// <summary>
    /// Builds the command line from config value.
    /// Example: <c>-p:EnableFullScreen=True</c>
    /// </summary>
    public static string BuildConfigCmdLine(string configName, object? configValue)
    {
        if (configValue == null) return string.Empty;

        return $"{Const.CONFIG_CMD_PREFIX}{configName}=\"{configValue}\"";
    }



    /// <summary>
    /// Open URL in the default browser.
    /// </summary>
    public static async Task OpenUrlAsync(Visual? visual, string? url, string campaign = "from_unknown")
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;

            // only tag http(s) web URLs with campaign tracking; protocol URIs (ms-settings:,
            // mailto:, ...) must launch untouched or the extra query corrupts the deep link
            if (uri.Scheme is "http" or "https")
            {
                var ub = new UriBuilder(uri);
                var queries = HttpUtility.ParseQueryString(ub.Query);
                queries["utm_source"] = $"app_{Core.BuildInfo.AppVersion}";
                queries["utm_medium"] = "app_click";
                queries["utm_campaign"] = campaign;

                ub.Query = queries.ToString();
                uri = ub.Uri;
            }


            var launcher = TopLevel.GetTopLevel(visual)?.Launcher;
            if (launcher is not null)
            {
                await launcher.LaunchUriAsync(uri);
            }
        }
        catch { }
    }


    /// <summary>
    /// Opens file path in Explorer and selects it.
    /// </summary>
    public static void OpenFilePath(string? filePath)
    {
        if (Core.ShellProvider is null) return;

        Core.ShellProvider.OpenFilePath(filePath);
    }


    /// <summary>
    /// Opens the folder path in Explorer, creates the folder path if not existed.
    /// </summary>
    public static void OpenFolderPath(string? dirPath)
    {
        if (Core.ShellProvider is null) return;

        Core.ShellProvider.OpenFolderPath(dirPath);
    }


    /// <summary>
    /// Deletes a file with option to move to recycle bin.
    /// </summary>
    public static void DeleteFile(string filePath, bool moveToRecycleBin = true)
    {
        if (Core.ShellProvider is not null)
        {
            Core.ShellProvider.DeleteFile(filePath, moveToRecycleBin);
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch { }
    }

}

