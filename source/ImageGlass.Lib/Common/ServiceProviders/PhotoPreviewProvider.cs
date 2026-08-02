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
using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common.ServiceProviders;

public class PhotoPreviewProvider : IPhotoPreviewProvider
{
    /// <summary>
    /// Fraction of the requested size a preview must reach to count as sharp enough.
    /// Callers request 2x the size they display (supersampling margin), so half of it is still
    /// drawn without any upscaling; below that the preview visibly blurs.
    /// </summary>
    private const double MIN_PREVIEW_SIZE_RATIO = 0.5;


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public virtual async Task<SKImage?> GetPreviewAsync(PhotoMetadata meta, double? minHeight,
        CancellationToken token = default)
    {
        // 1. fast path: native scaled decode via SkiaSharp
        var size = (int)(minHeight ?? double.MinValue);
        var imgPreview = await Task.Run(() => SkiaCodec.LoadThumbnail(meta.FilePath, size), token)
            .ConfigureAwait(false);


        // 2. try embedded EXIF preview
        if (imgPreview.IsDisposed())
        {
            using var thumbM = meta.GetEmbeddedPreview();
            if (thumbM is not null && thumbM.Height >= minHeight)
            {
                imgPreview = SkiaCodec.FromMagick(thumbM, meta.SkiaColorSpace);
            }
        }


        // 3. process preview
        if (TryProcessImage(imgPreview, meta, out var imgProcessed))
        {
            imgPreview?.Dispose();
            imgPreview = imgProcessed;
        }


        if (imgPreview.IsDisposed()) imgPreview = null;
        return imgPreview;
    }


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public virtual async Task<SKImage?> GetThumbnailAsync(PhotoMetadata meta, double minHeight,
        CancellationToken token = default)
    {
        var minSize = (int)minHeight;
        var maxSize = minSize * 2;

        // 1. fast path: try to get the quick preview
        var imgPreview = await GetPreviewAsync(meta, minSize, token);
        var isPreviewLargeEnough = IsPreviewLargeEnough(imgPreview, meta, minSize);


        // 2. slow path: use ImageMagick for unsupported formats, and for previews that came back
        // smaller than the gallery cell (they would be scaled up and look blurry)
        if (!isPreviewLargeEnough)
        {
            using var imgM = await MagickCodec.QuickDecodeAsync(meta.FilePath, maxSize, maxSize, token: token);
            var imgMagick = SkiaCodec.FromMagick(imgM, meta.SkiaColorSpace);

            // an undersized preview is still better than nothing if Magick did no better
            _ = KeepLarger(ref imgPreview, imgMagick);
        }


        // 2b. slowest path: decode through the codec registry so that custom/plugin
        //     codecs can supply a thumbnail for formats neither SkiaSharp nor
        //     ImageMagick can decode on their own.
        if (imgPreview.IsDisposed())
        {
            imgPreview = await DecodeViaCodecRegistryAsync(meta, maxSize, token);
        }


        // 3. shrink anything bigger than requested. The Shell hands back whole cache tiers and the
        // Magick/codec fallbacks decode at maxSize for headroom, so results are routinely oversized;
        // keeping them would cost several MB of gallery memory per photo. Skia-only: a Magick
        // round-trip here costs ~170ms per thumbnail and dominates gallery load time.
        if (minSize > 0 && (imgPreview?.Width > minSize || imgPreview?.Height > minSize))
        {
            var imgScaled = await Task.Run(() => SkiaCodec.ScaleDown(imgPreview, minSize), token)
                .ConfigureAwait(false);
            if (!imgScaled.IsDisposed())
            {
                imgPreview?.Dispose();
                imgPreview = imgScaled;
            }
        }


        if (imgPreview.IsDisposed()) imgPreview = null;
        return imgPreview;
    }


    /// <summary>
    /// Checks whether <paramref name="img"/> can fill a <paramref name="requestedSize"/> box
    /// without being scaled up. A source smaller than the request caps what any provider is able
    /// to return, so the source's own size becomes the target in that case.
    /// </summary>
    public static bool IsPreviewLargeEnough(SKImage? img, PhotoMetadata meta, double requestedSize)
    {
        if (img.IsDisposed()) return false;
        if (requestedSize <= 0) return true;

        // the supersampling margin is optional, the source size is not: when the source is
        // smaller than the request, its full size is the sharpest result obtainable
        var wantedSize = requestedSize * MIN_PREVIEW_SIZE_RATIO;
        var srcLongestSide = (double)Math.Max(meta.Width, meta.Height);
        if (srcLongestSide > 0) wantedSize = Math.Min(wantedSize, srcLongestSide);

        var imgLongestSide = (double)Math.Max(img.Width, img.Height);
        return imgLongestSide >= wantedSize;
    }


    /// <summary>
    /// Keeps whichever of <paramref name="current"/> and <paramref name="candidate"/> has the
    /// larger longest side and disposes the other. Returns <c>true</c> when
    /// <paramref name="candidate"/> won.
    /// </summary>
    protected static bool KeepLarger(ref SKImage? current, SKImage? candidate)
    {
        if (candidate.IsDisposed()) return false;

        var currentSide = GetLongestSide(current);
        var candidateSide = GetLongestSide(candidate);

        if (candidateSide <= currentSide)
        {
            candidate.Dispose();
            return false;
        }

        current?.Dispose();
        current = candidate;
        return true;
    }


    /// <summary>
    /// Gets the longest side of the image, or <c>0</c> if it is null or disposed.
    /// </summary>
    private static int GetLongestSide(SKImage? img)
    {
        if (img.IsDisposed()) return 0;

        return Math.Max(img.Width, img.Height);
    }


    /// <summary>
    /// Decodes the image through the codec registry and returns its raster frame.
    /// This lets custom/plugin codecs produce a thumbnail for formats that the
    /// built-in SkiaSharp/ImageMagick paths cannot decode. Orientation and color
    /// management are intentionally left to the codec (as in the full-image decode
    /// path), so no further processing is applied here.
    /// </summary>
    protected static async Task<SKImage?> DecodeViaCodecRegistryAsync(PhotoMetadata meta,
        int maxSize, CancellationToken token)
    {
        var context = new CodecSelectionContext
        {
            EnableVectorRenderer = Core.Config.EnableVectorRenderer,
            IsDestColorProfileSupported = Core.IsDestColorProfileSupported,
        };

        var codec = Core.CodecRegistry.SelectDecodeCodec(meta, context);
        if (codec is null) return null;

        var options = new PhotoReadOptions
        {
            Width = (uint)Math.Max(0, maxSize),
            Height = (uint)Math.Max(0, maxSize),
        };

        using var result = await codec.DecodeAsync(meta, options, context, token).ConfigureAwait(false);

        // detach the raster frame so disposing the result does not dispose it
        var imgFrame = result.SingleFrame;
        result.SingleFrame = null;
        return imgFrame;
    }


    /// <summary>
    /// Processes the preview image by applying orientation and color management adjustments.
    /// </summary>
    protected static bool TryProcessImage(SKImage? imgPreview, PhotoMetadata meta, out SKImage? output)
    {
        output = null;
        if (imgPreview.IsDisposed()) return false;


        // 1. apply orientation
        if (SkiaCodec.TryApplyOrientation(imgPreview, meta.Orientation, out var imgOriented))
        {
            output?.Dispose();
            output = imgOriented;
        }


        // 2. apply color management
        if (SkiaCodec.TryApplyColorSpace(output ?? imgPreview, Core.DestColorProfile, out var imgFrameColored))
        {
            output?.Dispose();
            output = imgFrameColored;
        }

        return true;
    }

}
