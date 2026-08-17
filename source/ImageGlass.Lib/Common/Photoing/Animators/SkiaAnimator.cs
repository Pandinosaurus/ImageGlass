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
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using Avalonia.Threading;
using ImageGlass.Common.Extensions;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ImageGlass.Common.Photoing;

/// <summary>
/// A SkiaSharp-based animator that decodes and composites frames on demand.
/// Optimized for low memory usage by maintaining a single composite buffer
/// instead of caching all frames.
/// </summary>
public class SkiaAnimator : AnimatorImpl
{
    private const int MAX_CACHE_COUNT = 5;

    private readonly SKCodec _codec;
    private readonly SKImage?[] _frameCache;
    private readonly Queue<uint> _cachedFramesQueue = new();
    private readonly Lock _syncLock = new();


    /// <summary>
    /// The canvas where frames are composed. 
    /// Keeps the current visual state of the animation.
    /// </summary>
    private SKBitmap? _compositeBitmap;

    /// <summary>
    /// Back buffer used ONLY when a frame requires <see cref="SKCodecAnimationDisposalMethod.RestorePrevious"/>.
    /// Instantiated lazily to save memory.
    /// </summary>
    private SKBitmap? _backupBitmap;

    private int _lastRenderedFrameIndex = -1;
    private DispatcherTimer _timer;


    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaAnimator"/> class.
    /// </summary>
    public SkiaAnimator(SKCodec? srcCodec, SKCodecFrameInfo[] frames) : base(frames)
    {
        _codec = srcCodec ?? throw new ArgumentNullException(nameof(srcCodec));
        _frameCache = new SKImage[frames.Length];


        // Use DispatcherTimer to integrate with Avalonia's loop.
        // We set a high resolution (16ms ~ 60fps) to poll the stopwatch in the base class.
        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher.UIThread);
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Timer_Tick;
    }


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override void OnDisposing()
    {
        base.OnDisposing();

        lock (_syncLock)
        {
            StopTimer();
            _timer.Tick -= Timer_Tick;

            _compositeBitmap?.Dispose();
            _compositeBitmap = null;

            _backupBitmap?.Dispose();
            _backupBitmap = null;

            // Dispose caches
            for (int i = 0; i < _frameCache.Length; i++)
            {
                _frameCache[i]?.Dispose();
                _frameCache[i] = null;
            }
            _cachedFramesQueue.Clear();
        }
    }


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override void StartTimer()
    {
        _timer.Start();
    }


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override void StopTimer()
    {
        _timer.Stop();
    }


    private void Timer_Tick(object? sender, EventArgs e)
    {
        OnTimerTicked();
    }


    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public override SKImage? GetRenderedFrameBitmap(uint frameIndex)
    {
        lock (_syncLock)
        {
            if (IsDisposed || _codec is null) return null;

            // 1. Return cached result if available
            var cached = _frameCache[frameIndex];
            if (!cached.IsDisposed()) return cached;

            // Clear stale entry (disposed externally by SKImageRef)
            _frameCache[frameIndex] = null;


            // 2. Initialize Composite Bitmap if needed (Lazy init)
            if (_compositeBitmap is null)
            {
                var info = _codec.Info;
                // For high-bit-depth sources (HDR AVIF, animated WebP with wide gamut),
                // preserve the native color type. Otherwise use Bgra8888 for best
                // compatibility with Avalonia/Windows. Premul is faster for composition.
                var compositeType = SkiaCodec.IsHighBitDepthColorType(info.ColorType)
                    ? info.ColorType
                    : SKColorType.Bgra8888;
                _compositeBitmap = new SKBitmap(info.Width, info.Height, compositeType, SKAlphaType.Premul);
                _lastRenderedFrameIndex = -1;
            }


            // 3. Handle Seeking: If we are not strictly sequential, we must reset and replay.
            // Optimization: If moving from -1 to 0, or N to N+1, it is sequential.
            // If frameIndex == _lastRenderedFrameIndex, we just return the current state (idempotent).
            if (frameIndex < _lastRenderedFrameIndex || frameIndex > _lastRenderedFrameIndex + 1)
            {
                // Reset composition to restart
                _compositeBitmap.Erase(SKColors.Transparent);
                _lastRenderedFrameIndex = -1;
            }


            // 4. Sequential Catch-up Loop
            // Renders all frames from the last rendered point up to the requested frame.
            // This ensures disposal methods and composition are applied correctly.
            for (int i = _lastRenderedFrameIndex + 1; i <= frameIndex; i++)
            {
                RenderSingleFrame(i);
            }


            // 5. Return Snapshot
            // SKImage.FromBitmap creates a copy if the bitmap is mutable.
            // This copy is ESSENTIAL for thread safety, preventing the UI from reading 
            // the bitmap while the animator modifies it for the next frame.
            var frameImage = SKImage.FromBitmap(_compositeBitmap);
            _frameCache[frameIndex] = frameImage;


            // 6. Manage Cache: FIFO Eviction
            if (!_cachedFramesQueue.Contains(frameIndex))
            {
                _cachedFramesQueue.Enqueue(frameIndex);
            }

            while (_cachedFramesQueue.Count > MAX_CACHE_COUNT)
            {
                var evictedIndex = _cachedFramesQueue.Dequeue();

                // Ensure we don't dispose the one we just created (safety check)
                if (evictedIndex != frameIndex)
                {
                    _frameCache[evictedIndex]?.Dispose();
                    _frameCache[evictedIndex] = null;
                }
            }

            return frameImage;
        }
    }


    /// <summary>
    /// Renders a single frame into the _compositeBitmap.
    /// </summary>
    private void RenderSingleFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _frames.Length) return;
        if (_compositeBitmap is null) return;


        // 1. Dispose the PREVIOUS frame:
        // Apply the disposal method defined in the PREVIOUS frame's metadata.
        ProcessPreviousFrameDisposal(frameIndex, _compositeBitmap, _backupBitmap);


        // 2. Prepare for CURRENT frame disposal requirements:
        // If the CURRENT frame requires "RestorePrevious" later, we must save the 
        // current state (Composite before this frame is drawn) now.
        ProcessCurrentFrameDisposal(frameIndex, _compositeBitmap, _backupBitmap);


        // 3. Draw the CURRENT Frame:
        // Decode the frame pixels onto the composite bitmap.
        DrawCurrentFrame(frameIndex, _compositeBitmap);


        _lastRenderedFrameIndex = frameIndex;
    }


    /// <summary>
    /// Dispose the PREVIOUS frame:
    /// Apply the disposal method defined in the PREVIOUS frame's metadata.
    /// </summary>
    private void ProcessPreviousFrameDisposal(int frameIndex, SKBitmap bmpComposite, SKBitmap? bmpBackup)
    {
        if (frameIndex <= 0) return;

        var prevIndex = frameIndex - 1;
        var prevMeta = _frames[prevIndex];

        // NOTE: Accurate GIF rendering requires the specific FrameRect of the previous frame.
        // Standard SkiaSharp 2.88 SKCodecFrameInfo may not expose FrameRect.
        // We default to full size here for safety, or assume you have a helper/extension.
        var prevRect = new SKRectI(0, 0, bmpComposite.Width, bmpComposite.Height);

        // Clip rect to bitmap bounds
        var safePrevRect = SKRectI.Intersect(new SKRectI(0, 0, bmpComposite.Width, bmpComposite.Height), prevRect);

        if (prevMeta.DisposalMethod == SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
        {
            // Clear the previous frame's area to Transparent
            if (!safePrevRect.IsEmpty)
            {
                using var canvas = new SKCanvas(bmpComposite);
                using var paint = new SKPaint { BlendMode = SKBlendMode.Clear };
                canvas.DrawRect(safePrevRect, paint);
            }
        }
        else if (prevMeta.DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious)
        {
            // Restore the area from the backup buffer
            if (bmpBackup is not null)
            {
                using var canvas = new SKCanvas(bmpComposite);
                // Only redraw the specific area that needs restoring
                var srcRect = safePrevRect;
                var dstRect = safePrevRect;
                canvas.DrawBitmap(bmpBackup, srcRect, dstRect);
            }
        }
        else
        {
            // Preserve the previous frame (draw over it)
        }

    }


    /// <summary>
    /// Prepare for CURRENT frame disposal requirements:
    /// If the CURRENT frame requires "RestorePrevious" later, we must save the 
    /// current state (Composite before this frame is drawn) now.
    /// </summary>
    private void ProcessCurrentFrameDisposal(int frameIndex, SKBitmap bmpComposite, SKBitmap? bmpBackup)
    {
        var curMeta = _frames[frameIndex];

        if (curMeta.DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious)
        {
            // Allocate or re-allocate backup buffer if size mismatch
            if (bmpBackup is null || bmpBackup.Info.Size != bmpComposite.Info.Size)
            {
                bmpBackup?.Dispose();
                bmpBackup = bmpComposite.Copy(); // Full copy
            }
            else
            {
                // Copy current composite state to backup
                // Using DrawBitmap is often faster/safer than raw memory copy for managed Skia wrappers
                using var canvas = new SKCanvas(bmpBackup);
                using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
                canvas.DrawBitmap(bmpComposite, 0, 0, paint);
            }
        }

    }


    /// <summary>
    /// Draw the CURRENT Frame:
    /// Decode the frame pixels onto the composite bitmap.
    /// </summary>
    private void DrawCurrentFrame(int frameIndex, SKBitmap bmpComposite)
    {
        // Assumption: We decode full frame size if FrameRect is unavailable.
        var curMeta = _frames[frameIndex];
        var curRect = new SKRectI(0, 0, bmpComposite.Width, bmpComposite.Height);

        // OPTIMIZATION: Passing priorFrame tells the codec we are drawing on top of an existing state.
        // This dramatically speeds up decoding for formats that support inter-frame compression (GIF/WebP).
        var frameOptions = new SKCodecOptions(frameIndex, curMeta.RequiredFrame);

        // Zero-copy attempt: Write directly to bitmap memory
        // We calculate the memory address for the sub-rectangle (if curRect is used)
        var info = bmpComposite.Info;

        if (curRect.Left >= 0 && curRect.Top >= 0 && curRect.Right <= info.Width && curRect.Bottom <= info.Height)
        {
            var pixels = bmpComposite.GetPixels();
            var rowBytes = bmpComposite.RowBytes;

            // Offset logic: Top * RowBytes + Left * BytesPerPixel
            var offset = curRect.Top * rowBytes + curRect.Left * info.BytesPerPixel;
            var ptr = new IntPtr(pixels.ToInt64() + offset);

            // Decode directly into the composite bitmap
            var frameInfo = new SKImageInfo(curRect.Width, curRect.Height, info.ColorType, info.AlphaType);
            _ = _codec.GetPixels(frameInfo, ptr, rowBytes, frameOptions);
        }
        else
        {
            // Fallback for safety or complex bounds: Decode to temp and draw
            using var bmpFrame = new SKBitmap(curRect.Width, curRect.Height, info.ColorType, info.AlphaType);
            var result = _codec.GetPixels(bmpFrame.Info, bmpFrame.GetPixels(), bmpFrame.RowBytes, frameOptions);

            if (result == SKCodecResult.Success)
            {
                using var canvas = new SKCanvas(bmpComposite);
                canvas.DrawBitmap(bmpFrame, curRect.Left, curRect.Top);
            }
        }
    }


}