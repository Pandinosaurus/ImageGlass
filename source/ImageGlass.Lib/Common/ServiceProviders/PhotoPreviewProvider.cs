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


        // 2. slow path: use ImageMagick for unsupported formats
        if (imgPreview.IsDisposed())
        {
            using var imgM = await MagickCodec.QuickDecodeAsync(meta.FilePath, maxSize, maxSize, token: token);
            imgPreview = SkiaCodec.FromMagick(imgM, meta.SkiaColorSpace);
        }


        // 2b. slowest path: decode through the codec registry so that custom/plugin
        //     codecs can supply a thumbnail for formats neither SkiaSharp nor
        //     ImageMagick can decode on their own.
        if (imgPreview.IsDisposed())
        {
            imgPreview = await DecodeViaCodecRegistryAsync(meta, maxSize, token);
        }


        // 3. resize if needed
        if (minSize > 0 && (imgPreview?.Width > maxSize || imgPreview?.Height > maxSize))
        {
            var resizedBmpPreview = await SkiaCodec.ResizeAsync(imgPreview, minSize, token: token);
            imgPreview?.Dispose();
            imgPreview = SKImage.FromBitmap(resizedBmpPreview);
        }


        if (imgPreview.IsDisposed()) imgPreview = null;
        return imgPreview;
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
