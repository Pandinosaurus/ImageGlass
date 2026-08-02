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
using ImageGlass.Common.Extensions;
using ImageGlass.Common.Photoing;
using ImageGlass.Common.Types;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ImageGlass.UI.Viewer;


/// <summary>
/// A tiled mipmap cache for rendering large images efficiently.
/// Tiles are extracted on-demand from the source image at the requested mip level
/// and cached with LRU eviction to bound memory usage.
/// <para>
/// At each mip level, a tile covers <c>TILE_SIZE &lt;&lt; mipLevel</c> source pixels
/// but always outputs a <c>TILE_SIZE × TILE_SIZE</c> image. This keeps the number
/// of visible tiles roughly constant (~12 for a 1920×1080 viewport) regardless of zoom.
/// </para>
/// For animated images, this cache should NOT be used.
/// </summary>
internal sealed class MipmapTileCache : PhDisposable
{
    public const int TILE_SIZE = 512;
    private const int MAX_CACHED_TILES = 100;
    private const int MAX_MIP_LEVEL = 6;

    /// <summary>
    /// Minimum total pixels to benefit from tiling.
    /// Images smaller than this use direct rendering.
    /// </summary>
    private const long MIN_PIXELS_FOR_TILING = 8192 * 8192;

    /// <summary>
    /// Thread synchronization lock protecting access to cache dictionaries and linked list.
    /// Used to ensure thread-safe tile insertion, eviction, and LRU promotion.
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    /// Maps (tileX, tileY, mipLevel) to cached SKImage tiles.
    /// Each tile is a 512×512 image extracted from the source at the given mip level.
    /// </summary>
    private readonly Dictionary<(int x, int y, int level), SKImage> _tiles = [];

    /// <summary>
    /// Maps (tileX, tileY, mipLevel) to LinkedListNode for O(1) LRU promotion.
    /// Without this, LRU promotion would require O(n) linked list traversal.
    /// </summary>
    private readonly Dictionary<(int x, int y, int level), LinkedListNode<(int x, int y, int level)>> _nodeMap = [];

    /// <summary>
    /// LinkedList maintaining LRU order of cached tiles (oldest at First, newest at Last).
    /// When a tile is accessed, its node is moved to the end (most recently used).
    /// When cache exceeds MAX_CACHED_TILES, tiles are evicted from the front.
    /// </summary>
    private readonly LinkedList<(int x, int y, int level)> _lruList = new();

    /// <summary>
    /// Reference to the full-resolution source image (SKImageRef).
    /// Acquired via lease pattern to support concurrent reads.
    /// </summary>
    private readonly SKImageRef _sourceRef;

    /// <summary>
    /// The color type of the source image, used to create tiles at matching bit depth.
    /// </summary>
    private readonly SKColorType _colorType;

    /// <summary>
    /// The color space of the source image, attached to tiles during extraction
    /// so that downscaling preserves encoded values without color-space conversion.
    /// </summary>
    private readonly SKColorSpace? _colorSpace;

    /// <summary>
    /// Maximum number of tiles to cache, scaled down for high-bit-depth formats
    /// to stay within a constant memory budget.
    /// </summary>
    private readonly int _maxCachedTiles;


    /// <summary>
    /// Gets the width of the source image.
    /// </summary>
    public int SourceWidth { get; }

    /// <summary>
    /// Gets the height of the source image.
    /// </summary>
    public int SourceHeight { get; }


    private MipmapTileCache(SKImageRef sourceRef, int width, int height,
        SKColorType colorType, SKColorSpace? colorSpace)
    {
        _sourceRef = sourceRef;
        _sourceRef.KeepAlive();
        SourceWidth = width;
        SourceHeight = height;
        _colorType = colorType;
        _colorSpace = colorSpace;

        // Scale max tiles inversely with bytes-per-pixel to keep a constant memory budget.
        // Budget baseline: MAX_CACHED_TILES tiles of Rgba8888 (4 bpp).
        var bpp = new SKImageInfo(1, 1, colorType).BytesPerPixel;
        _maxCachedTiles = bpp <= 4
            ? MAX_CACHED_TILES
            : Math.Max(10, MAX_CACHED_TILES * 4 / bpp);
    }



    #region Static Methods

    /// <summary>
    /// Creates a tile cache for the given source image, or returns <c>null</c>
    /// if the image is too small to benefit from tiling.
    /// </summary>
    public static MipmapTileCache? Create(SKImageRef? sourceRef)
    {
        var img = sourceRef?.Image;
        if (img is null || img.IsDisposed()) return null;

        var pixels = (long)img.Width * img.Height;
        if (pixels < MIN_PIXELS_FOR_TILING) return null;

        return new MipmapTileCache(sourceRef!, img.Width, img.Height,
            img.ColorType, img.ColorSpace);
    }


