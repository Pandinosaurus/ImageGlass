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
using ImageGlass.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ImageGlass.Plugins;


/// <summary>
/// Loads native (in-process) codec plugins, validates their ABI surface,
/// and produces <see cref="NativeCodecProxy"/> instances ready to register in the codec registry.
/// </summary>
public sealed unsafe class PluginRegistry : PhDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, NativePlugin> _plugins = new(StringComparer.Ordinal);

    private static int DecodeMajor(int abiVersion) => abiVersion / 1_000_000;

    /// <summary>
    /// Subfolder under <c>_plugins</c> where a reinstall stashes the previous install for later reaping.
    /// </summary>
    internal const string TRASH_DIR_NAME = "_trash";

    // Derived from real field offsets: counting fields gets it wrong (a leading int pads to 8).
    private static readonly int MIN_CODEC_API_SIZE = MeasureCodecApi();
    private static readonly int MIN_CAPABILITY_SIZE = MeasureCapabilityDecodeFields();
    private static readonly int CAPABILITY_ENCODE_FIELDS_END = MeasureCapabilityEncodeFields();


    /// <summary>
    /// Offset past <c>FreePixelBuffer</c>: the smallest table carrying the required decode members.
    /// </summary>
    private static int MeasureCodecApi()
    {
        IGCodecApi probe = default;
        return (int)((byte*)&probe.FreePixelBuffer - (byte*)&probe) + sizeof(nint);
    }


    /// <summary>
    /// Offset past <c>DecodeExtensions</c>: the smallest capability carrying the decode fields.
    /// </summary>
    private static int MeasureCapabilityDecodeFields()
    {
        IGCodecCapability probe = default;
        return (int)((byte*)&probe.DecodeExtensions - (byte*)&probe) + sizeof(nint);
    }


    /// <summary>
    /// Offset just past <c>EncodeExtensions</c>; below this the encode fields are absent.
    /// </summary>
    private static int MeasureCapabilityEncodeFields()
    {
        IGCodecCapability probe = default;
        return (int)((byte*)&probe.EncodeExtensions - (byte*)&probe) + sizeof(nint);
    }


    /// <summary>
    /// Validates a manifest <c>Executable</c> and resolves it to a full path inside
    /// <paramref name="pluginDir"/>. Rejects absolute paths, directory separators,
    /// <c>..</c> traversal, escapes outside the plugin folder, and any file whose
    /// extension is not the platform's native shared-library extension.
    /// </summary>
    internal static bool TryResolvePluginLibraryPath(string executable, string pluginDir, out string libraryPath)
    {
        libraryPath = string.Empty;

        // Must be a bare filename: no rooting, no directory separators, no traversal.
        if (Path.IsPathRooted(executable)) return false;
        if (executable.Contains('/') || executable.Contains('\\')) return false;
        if (executable.Contains("..")) return false;

        // Must carry the platform's native shared-library extension.
        var expectedExt = OperatingSystem.IsWindows() ? ".dll"
            : OperatingSystem.IsMacOS() ? ".dylib"
            : ".so";
        if (!Path.GetExtension(executable).Equals(expectedExt, StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = Path.Combine(pluginDir, executable);

        // Belt-and-suspenders: the resolved path must stay inside the plugin folder.
        if (!BHelper.IsPathContainedIn(candidate, pluginDir)) return false;

        libraryPath = candidate;
        return true;
    }


    /// <summary>
    /// Tracks per-plugin crash markers and session-disabled state.
    /// Owned by this registry so that quarantine policy and the loaded plugin
    /// table share the same lifetime.
    /// </summary>
    public PluginFailureManager FailureManager { get; } = new();


    /// <summary>
    /// Loads the native plugin and probes its codecs. Returns null on any failure.
    /// </summary>
    internal NativePlugin? LoadAndProbe(PluginManifest manifest, string pluginDir)
    {
        if (manifest.Kind != IGPluginKind.Codec) return null;
        if (string.IsNullOrEmpty(manifest.Executable))
        {
            Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' has no Executable; skipping.");
            return null;
        }

        if (FailureManager.IsQuarantined(manifest.Id))
        {
            Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' is quarantined; skipping.");
            return null;
        }

        // Executable must be a plain relative filename inside the plugin folder
        // with the platform's native-library extension; reject absolute paths and traversal.
        if (!TryResolvePluginLibraryPath(manifest.Executable, pluginDir, out var libraryPath))
        {
            Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' has an unsafe or invalid Executable '{manifest.Executable}'; skipping.");
            return null;
        }
        if (!File.Exists(libraryPath))
        {
            Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' library not found: {libraryPath}");
            return null;
        }

        // Crash-loop guard: a leftover "loading" breadcrumb means a previous attempt to load
        // this plugin hard-crashed the process (uncatchable native fault). Quarantine it instead
        // of trying again and crashing on every launch.
        if (FailureManager.HasLoadingBreadcrumb(manifest.Id))
        {
            FailureManager.ClearLoadingBreadcrumb(manifest.Id);
            FailureManager.Quarantine(manifest.Id, "hard crash during a previous load");
            Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' crashed on a previous load; quarantined.");
            return null;
        }

        // Trust gate: never execute a plugin the user has not explicitly enabled and whose pinned
        // SHA-256 still matches the on-disk library (defends against a swapped binary).
        if (!PluginTrustPolicy.IsTrusted(manifest.Id, libraryPath))
        {
            Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' is not enabled/trusted (or its file changed); skipping.");
            return null;
        }

        nint libHandle = 0;
        IGPluginApi* pluginApi = null;

        // Write a breadcrumb before touching native code; it is cleared in the finally on any
        // managed return/exception. Only a hard native crash leaves it behind for the guard above.
        FailureManager.SetLoadingBreadcrumb(manifest.Id);
        try
        {
            // 1. Load the library
            libHandle = NativeLibrary.Load(libraryPath);

            // 2. Resolve the entry point
            if (!NativeLibrary.TryGetExport(libHandle, IGNativeAbi.ENTRY_POINT_NAME, out var entryAddr))
            {
                Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' missing export '{IGNativeAbi.ENTRY_POINT_NAME}'.");
                NativeLibrary.Free(libHandle);
                return null;
            }
            var entry = (delegate* unmanaged[Cdecl]<int, IGHostApi*, IGPluginApi*>)entryAddr;

            // 3. Negotiate ABI
            var hostApi = PluginHostApiTable.Get();
            try
            {
                pluginApi = entry(IGNativeAbi.IG_PLUGIN_ABI_VERSION, hostApi);
            }
            catch (Exception ex)
            {
                FailureManager.Quarantine(manifest.Id, $"entry point threw: {ex.Message}");
                NativeLibrary.Free(libHandle);
                return null;
            }
            if (pluginApi == null)
            {
                Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' entry point returned null (rejected).");
                NativeLibrary.Free(libHandle);
                return null;
            }

            // 4. Validate plugin ABI
            if (pluginApi->StructSize != sizeof(IGPluginApi))
            {
                Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' StructSize mismatch (got {pluginApi->StructSize}, expected {sizeof(IGPluginApi)}).");
                NativeLibrary.Free(libHandle);
                return null;
            }
            if (DecodeMajor(pluginApi->AbiVersion) != IGNativeAbi.IG_PLUGIN_ABI_MAJOR)
            {
                Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' ABI major mismatch (plugin={pluginApi->AbiVersion}, host={IGNativeAbi.IG_PLUGIN_ABI_VERSION}).");
                NativeLibrary.Free(libHandle);
                return null;
            }

            // 5. Optional initialize
            if (pluginApi->Initialize != null)
            {
                IGStatus initStatus;
                try { initStatus = pluginApi->Initialize(); }
                catch (Exception ex)
                {
                    FailureManager.Quarantine(manifest.Id, $"Initialize threw: {ex.Message}");
                    NativeLibrary.Free(libHandle);
                    return null;
                }
                if (initStatus != IGStatus.OK)
                {
                    Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' Initialize returned {initStatus}.");
                    NativeLibrary.Free(libHandle);
                    return null;
                }
            }

            var handle = new NativePlugin(manifest.Id, libraryPath, libHandle, pluginApi);

            // 6. Enumerate codecs
            var codecCount = pluginApi->Info.CodecCount;
            if (codecCount < 0 || codecCount > 1024)
            {
                Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' reported invalid CodecCount={codecCount} (struct layout mismatch?).");
                FailureManager.Quarantine(manifest.Id, $"invalid CodecCount={codecCount} (likely IGPluginInfo struct layout mismatch).");
                NativeLibrary.Free(libHandle);
                return null;
            }
            var capabilities = new List<CodecPluginCapability>(codecCount);
            for (var i = 0; i < codecCount; i++)
            {
                IGCodecApi* codecApi = null;
                IGStatus status;
                try { status = pluginApi->GetCodec(i, &codecApi); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' GetCodec[{i}] threw: {ex.Message}");
                    continue;
                }
                if (status != IGStatus.OK || codecApi == null) continue;

                // First member and the only stable offset, so validate before touching anything
                // else; a stale library has a function pointer here and gets refused.
                if (codecApi->StructSize < MIN_CODEC_API_SIZE || codecApi->StructSize > sizeof(IGCodecApi))
                {
                    Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' codec[{i}] reports StructSize="
                        + $"{codecApi->StructSize} (expected {MIN_CODEC_API_SIZE}..{sizeof(IGCodecApi)}); "
                        + "rebuild the plugin against the current SDK.");
                    continue;
                }
                if (codecApi->GetCapability == null) continue;


                // The plugin owns the capability struct and returns a pointer to it.
                IGCodecCapability* cap = null;
                try { status = codecApi->GetCapability(&cap); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' codec[{i}].GetCapability threw: {ex.Message}");
                    continue;
                }
                if (status != IGStatus.OK || cap == null) continue;

                if (cap->StructSize < MIN_CAPABILITY_SIZE || cap->StructSize > sizeof(IGCodecCapability))
                {
                    Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' codec[{i}] capability StructSize="
                        + $"{cap->StructSize} is unusable; skipping.");
                    continue;
                }

                var managed = MarshalCapability(codecApi, cap);
                if (!HasAnyCapability(managed))
                {
                    Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' codec[{i}] advertises nothing usable; skipping.");
                    continue;
                }

                capabilities.Add(managed);
                handle.Codecs.Add(new NativeCodecEntry((nint)codecApi, managed));
            }

            // A plugin whose codecs all failed to probe contributes nothing; unload it rather
            // than caching a loaded library forever.
            if (handle.Codecs.Count == 0)
            {
                Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' registered no codecs; unloading.");
                handle.Dispose();
                return null;
            }

            // 7. Register with loader
            lock (_lock)
            {
                if (_plugins.TryGetValue(manifest.Id, out var existing))
                {
                    existing.Dispose();
                }
                _plugins[manifest.Id] = handle;
            }

            return handle;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginRegistry] '{manifest.Id}' load failed: {ex.Message}");
            FailureManager.Quarantine(manifest.Id, $"load failed: {ex.Message}");

            try
            {
                if (libHandle != 0)
                {
                    NativeLibrary.Free(libHandle);
                }
            }
            catch { }

            return null;
        }
        finally
        {
            // Cleared on any graceful return/exception; a hard native crash skips this, leaving
            // the breadcrumb so the guard above quarantines the plugin on the next launch.
            FailureManager.ClearLoadingBreadcrumb(manifest.Id);
        }
    }


    /// <summary>
    /// Returns <c>true</c> if a plugin with the given id is currently loaded.
    /// </summary>
    public bool IsLoaded(string pluginId)
    {
        lock (_lock)
        {
            return _plugins.ContainsKey(pluginId);
        }
    }


    /// <summary>
    /// Returns the loaded plugin handle, or <c>null</c> when it is not loaded. Lets the host
    /// rebuild a plugin's proxies without reloading the library.
    /// </summary>
    internal NativePlugin? GetLoadedPlugin(string pluginId)
    {
        lock (_lock)
        {
            return _plugins.GetValueOrDefault(pluginId);
        }
    }


    /// <summary>
    /// Hot-unloads a loaded plugin (Shutdown + free library); returns <c>false</c> if not loaded.
    /// Outstanding buffers are gated by <c>PluginLiveToken</c>, so late releases safely no-op.
    /// </summary>
    public bool UnloadPlugin(string pluginId)
    {
        NativePlugin? handle;
        lock (_lock)
        {
            if (!_plugins.Remove(pluginId, out handle)) return false;
        }

        try { handle.Dispose(); } catch { }
        return true;
    }


    /// <summary>
    /// Builds <see cref="NativeCodecProxy"/> instances for every codec advertised by the plugin.
    /// </summary>
    internal IEnumerable<NativeCodecProxy> CreateProxies(NativePlugin handle)
    {
        var list = new List<NativeCodecProxy>(handle.Codecs.Count);
        foreach (var entry in handle.Codecs)
        {
            unsafe
            {
                list.Add(new NativeCodecProxy(handle, (IGCodecApi*)entry.CodecApiPtr, entry.Capability, FailureManager));
            }
        }
        return list;
    }


    /// <summary>
    /// Snapshots a capability, downgrading each flag whose entry points are missing so the host
    /// never sees an advertised-but-unwired capability.
    /// </summary>
    private static CodecPluginCapability MarshalCapability(IGCodecApi* codecApi, IGCodecCapability* cap)
    {
        var readable = Math.Min(cap->StructSize, sizeof(IGCodecCapability));

        // A plugin built against a smaller capability struct carries no encode fields at all;
        // reading them would be an over-read past its allocation.
        var hasEncodeFields = readable >= CAPABILITY_ENCODE_FIELDS_END;

        var decodeExts = MarshalExtensions(cap->DecodeExtensionCount, cap->DecodeExtensions, "DecodeExtensions");
        var encodeExts = hasEncodeFields
            ? MarshalExtensions(cap->EncodeExtensionCount, cap->EncodeExtensions, "EncodeExtensions")
            : [];

        var animDecode = cap->SupportsAnimationDecoding != 0
            && codecApi->GetAnimationInfo != null
            && codecApi->FreeAnimationInfo != null
            && codecApi->DecodeAnimationFrame != null;

        // An empty encode list must never read as "encodes everything".
        var staticEncode = hasEncodeFields
            && cap->SupportsStaticRasterEncoding != 0
            && codecApi->EncodeStaticRaster != null
            && encodeExts.Length > 0;

        var multiEncode = staticEncode
            && cap->SupportsMultiFrameEncoding != 0
            && codecApi->BeginEncodeMultiFrame != null
            && codecApi->EncodeFrame != null
            && codecApi->EndEncodeMultiFrame != null;

        return new CodecPluginCapability
        {
            CodecId = cap->CodecId.ToManaged(),
            CodecName = cap->CodecName.ToManaged(),
            MetadataPriority = cap->MetadataPriority,
            DecodePriority = cap->DecodePriority,
            EncodePriority = hasEncodeFields ? cap->EncodePriority : 0,
            DecodingExtensions = decodeExts,
            EncodingExtensions = encodeExts,
            SupportsMetadata = cap->SupportsMetadata != 0 && codecApi->LoadMetadata != null,
            SupportsColorProfiles = cap->SupportsColorProfiles != 0,
            SupportsStaticRasterDecoding = cap->SupportsStaticRasterDecoding != 0
                && codecApi->DecodeStaticRaster != null && codecApi->FreePixelBuffer != null,
            SupportsAnimationDecoding = animDecode,
            SupportsStaticRasterEncoding = staticEncode,
            SupportsMultiFrameEncoding = multiEncode,
        };
    }


    /// <summary>
    /// Copies a plugin-owned extension array. A negative count means a struct layout mismatch,
    /// so clamp and report rather than throwing from <see cref="List{T}"/>.
    /// </summary>
    private static string[] MarshalExtensions(int count, IGStringRef* extensions, string what)
    {
        if (count < 0)
        {
            Debug.WriteLine($"[PluginRegistry] capability reports negative count for {what} ({count}); clamping to 0.");
            count = 0;
        }
        if (extensions == null || count == 0) return [];

        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(extensions[i].ToManaged());
        }
        return [.. list];
    }


    /// <summary>
    /// Whether a codec advertises anything the host can use.
    /// </summary>
    private static bool HasAnyCapability(CodecPluginCapability cap)
        => cap.SupportsMetadata
        || cap.SupportsStaticRasterDecoding
        || cap.SupportsAnimationDecoding
        || cap.SupportsStaticRasterEncoding;


    protected override void OnDisposing()
    {
        lock (_lock)
        {
            foreach (var handle in _plugins.Values)
            {
                try { handle.Dispose(); } catch { }
            }
            _plugins.Clear();
        }
        base.OnDisposing();
    }


    /// <summary>
    /// Returns everything the loaded plugin's codecs declare they can read and write, ignoring the
    /// user's per-extension choices. Empty when not loaded: reading a codec's formats means running
    /// it, which requires trust. Reads the probe-time snapshot, so it is safe on the UI thread.
    /// </summary>
    public (string[] Decode, string[] Encode) GetDeclaredCodecExtensions(string pluginId)
    {
        var handle = GetLoadedPlugin(pluginId);
        if (handle is null) return ([], []);

        var decode = new List<string>();
        var encode = new List<string>();
        var decodeSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var encodeSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in handle.Codecs)
        {
            Collect(entry.Capability.DecodingExtensions, decodeSeen, decode);
            Collect(entry.Capability.EncodingExtensions, encodeSeen, encode);
        }

        return ([.. decode], [.. encode]);

        static void Collect(string[] source, HashSet<string> seen, List<string> target)
        {
            foreach (var ext in source)
            {
                var normalized = PluginTrustPolicy.NormalizeExtension(ext);
                if (normalized.Length > 1 && seen.Add(normalized)) target.Add(normalized);
            }
        }
    }


    /// <summary>
    /// Scans the given directory for native plugin manifests
    /// (<c>igplugin.json</c>) and returns one entry per valid manifest found.
    /// Does NOT load any libraries; manifest deserialization only.
    /// </summary>
    public static List<(PluginManifest Manifest, string PluginDir)> DiscoverManifests(string pluginsDirectory)
    {
        var results = new List<(PluginManifest, string)>();
        if (!Directory.Exists(pluginsDirectory)) return results;

        foreach (var dir in Directory.EnumerateDirectories(pluginsDirectory))
        {
            // skip the reinstall stash folder
            if (string.Equals(Path.GetFileName(dir), TRASH_DIR_NAME, StringComparison.Ordinal)) continue;

            var manifestPath = Path.Combine(dir, PluginManifest.FILE_NAME);
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = System.Text.Json.JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginManifest);
                if (manifest is not null && !string.IsNullOrEmpty(manifest.Id))
                {
                    results.Add((manifest, dir));
                }
            }
            catch
            {
                // skip malformed manifests
            }
        }

        return results;
    }


    /// <summary>
    /// Best-effort delete of stashed previous installs; skips still-locked folders. Call before any load.
    /// </summary>
    public static void CleanupTrashDirs(string pluginsDir)
    {
        var trashRoot = Path.Combine(pluginsDir, TRASH_DIR_NAME);
        if (!Directory.Exists(trashRoot)) return;

        foreach (var dir in Directory.GetDirectories(trashRoot))
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
        try { Directory.Delete(trashRoot, recursive: false); } catch { } // drop the root once empty
    }

}

