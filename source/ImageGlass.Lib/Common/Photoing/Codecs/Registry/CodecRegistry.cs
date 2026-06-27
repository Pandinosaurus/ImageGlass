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
using ImageGlass.Common.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ImageGlass.Common.Photoing;


/// <summary>
/// A read-only snapshot of a registered codec (for diagnostics / settings UI).
/// </summary>
public sealed record CodecInfo(string CodecId, string CodecName, int DecodePriority,
    IReadOnlyList<string> SupportedExtensions, bool IsPlugin);


/// <summary>
/// Provides deterministic selection and lifetime management for registered photo codecs.
/// </summary>
public sealed class CodecRegistry : PhDisposable
{
    private readonly Lock _lock = new();
    private readonly List<ICodec> _codecs = [];
    private readonly List<ICodec> _metadataCodecs = [];
    private readonly List<ICodec> _decodeCodecs = [];

    // Per-extension fast-path caches. The first lookup for an extension walks the full
    // priority-sorted list; subsequent lookups try the remembered winner first and only
    // fall back to a full scan if it can no longer handle the file (e.g. context changed,
    // file content differs). Caches are cleared whenever a new codec is registered.
    private readonly ConcurrentDictionary<string, ICodec> _metadataCodecByExt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ICodec> _decodeCodecByExt = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// Initializes a new instance of <see cref="CodecRegistry"/> with the built-in codecs.
    /// </summary>
    public CodecRegistry()
    {
        Register(new SvgCodecAdapter());
        Register(new SkiaCodecAdapter());
        Register(new MagickCodecAdapter());
    }


    /// <summary>
    /// Registers a codec in the registry. All codecs (built-in or plugin) are treated equally
    /// and ordered purely by <see cref="ICodec.MetadataPriority"/> / <see cref="ICodec.DecodePriority"/>;
    /// a higher-priority plugin codec can therefore override a built-in for the same file.
    /// </summary>
    public void Register(ICodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        lock (_lock)
        {
            if (_codecs.Exists(c => c.CodecId.Equals(codec.CodecId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Codec '{codec.CodecId}' is already registered.");
            }

            _codecs.Add(codec);
            _metadataCodecs.Add(codec);
            _decodeCodecs.Add(codec);

            _metadataCodecs.Sort(static (left, right) => right.MetadataPriority.CompareTo(left.MetadataPriority));
            _decodeCodecs.Sort(static (left, right) => right.DecodePriority.CompareTo(left.DecodePriority));

            // Invalidate fast-path caches: the new codec may outrank the cached winner.
            _metadataCodecByExt.Clear();
            _decodeCodecByExt.Clear();
        }
    }


    /// <summary>
    /// Selects the first registered codec that can load metadata for the specified file.
    /// </summary>
    public ICodec? SelectMetadataCodec(string filePath)
    {
        var ext = string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetExtension(filePath);

        lock (_lock)
        {
            return SelectWithCache(_metadataCodecByExt, _metadataCodecs, ext,
                c => c.CanLoadMetadata(filePath), nameof(SelectMetadataCodec));
        }
    }


    /// <summary>
    /// Selects the first registered codec that can decode the specified metadata under the current runtime context.
    /// </summary>
    public ICodec? SelectDecodeCodec(PhotoMetadata metadata, CodecSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(context);

        var ext = metadata.FileExtension ?? string.Empty;

        lock (_lock)
        {
            return SelectWithCache(_decodeCodecByExt, _decodeCodecs, ext,
                c => c.CanDecode(metadata, context), nameof(SelectDecodeCodec));
        }
    }


    /// <summary>
    /// Returns a read-only snapshot of the registered codecs, ordered by decode priority
    /// (highest first). Informational only (for diagnostics / settings UI).
    /// </summary>
    public IReadOnlyList<CodecInfo> GetCodecInfos()
    {
        lock (_lock)
        {
            var list = new List<CodecInfo>(_decodeCodecs.Count);
            foreach (var c in _decodeCodecs)
            {
                var isBuiltIn = c is SvgCodecAdapter or SkiaCodecAdapter or MagickCodecAdapter;
                list.Add(new CodecInfo(c.CodecId, c.CodecName, c.DecodePriority,
                    c.SupportedExtensions, !isBuiltIn));
            }
            return list;
        }
    }


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override void OnDisposing()
    {
        lock (_lock)
        {
            foreach (var codec in _codecs)
            {
                codec.Dispose();
            }

            _codecs.Clear();
            _metadataCodecs.Clear();
            _decodeCodecs.Clear();
            _metadataCodecByExt.Clear();
            _decodeCodecByExt.Clear();
        }

        base.OnDisposing();
    }



    private static ICodec? SelectFirst(List<ICodec> orderedCodecs, Func<ICodec, bool> predicate, string opName)
    {
        foreach (var codec in orderedCodecs)
        {
            try { if (predicate(codec)) return codec; }
            catch (Exception ex) { Debug.WriteLine($"❌❌❌ {opName} ({codec.CodecId}): {ex.Message}"); }
        }
        return null;
    }


    /// <summary>
    /// Tries the cached codec for <paramref name="ext"/> first; on miss or stale entry,
    /// falls back to a full priority-ordered scan and updates the cache with the winner.
    /// </summary>
    private static ICodec? SelectWithCache(
        ConcurrentDictionary<string, ICodec> cache,
        List<ICodec> orderedCodecs,
        string ext,
        Func<ICodec, bool> predicate,
        string opName)
    {
        if (!string.IsNullOrEmpty(ext) && cache.TryGetValue(ext, out var cached))
        {
            try
            {
                if (predicate(cached)) return cached;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌ {opName} ({cached.CodecId}) [cached]: {ex.Message}");
            }
        }

        var codec = SelectFirst(orderedCodecs, predicate, opName);
        if (codec != null && !string.IsNullOrEmpty(ext))
        {
            cache[ext] = codec;
        }

        return codec;
    }

}