    /// <summary>
    /// Calculates the best mip level for a given zoom factor.
    /// At each transition (50%, 25%, 12.5%, …), the tile bitmap maps roughly 1:1
    /// to screen pixels, so quality is preserved without needing a bias.
    /// </summary>
    public static int GetMipLevel(double zoomFactor)
    {
        if (zoomFactor >= 1.0) return 0;
        var level = (int)Math.Log2(1.0 / zoomFactor);
        return Math.Clamp(level, 0, MAX_MIP_LEVEL);
    }


    /// <summary>
    /// Gets the source pixel coverage per tile at the given mip level.
    /// At mipLevel 0, each tile covers <c>TILE_SIZE</c> source pixels.
    /// At mipLevel N, each tile covers <c>TILE_SIZE &lt;&lt; N</c> source pixels.
    /// </summary>
    public static int GetSourceTileSize(int mipLevel)
    {
        return TILE_SIZE << mipLevel;
    }

    #endregion // Static Methods



    #region Instance Methods

    /// <summary>
    /// Gets a tile image, returning a cached version or extracting a new one.
    /// The returned <see cref="SKImage"/> is owned by the cache — do NOT dispose it.
    /// </summary>
    public SKImage? GetTile(int tileX, int tileY, int mipLevel)
    {
        if (IsDisposed) return null;

        var key = (tileX, tileY, mipLevel);

        lock (_lock)
        {
            if (_tiles.TryGetValue(key, out var cached))
            {
                // O(1) LRU promotion
                if (_nodeMap.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddLast(node);
                }
                return cached;
            }
        }

        // extract tile outside lock (heavy work, SKImage reads are thread-safe)
        SKImage? tile;
        try
        {
            tile = ExtractTile(tileX, tileY, mipLevel);
        }
        catch
        {
            return null;
        }

        if (tile is null) return null;

        lock (_lock)
        {
            // double-check: another thread may have inserted while we were extracting
            if (_tiles.TryGetValue(key, out var existing))
            {
                tile.Dispose();
                if (_nodeMap.TryGetValue(key, out var existingNode))
                {
                    _lruList.Remove(existingNode);
                    _lruList.AddLast(existingNode);
                }
                return existing;
            }

            _tiles[key] = tile;
            _nodeMap[key] = _lruList.AddLast(key);

            // LRU eviction
            while (_tiles.Count > _maxCachedTiles && _lruList.First is not null)
            {
                var oldest = _lruList.First.Value;
                _lruList.RemoveFirst();
                _nodeMap.Remove(oldest);

                if (_tiles.Remove(oldest, out var image))
                {
                    image.Dispose();
                }
            }
        }

        return tile;
    }


    /// <summary>
    /// Extracts a tile from the source image at the given mip level.
    /// </summary>
    private SKImage? ExtractTile(int tileX, int tileY, int mipLevel)
    {
        if (IsDisposed) return null;

        var sourceTileSize = GetSourceTileSize(mipLevel);

        // source region in original image coordinates
        var srcX = tileX * sourceTileSize;
        var srcY = tileY * sourceTileSize;
        var srcW = Math.Min(sourceTileSize, SourceWidth - srcX);
        var srcH = Math.Min(sourceTileSize, SourceHeight - srcY);

        if (srcX >= SourceWidth || srcY >= SourceHeight || srcW <= 0 || srcH <= 0)
            return null;

        // output tile dimensions (proportional to source coverage for edge tiles)
        var tileW = Math.Max(1, srcW * TILE_SIZE / sourceTileSize);
        var tileH = Math.Max(1, srcH * TILE_SIZE / sourceTileSize);

        return ExtractFromSource(srcX, srcY, srcW, srcH, tileW, tileH);
    }


    /// <summary>
    /// Extracts a tile by reading from the full-resolution source image.
    /// </summary>
    private SKImage? ExtractFromSource(int srcX, int srcY, int srcW, int srcH, int tileW, int tileH)
    {
        using var lease = _sourceRef.Acquire();
        var srcImage = lease?.Image;
        if (srcImage is null || srcImage.IsDisposed()) return null;

        // Use the source's color type and space so that high-bit-depth / HDR
        // data is preserved in tiles without unwanted color-space conversion.
        var info = new SKImageInfo(tileW, tileH, _colorType, SKAlphaType.Premul, _colorSpace);
        using var bitmap = new SKBitmap(info);

        using (var canvas = new SKCanvas(bitmap))
        {
            var sampling = SkiaCodec.ToSamplingOptions(ImageInterpolation.CubicMitchell);

            canvas.DrawImage(srcImage,
                new SKRect(srcX, srcY, srcX + srcW, srcY + srcH),
                new SKRect(0, 0, tileW, tileH), sampling);
        }

        // allow the image to share the bitmap's pixel data
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }


    protected override void OnDisposing()
    {
        base.OnDisposing();

        lock (_lock)
        {
            foreach (var tile in _tiles.Values) tile.Dispose();
            _tiles.Clear();
            _lruList.Clear();
            _nodeMap.Clear();
        }

        _sourceRef.RequestDispose();
    }


    #endregion // Instance Methods


}
