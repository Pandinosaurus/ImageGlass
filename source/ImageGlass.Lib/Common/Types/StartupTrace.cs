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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ImageGlass.Common.Types;


/// <summary>
/// Opt-in, cross-platform startup profiler. Records wall-clock milestones from process start to the
/// first window paint and writes them to <c>ig_startup_trace.log</c> in the config dir.
/// </summary>
/// <remarks>
/// <para>
/// Enable it by launching with the <see cref="ExeParams.STARTUP_TRACE"/> 
/// command-line flag; <see cref="EnableFromArgs"/> is called during app-instance initialization.
/// </para>
/// <para>
/// Marks are always buffered (a couple of cheap locked list adds during startup only), so marks
/// recorded before the flag is parsed - e.g. the very first <c>Main</c> mark - are still captured.
/// <see cref="Flush"/> only produces output when tracing is enabled, so a normal launch is silent.
/// </para>
/// </remarks>
public static class StartupTrace
{
    private static readonly Stopwatch _sw = Stopwatch.StartNew();
    private static readonly List<(long Ms, string Name, int Tid)> _marks = new(32);
    private static readonly Lock _lock = new();
    private static bool _flushed;


    /// <summary>
    /// Whether trace output is enabled. Set by <see cref="EnableFromArgs"/> from the CLI flag.
    /// </summary>
    public static bool Enabled { get; private set; }


    /// <summary>
    /// Enables trace output if the given command-line args contain
    /// <see cref="ExeParams.STARTUP_TRACE"/>. Safe to call after marks have been recorded.
    /// </summary>
    public static void EnableFromArgs(string[]? args)
    {
        if (Enabled || args is null) return;

        foreach (var arg in args)
        {
            if (string.Equals(arg, ExeParams.STARTUP_TRACE, StringComparison.OrdinalIgnoreCase))
            {
                Enabled = true;
                return;
            }
        }
    }


    /// <summary>
    /// Records a named milestone with the current elapsed time. Always buffered so it survives being
    /// called before the trace flag is parsed; output is gated by <see cref="Enabled"/> in
    /// <see cref="Flush"/>.
    /// </summary>
    public static void Mark(string name)
    {
        lock (_lock)
        {
            _marks.Add((_sw.ElapsedMilliseconds, name, Environment.CurrentManagedThreadId));
        }
    }


    /// <summary>
    /// Writes the recorded marks (with per-step deltas) to the debug output and appends one block to file
    /// in the config dir. Writes at most once per run (call it from the last  startup step).
    /// No-op when tracing is disabled.
    /// </summary>
    public static void Flush()
    {
        if (!Enabled) return;

        lock (_lock)
        {
            if (_flushed) return;
            _flushed = true;

            var lines = new List<string>(_marks.Count + 3)
            {
                $"===== ImageGlass startup trace @ PID {Environment.ProcessId} =====",
            };

            // OS-launch -> first mark estimate (meaningful for native single-file AOT: no extraction)
            try
            {
                var startToNow = DateTime.Now - Process.GetCurrentProcess().StartTime;
                lines.Add($"   (process start -> first mark ~ {startToNow.TotalMilliseconds,7:0.0} ms)");
            }
            catch { }

            long prev = 0;
            foreach (var (ms, name, tid) in _marks)
            {
                lines.Add($"{ms,7} ms  (+{ms - prev,6} ms)  [t{tid}]  {name}");
                prev = ms;
            }

            foreach (var line in lines)
            {
                Debug.WriteLine($"[IG-STARTUP] {line}");
            }

            try
            {
                var logPath = BHelper.ConfigDir("ig_startup_trace.log");
                File.AppendAllText(logPath, string.Join(Environment.NewLine, lines)
                    + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
