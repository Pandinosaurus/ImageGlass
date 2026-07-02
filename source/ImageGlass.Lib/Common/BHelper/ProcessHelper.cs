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
using Avalonia.Controls.ApplicationLifetimes;
using ImageGlass.Common.Types;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common;


public partial class BHelper
{
    private static readonly TaskFactory _taskFactory = new(
        CancellationToken.None, TaskCreationOptions.None,
        TaskContinuationOptions.None, TaskScheduler.Default);


    /// <summary>
    /// Starts a process with the given command and arguments.
    /// </summary>
    public static void RunProcess(string fileName, string arguments)
    {
        using var proc = new Process();
        proc.StartInfo.FileName = fileName;
        proc.StartInfo.Arguments = arguments;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;
        proc.Start();
    }


    /// <summary>
    /// Runs a process and reads its standard output.
    /// </summary>
    public static string RunProcessAndReadOutput(string fileName, string arguments)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = fileName;
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.Start();

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return output;
        }
        catch
        {
            return string.Empty;
        }
    }


    /// <summary>
    /// Builds correct file path for executable and app protocol.
    /// </summary>
    public static (string Executable, string Args) BuildExeArgs(string executable, string arguments, string currentFilePath = "")
    {
        var exe = executable.Trim();
        var isAppProtocol = exe.EndsWith(':');

        // exclude the double quotes if the executable is app protocol
        var filePath = isAppProtocol ? currentFilePath : $"\"{currentFilePath}\"";

        var args = arguments.Replace(Const.FILE_MACRO, filePath);

        return (Executable: exe, Args: args);
    }


    /// <summary>
    /// Run a command, supports auto-elevating process privilege
    /// if admin permission is required.
    /// </summary>
    public static async Task<IgExitCode> RunExeCmd(string exePath, string args, bool waitForExit = true, bool appendIgArgs = true, bool showError = false)
    {
        IgExitCode code;

        try
        {
            if (appendIgArgs)
            {
                args += $" {ExeParams.HIDE_ADMIN_REQUIRED_ERROR_UI}";
            }

            code = (IgExitCode)await RunExeAsync(exePath, args, false, waitForExit, showError);


            // If that fails due to privs error, re-attempt with admin privs.
            if (code == IgExitCode.AdminRequired)
            {
                code = (IgExitCode)await RunExeAsync(
                    exePath,
                    args,
                    asAdmin: true,
                    waitForExit: waitForExit);
            }
        }
        catch
        {
            code = IgExitCode.Error;
        }

        return code;
    }


    /// <summary>
    /// Runs executable.
    /// </summary>
    public static async Task<int> RunExeAsync(string path, string args, bool asAdmin = false, bool waitForExit = false, bool showError = false)
    {
        var proc = new Process();

        // path is a protocal
        if (path.EndsWith(':'))
        {
            var url = $"{path}{args}";
            proc.StartInfo.FileName = url;
        }
        else
        {
            proc.StartInfo.FileName = path;
            proc.StartInfo.Arguments = args;
        }

        proc.StartInfo.Verb = asAdmin ? "runas" : "";
        proc.StartInfo.UseShellExecute = true;
        proc.StartInfo.ErrorDialog = showError;

        try
        {
            proc.Start();

            if (waitForExit)
            {
                await proc.WaitForExitAsync();

                return proc.ExitCode;
            }

            return (int)IgExitCode.Done;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("system cannot find the file", StringComparison.OrdinalIgnoreCase))
            {
                return (int)IgExitCode.Error_FileNotFound;
            }

            return (int)IgExitCode.Error;
        }
    }


    /// <summary>
    /// Runs an async function synchronous in a new thread.
    /// Source: <see href="https://github.com/aspnet/AspNetIdentity/blob/b7826741279450c58b230ece98bd04b4815beabf/src/Microsoft.AspNet.Identity.Core/AsyncHelper.cs" />
    /// </summary>
    public static TResult RunSync<TResult>(Func<Task<TResult>> func)
    {
        var cultureUi = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.CurrentCulture;

        return _taskFactory.StartNew(() =>
        {
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = cultureUi;
            return func();
        }).Unwrap().GetAwaiter().GetResult();
    }


    /// <summary>
    /// Runs an async function synchronous in a new thread.
    /// Source: <see href="https://github.com/aspnet/AspNetIdentity/blob/b7826741279450c58b230ece98bd04b4815beabf/src/Microsoft.AspNet.Identity.Core/AsyncHelper.cs" />
    /// </summary>
    public static void RunSync(Func<Task> func)
    {
        var cultureUi = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.CurrentCulture;

        _taskFactory.StartNew(() =>
        {
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = cultureUi;
            return func();
        }).Unwrap().GetAwaiter().GetResult();
    }


    /// <summary>
    /// Returns <c>true</c> if another instance of this app (besides the current process) is running.
    /// </summary>
    public static bool HasOtherInstances()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            var procs = Process.GetProcessesByName(current.ProcessName);

            var hasOther = false;
            foreach (var proc in procs)
            {
                if (proc.Id != current.Id) hasOther = true;
                proc.Dispose();
            }

            return hasOther;
        }
        catch { return false; }
    }


    /// <summary>
    /// Terminates all other running instances of this app, keeping the current process alive.
    /// </summary>
    public static void CloseOtherInstances()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var proc in Process.GetProcessesByName(current.ProcessName))
            {
                try
                {
                    if (proc.Id != current.Id) proc.Kill();
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }
    }


    /// <summary>
    /// Restarts the app: releases the single-instance mutex so a fresh instance can take ownership,
    /// launches it, then exits the current process.
    /// </summary>
    /// <param name="suppressQuickSetup">
    /// Pass <c>true</c> when restarting out of the Quick Setup wizard so the fresh instance skips
    /// the forced wizard for that launch (prevents an admin-locked version from looping).
    /// </param>
    public static void RestartApp(bool suppressQuickSetup = false)
    {
        // release the single-instance lock; otherwise the new instance would just forward to this
        // (exiting) one and quit, leaving no window
        Core.AppInstance.Dispose();

        var args = suppressQuickSetup ? ExeParams.NO_QUICK_SETUP : string.Empty;
        _ = RunExeAsync(AppExePath, args);
        ExitApp(false);
    }


    /// <summary>
    /// Exits the app.
    /// </summary>
    public static void ExitApp(bool forced, int exitCode = 0)
    {
        // force exit
        if (forced)
        {
            Environment.Exit(exitCode);
            return;
        }

        var appLf = Application.Current?.ApplicationLifetime;

        // try to exit the app
        if (appLf is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = desktop.TryShutdown(exitCode);
        }
    }

}
