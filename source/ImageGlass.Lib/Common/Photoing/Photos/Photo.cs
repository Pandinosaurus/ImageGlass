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
using Avalonia.Media.Imaging;
using ImageGlass.Common.Extensions;
using ImageGlass.Common.Loggers;
using ImageGlass.Common.ServiceProviders;
using ImageGlass.Common.Types;
using ImageMagick;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common.Photoing;

public partial class Photo : PhDisposable
{
    // private properties
    private uint _width = 0;
    private uint _height = 0;
    private int _frameIndex = -1;

    private Task<PhotoMetadata>? _taskMetadata;
    private CancellationTokenSource? _cancelPhotoLoading;
    private readonly Lock _lock = new();
    private int _loadGeneration;

    // track pending tasks
    private ConcurrentDictionary<Guid, bool> _taskRefs = new();
    private CancellationTokenSource? _cancelThumbnailLoading;

    /// <summary>
    /// Prevents duplicate concurrent loads of the same photo's thumbnail.
    /// </summary>
    private readonly SemaphoreSlim _thumbnailLock = new(1, 1);

    /// <summary>
    /// Limits how many different Photo instances load thumbnails concurrently across the entire
    /// app. Scaled to the CPU so the first screen of gallery thumbnails isn't serialized through
    /// just a few slots (was a fixed 4).
    /// </summary>
    private static readonly int _thumbnailConcurrency = Math.Clamp(Environment.ProcessorCount - 1, 4, 16);
    private static readonly SemaphoreSlim _thumbnailThrottleLock = new(_thumbnailConcurrency, _thumbnailConcurrency);



    #region Public Propterties

    /// <summary>
    /// Gets the native bitmap,
    /// either <see cref="SKImage"/>, <see cref="AnimatorImpl"/>,
    /// or <see cref="SkiaVectorSource"/>.
    /// </summary>
    public IDisposable? Bitmap { get; private set; } = null;

    /// <summary>
    /// Gets the size of the photo.
    /// </summary>
    public Size Size => new Size(_width, _height);

    /// <summary>
    /// Gets the width of the photo.
    /// </summary>
    public uint Width => (uint)Size.Width;

    /// <summary>
    /// Gets the height of the photo.
    /// </summary>
    public uint Height => (uint)Size.Height;

    /// <summary>
    /// Gets the current frame index of this photo.
    /// </summary>
    public int FrameIndex => _frameIndex;

    /// <summary>
    /// Gets the loading state of the photo.
    /// </summary>
    public PhotoState State { get; set; } = PhotoState.None;

    /// <summary>
    /// Gets the codec ID used to decode the photo.
    /// </summary>
    public string CodecId
    {
        get; private set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    } = string.Empty;


    /// <summary>
    /// Gets, sets value indicating if the photo is current index.
    /// </summary>
    public bool IsCurrent
    {
        get; set
        {
            if (field == value) return;
            field = value;
            _ = OnPropertyChanged();
        }
    }


    /// <summary>
    /// Checks if this photo is a clipboard photo.
    /// </summary>
    public bool IsClipboard => string.IsNullOrEmpty(FilePath);


    /// <summary>
    /// Gets file path of the photo. E.g. <c>"C:\Album\My photo.png"</c>.
    /// </summary>
    public string FilePath
    {
        get => Metadata.FilePath; set
        {
            if (!Metadata.FilePath.Equals(value, StringComparison.Ordinal))
            {
                Metadata.FilePath = value;

                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(DirPath));
                OnPropertyChanged(nameof(Metadata));

                OnPropertyChanged(nameof(IsClipboard));
                OnPropertyChanged(nameof(Extension));
                OnPropertyChanged(nameof(FileTitle));
                OnPropertyChanged(nameof(GalleryFileTitle));
                OnPropertyChanged(nameof(GalleryFileExtension));
            }
        }
    }

    /// <summary>
    /// Gets original dir path. E.g: <c>"C:\Album"</c>.
    /// </summary>
    public string DirPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    /// <summary>
    /// Gets original file extension. E.g: <c>".png"</c>.
    /// </summary>
    public string Extension => Path.GetExtension(FilePath);

    /// <summary>
    /// Gets original file name without extension. E.g. <c>"My photo"</c>.
    /// </summary>
    public string FileTitle => Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>
    /// Gets the file name without extension and including a trailing dot. E.g. <c>"My photo."</c>.
    /// </summary>
    public string GalleryFileTitle => $"{FileTitle}.";

    /// <summary>
    /// Gets file extension without dot. E.g. <c>"png"</c>.
    /// </summary>
    public string GalleryFileExtension => Extension.Length > 1 ? Extension.Substring(1) : string.Empty;




    /// <summary>
    /// Gets the error details.
    /// </summary>
    public Exception? Error { get; set; } = null;

    /// <summary>
    /// Gets, sets options for reading photo.
    /// </summary>
    public PhotoReadOptions ReadOptions { get; set; } = new();

    /// <summary>
    /// Gets, sets the settings for reading Metadata and photo with <see cref="MagickCodec"/>.
    /// </summary>
    public MagickReadSettings? ReadSettings { get; set; } = null;

    /// <summary>
    /// Gets image metadata.
    /// </summary>
    public PhotoMetadata Metadata
    {
        get; private set
        {
            if (field == value) return;
            try
            {
                field.Dispose();
                field = value;
                _ = OnPropertyChanged();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌: Metadata setter: {ex.Message}");
            }
        }
    } = new();

    /// <summary>
    /// Gets photo loading cancellation token source.
    /// </summary>
    public CancellationToken? CancelToken => _cancelPhotoLoading?.Token;

    /// <summary>
    /// Gets, sets the image source for gallery thumbnail.
    /// </summary>
    public Bitmap? GalleryThumbnail
    {
        get; set
        {
            if (field == value) return;

            try
            {
                var old = field;
                field = value;
                _ = OnPropertyChanged();
                old?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌: GalleryThumbnail setter: {ex.Message}");
            }
        }
    }

    #endregion // Public Propterties



    #region Instance Initilization

    /// <summary>
    /// Initializes new instance of <see cref="Photo"/>
    /// </summary>
    public Photo() { }


    /// <summary>
    /// Initializes new instance of <see cref="Photo"/>
    /// </summary>
    public Photo(string filePath = "", PhotoReadOptions? options = null)
    {
        FilePath = filePath;
        ReadOptions = options ?? new();
    }


    /// <summary>
    /// Initializes a new single-frame photo using a bitmap source for rendering.
    /// </summary>
    public Photo(SKBitmap? bmp, PhotoState state = PhotoState.Loaded)
    {
        InitializePhoto(bmp, bmp?.Width ?? 0, bmp?.Height ?? 0, null, state);
    }


    /// <summary>
    /// Initializes a new single-frame photo using a image source for rendering.
    /// </summary>
    public Photo(SKImage? img, PhotoState state = PhotoState.Loaded)
    {
        InitializePhoto(img, img?.Width ?? 0, img?.Height ?? 0, null, state);
    }


    /// <summary>
    /// Initializes a new single-frame using a image source for rendering.
    /// </summary>
    public Photo(SKImage? img, PhotoMetadata? meta, PhotoState state = PhotoState.Loaded)
    {
        InitializePhoto(img, 0, 0, meta, state);
    }


    #endregion // Instance Initilization



    #region Override Functions

    /// <summary>
    /// <inheritdoc/>
    /// Calling this function also disposes Metadata object.
    /// </summary>
    protected override async void OnDisposing()
    {
        base.OnDisposing();

        await OnDisposing(true);
    }


    /// <summary>
    /// Handles the disposal of resources when an object is being disposed.
    /// </summary>
    /// <param name="disposeEverything">
    /// Option to dispose everything or only the Bitmap object.
    /// </param>
    private async Task OnDisposing(bool disposeEverything)
    {
        CancelLoading();

        // let pinned background readers finish, else they read a freed bitmap
        while (!_taskRefs.IsEmpty)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        UnloadBitmap();

        // dispose everything
        if (disposeEverything)
        {
            await UnloadThumbnailAsync().ConfigureAwait(false);

            // Do NOT dispose _thumbnailLock: a concurrent LoadThumbnailAsync may
            // still be parked on its WaitAsync (e.g. behind the throttle lock), and
            // disposing it makes that await throw ObjectDisposedException on a
            // background task — surfacing as an unobserved-exception crash. We only
            // use WaitAsync/Release (never AvailableWaitHandle), so the SemaphoreSlim
            // holds no unmanaged handle and is reclaimed safely by the GC.

            if (_taskMetadata is not null)
            {
                await _taskMetadata;
            }

            Metadata.Dispose();
            Metadata = new();

            _cancelPhotoLoading?.Dispose();
            _cancelPhotoLoading = null;
        }
    }


    #endregion // Override Functions



    #region Private Functions

    private void InitializePhoto(IDisposable? src, int width, int height, PhotoMetadata? meta, PhotoState state = PhotoState.Loaded)
    {
        // set Bitmap
        if (src is null) Bitmap = null;
        else if (src is SKImage img) Bitmap = img;
        else if (src is SKBitmap bmp) Bitmap = SkiaCodec.ToSKImage(bmp);
        else if (src is AnimatorImpl animator) Bitmap = animator;
        else throw new ArgumentException("IGE: Unsupported bitmap source", nameof(src));


        Metadata.Dispose();
        Metadata = meta ?? new()
        {
            Width = (uint)width,
            Height = (uint)height,
            FrameCount = 1,
        };

        _width = (uint)Metadata.Width;
        _height = (uint)Metadata.Height;

        State = state;
    }


    private CodecSelectionContext CreateCodecSelectionContext(PhotoMetadata meta)
    {
        return new CodecSelectionContext
        {
            EnableVectorRenderer = Core.Config.EnableVectorRenderer,
            IsDestColorProfileSupported = Core.IsDestColorProfileSupported,
            LoadRawThumbnailOnly = ReadOptions.OnlyLoadRawPreview && meta.RawThumbnail is not null,
            LoadOtherThumbnailOnly = ReadOptions.OnlyLoadNonRawPreview && (meta.ExifProfile?.ThumbnailLength ?? 0) > 0,
        };
    }


    private void ApplyDecodeResult(CodecDecodeResult result)
    {
        // set decoder codec
        CodecId = result.CodecId;

        _width = (uint)result.Size.Width;
        _height = (uint)result.Size.Height;

        if (result.VectorSource is not null)
        {
            Bitmap = result.VectorSource;
            result.VectorSource = null;
            return;
        }

        if (result.Animator is not null)
        {
            result.Animator.FrameChanged -= OnAnimatorFrameChanged;
            result.Animator.FrameChanged += OnAnimatorFrameChanged;
            Bitmap = result.Animator;
            result.Animator = null;
            return;
        }

        if (result.SingleFrame is not null)
        {
            Bitmap = result.SingleFrame;
            result.SingleFrame = null;
            return;
        }

        Bitmap = null;
    }


    private MagickReadSettings GetOrCreateMagickReadSettings()
    {
        ReadSettings ??= MagickCodec.ParseSettings(ReadOptions, false, FilePath);
        return ReadSettings;
    }


    /// <summary>
    /// Handles the decoding of image files based on their metadata.
    /// </summary>
    private async Task OnDecodingAsync(PhotoMetadata meta, CancellationToken token)
    {
        var context = CreateCodecSelectionContext(meta);
        var selectedCodec = Core.CodecRegistry.SelectDecodeCodec(meta, context);

        // no registered codec claims the file -> let Magick try anyway, it sniffs the content
        var isFallback = selectedCodec is null;
        var codec = selectedCodec ?? Core.CodecRegistry.FallbackDecodeCodec;

        // codec-agnostic trace: covers built-in and plugin codecs uniformly
        var isPlugin = codec is not (SvgCodecAdapter or SkiaCodecAdapter or MagickCodecAdapter);
        PhotoTrace.Mark("decode:codec", FilePath,
            $"{codec.CodecId} (decodePriority={codec.DecodePriority}, plugin={isPlugin}, "
            + $"fallback={isFallback}, frameIndex={ReadOptions.FrameIndex})");

        using var result = await codec.DecodeAsync(meta, ReadOptions, context, token).ConfigureAwait(false);

        // describe before ApplyDecodeResult nulls out the result fields (moves them to Bitmap)
        if (PhotoTrace.Enabled) PhotoTrace.Mark("decode:done", FilePath, $"kind={DescribeDecodeResult(result)}");

        ApplyDecodeResult(result);
    }


    /// <summary>
    /// Formats a decode result for <see cref="PhotoTrace"/> (source kind + dimensions/color type).
    /// </summary>
    private static string DescribeDecodeResult(CodecDecodeResult result)
    {
        if (result.VectorSource is not null) return "vector";
        if (result.Animator is not null) return $"animator frames={result.Animator.Frames.Length}";
        if (result.SingleFrame is SKImage img)
            return $"single-frame {img.Width}x{img.Height} colorType={img.ColorType}";
        return "none";
    }


    /// <summary>
    /// Formats key metadata for <see cref="PhotoTrace"/> (dimensions, frames, HDR, color profile).
    /// </summary>
    private static string DescribeMetadata(PhotoMetadata meta)
    {
        var hdr = meta.IsHdr ? $"HDR({meta.HdrTransferFn})" : "SDR";
        var profile = string.IsNullOrEmpty(meta.ColorProfileName) ? "none" : meta.ColorProfileName;
        return $"{meta.Width}x{meta.Height} frames={meta.FrameCount} ext={meta.FileExtension} "
            + $"vector={meta.IsVector} {hdr} wideGamut={meta.IsWideGamut} bpc={meta.BitsPerChannel} profile={profile}";
    }


    /// <summary>
    /// Keeps <see cref="_frameIndex"/> in sync during animation playback.
    /// </summary>
    private void OnAnimatorFrameChanged(AnimatorImpl sender, AnimatorFrameChangedEventArgs e)
    {
        _frameIndex = (int)e.CurrentFrame;
    }


    #endregion // Private Functions



    #region Public Functions

    /// <summary>
    /// Disposes the Bitmap and resets the relevant data.
    /// This method keeps the Metadata and neccessary resources.
    /// </summary>
    public async void Unload()
    {
        // wait for all pending tasks are done
        while (!_taskRefs.IsEmpty)
        {
            await Task.Delay(10);
        }

        // reset info
        State = PhotoState.None;
        Error = null;

        // unload image
        await OnDisposing(false);
    }


    /// <summary>
    /// Pins <see cref="Bitmap"/> for a background operation so <see cref="Unload"/> waits for it
    /// instead of disposing the image mid-use. Dispose the returned scope when done.
    /// </summary>
    public IDisposable PinBitmap()
    {
        var taskId = Guid.NewGuid();
        _ = _taskRefs.TryAdd(taskId, true);

        return new BitmapPin(this, taskId);
    }


    private sealed class BitmapPin(Photo photo, Guid taskId) : IDisposable
    {
        public void Dispose() => photo._taskRefs.TryRemove(taskId, out _);
    }


    /// <summary>
    /// Disposes the bitmap only and keeps other relevent data.
    /// </summary>
    public void UnloadBitmap()
    {
        if (Bitmap is AnimatorImpl animator)
        {
            animator.FrameChanged -= OnAnimatorFrameChanged;
        }

        Bitmap?.Dispose();
        Bitmap = null;

        _frameIndex = -1;
    }


    /// <summary>
    /// Stops any ongoing photo loading process.
    /// </summary>
    [MemberNotNull(nameof(_cancelPhotoLoading))]
    public virtual CancellationToken CancelLoading()
    {
        lock (_lock)
        {
            _cancelPhotoLoading?.Cancel();
            _cancelPhotoLoading?.Dispose();
            _cancelPhotoLoading = new();

            return _cancelPhotoLoading.Token;
        }
    }


    /// <summary>
    /// Loads photo from file.
    /// </summary>
    public virtual async Task LoadAsync(bool useCache,
        Func<PhotoLoadingEventArgs, Task>? handleProgressFn = null,
        bool skipLoadingEvent = false)
    {
        // use cached data
        if (useCache && State != PhotoState.None)
        {
            PhotoTrace.Mark("load:cache-hit", FilePath, $"state={State}");
            return;
        }
        var token = CancelLoading();
        var myGeneration = Interlocked.Increment(ref _loadGeneration);

        PhotoTrace.Begin(FilePath, $"useCache={useCache}, skipLoadingEvent={skipLoadingEvent}");
        try
        {
            // reset dispose status
            _isDisposed.SetFalse();
            State = PhotoState.None;
            Error = null;


            // 1. load metadata ===================
            // cancel if requested
            if (token.IsCancellationRequested) return;

            // load metadata
            await LoadMetadataAsync(useCache);
            PhotoTrace.Mark("metadata:loaded", FilePath, DescribeMetadata(Metadata));

            if (!skipLoadingEvent)
            {
                if (handleProgressFn is not null)
                {
                    PhotoTrace.Mark("preview:dispatch", FilePath);
                    await handleProgressFn(new(PhotoState.Preview, this, token));
                }
            }


            // 2. load image data ===================
            // cancel if requested
            if (token.IsCancellationRequested) return;

            // decode the photo on a dedicated thread to avoid thread pool starvation
            // (thumbnail loading can saturate the thread pool when OS thumbnail cache is disabled)
            Error = await Task.Factory.StartNew(async () =>
            {
                try
                {
                    await OnDecodingAsync(Metadata, token);
                    return null;
                }
                catch (TaskCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

            if (Error is not null) PhotoTrace.Mark("decode:error", FilePath, Error.Message);

            // cancel if requested
            if (token.IsCancellationRequested) return;


            // done loading
            State = PhotoState.Loaded;
            PhotoTrace.Mark("loaded:dispatch", FilePath, $"hasError={Error is not null}");
            if (handleProgressFn is not null)
            {
                await handleProgressFn(new(PhotoState.Loaded, this, token));
            }
        }
        catch (TaskCanceledException)
        {
            State = PhotoState.None;
        }
        catch (Exception ex)
        {
            Error = ex;
            State = PhotoState.Loaded;
            PhotoTrace.Mark("load:exception", FilePath, ex.Message);
            if (handleProgressFn is not null)
            {
                await handleProgressFn(new(PhotoState.Loaded, this, token));
            }
        }
        finally
        {
            PhotoTrace.End(FilePath, $"state={State}, cancelled={token.IsCancellationRequested}");

            // only unload if no newer load has started on this Photo;
            // a newer LoadAsync increments _loadGeneration, so if ours
            // is stale, calling Unload would cancel the newer load
            if (token.IsCancellationRequested
                && Volatile.Read(ref _loadGeneration) == myGeneration)
            {
                Unload();
            }
        }
    }


    /// <summary>
    /// Loads <c><see cref="Metadata"/></c> for the photo.
    /// Returns the cached metadata if it's not null and up-to-date.
    /// </summary>
    public async Task LoadMetadataAsync(bool useCache, PhotoReadOptions? newOptions = null)
    {
        var meta = await Task.Run(() => LoadMetadataAsync__(useCache, newOptions)).ConfigureAwait(false);

        if (meta is not null)
        {
            Metadata = meta;
        }
    }
    private async Task<PhotoMetadata?> LoadMetadataAsync__(bool useCache, PhotoReadOptions? newOptions = null)
    {
        try
        {
            ReadOptions = newOptions ?? ReadOptions;

            // if already started loading, wait for the task completes
            if (useCache)
            {
                if (_taskMetadata is not null
                    && _taskMetadata.Status != TaskStatus.Canceled
                    && _taskMetadata.Status != TaskStatus.Faulted)
                {
                    return await _taskMetadata;
                }
            }
            else
            {
                _taskMetadata = null;
            }


            // check if the current Metadata is outdated or not
            var hasOutdatedCache = !useCache || Metadata.IsOutdated();

            // load the metadata if it's outdated
            if (hasOutdatedCache)
            {
                var metadataCodec = Core.CodecRegistry.SelectMetadataCodec(FilePath);
                PhotoTrace.Mark("metadata:codec", FilePath,
                    metadataCodec is null ? "none (fallback)" : $"{metadataCodec.CodecId} (metaPriority={metadataCodec.MetadataPriority})");

                // load metadata off-thread
                if (metadataCodec is not null)
                {
                    _taskMetadata = Task.Run(() => metadataCodec.LoadMetadataAsync(
                        FilePath,
                        ReadOptions,
                        CancellationToken.None));
                }
                else
                {
                    _taskMetadata = Task.Run(() => new PhotoMetadata(FilePath));
                }

                // must assign on UI thread
                return await _taskMetadata;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌❌❌ {nameof(LoadMetadataAsync__)}: {ex.Message}");
        }

        return null;
    }


    /// <summary>
    /// Gets an image frame from the photo.
    /// </summary>
    public async Task<SKImage?> GetFrameAsync(uint frameIndex)
    {
        var newFrameIndex = (int)frameIndex;

        // 1. animated formats: delegate to animator's own frame cache
        if (Bitmap is AnimatorImpl animator)
        {
            _frameIndex = newFrameIndex;
            return animator.GetRenderedFrameBitmap(frameIndex);
        }

        // 2. cache hit: requested frame is already loaded in Bitmap
        if (frameIndex == _frameIndex && Bitmap is SKImage cachedImg)
        {
            return cachedImg;
        }


        // 3. single-frame image: nothing else to decode
        if (Metadata.FrameCount <= 1)
        {
            _frameIndex = newFrameIndex;
            return Bitmap as SKImage;
        }


        // 4. multi-frame: decode the requested frame via the registered codec
        // (Magick built-in or any plugin codec that supports the format).
        var newFrame = await Task.Factory.StartNew(async () =>
        {
            var options = ReadOptions with { FrameIndex = newFrameIndex };
            var context = CreateCodecSelectionContext(Metadata);
            var codec = Core.CodecRegistry.SelectDecodeCodec(Metadata, context);
            if (codec is not null)
            {
                using var result = await codec.DecodeAsync(Metadata, options, context, CancellationToken.None).ConfigureAwait(false);
                if (result.SingleFrame is SKImage sf)
                {
                    // update decoder codec
                    CodecId = result.CodecId;

                    // Detach the frame from the result so its dispose doesn't free our image.
                    var detached = sf;
                    result.SingleFrame = null;
                    return detached;
                }
            }

            // Fallback: legacy direct-Magick path (e.g. SVG vector codec returned no raster).
            using var data = await MagickCodec.DecodeImageAsync(Metadata,
                options, GetOrCreateMagickReadSettings(), null, CancellationToken.None);
            return SkiaCodec.FromMagick(data.SingleFrame, Metadata.SkiaColorSpace, Metadata.IsHdr);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();


        lock (_lock)
        {
            if (IsDisposed)
            {
                newFrame?.Dispose();
                return null;
            }

            Bitmap?.Dispose();
            Bitmap = newFrame;
            _frameIndex = newFrameIndex;
            _width = (uint)(newFrame?.Width ?? 0);
            _height = (uint)(newFrame?.Height ?? 0);
        }

        return newFrame;
    }


    /// <summary>
    /// Decodes a fresh copy of the given frame directly from the source, WITHOUT touching the cached
    /// <see cref="Bitmap"/> or the current frame index. The caller owns the returned image. Used by
    /// the viewer to (re)capture the pre-tone-map HDR frame for live re-tone-mapping without a full,
    /// display-disrupting reload.
    /// </summary>
    public async Task<SKImage?> DecodeStaticFrameAsync(uint frameIndex, CancellationToken token = default)
    {
        var newFrameIndex = (int)frameIndex;

        return await Task.Factory.StartNew(async () =>
        {
            var options = ReadOptions with { FrameIndex = newFrameIndex };
            var context = CreateCodecSelectionContext(Metadata);
            var codec = Core.CodecRegistry.SelectDecodeCodec(Metadata, context);
            if (codec is not null)
            {
                using var result = await codec.DecodeAsync(Metadata, options, context, token).ConfigureAwait(false);
                if (result.SingleFrame is SKImage sf)
                {
                    // detach so the result's dispose doesn't free the returned image
                    var detached = sf;
                    result.SingleFrame = null;
                    return detached;
                }
            }

            // fallback: direct Magick decode
            using var data = await MagickCodec.DecodeImageAsync(Metadata, options,
                GetOrCreateMagickReadSettings(), null, token);
            return SkiaCodec.FromMagick(data.SingleFrame, Metadata.SkiaColorSpace, Metadata.IsHdr);
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }


    /// <summary>
    /// Saves the photo to file.
    /// </summary>
    /// <exception cref="Exception"></exception>
    public async Task SaveAsAsync(string destFilePath,
        PhotoTransform transforms, uint quality,
        bool preserveModifiedDate = false, CancellationToken token = default)
    {
        var taskId = Guid.NewGuid();
        _ = _taskRefs.TryAdd(taskId, true);

        try
        {
            var lastWriteTime = File.GetLastWriteTime(destFilePath);

            // 1. save clipboard photo to file
            if (IsClipboard && Bitmap is SKImage img)
            {
                await Task.Factory.StartNew(async () =>
                {
                    await SkiaCodec.SaveAsync(img, destFilePath, transforms, quality, token);
                }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            }

            // 2. save photo to file
            else
            {
                await Task.Factory.StartNew(async () =>
                {
                    // update read options
                    var readOptions = ReadOptions with
                    {
                        FrameIndex = Metadata.FrameCount > 1
                            ? -1 // save all frame
                            : ReadOptions.FrameIndex, // save only current frame
                    };

                    await MagickCodec.SaveAsync(Metadata, destFilePath, readOptions, transforms, quality, token);
                }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            }


            // Issue #307: option to preserve the modified date/time
            if (preserveModifiedDate)
            {
                File.SetLastWriteTime(destFilePath, lastWriteTime);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _ = _taskRefs.TryRemove(taskId, out _);
        }
    }


    /// <summary>
    /// Signals cancellation to any in-progress thumbnail loading so it exits early.
    /// </summary>
    public void CancelThumbnailLoading()
    {
        _cancelThumbnailLoading?.Cancel();
    }


    /// <summary>
    /// Loads a thumbnail image at the specified DPI scaling factor.
    /// </summary>
    public async Task LoadThumbnailAsync(double dpi)
    {
        var thumbSize = Core.Config.ThumbnailSize * dpi * 2; // 2x bigger
        await LoadThumbnailAsync(thumbSize, useCache: true);
    }


    /// <summary>
    /// Loads the gallery thumbnail asynchronously.
    /// Uses a semaphore to ensure only one load per photo at a time,
    /// and caches the result on <see cref="GalleryThumbnail"/>.
    /// </summary>
    public async Task LoadThumbnailAsync(double thumbSize, bool useCache)
    {
        // 1. fast path: use cached thumbnail
        if (useCache && GalleryThumbnail is not null) return;
        if (IsDisposed || string.IsNullOrEmpty(FilePath)) return;

        // 2. acquire global throttle to avoid saturating the thread pool
        //    (prevents blocking the main image loading when many thumbnails load at once)
        await _thumbnailThrottleLock.WaitAsync().ConfigureAwait(false);

        try
        {
            await _thumbnailLock.WaitAsync().ConfigureAwait(false);

            try
            {
                // 3. double-check after acquiring the lock
                if (useCache && GalleryThumbnail is not null) return;
                if (IsDisposed) return;

                // reset cancellation for this load
                _cancelThumbnailLoading?.Dispose();
                _cancelThumbnailLoading = new CancellationTokenSource();
                var token = _cancelThumbnailLoading.Token;


                // 4. load metadata if needed
                await LoadMetadataAsync(true).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;


                // 4b. try disk cache
                var diskThumb = await ThumbnailDiskCache.TryGetAsync(FilePath, (int)thumbSize, token)
                    .ConfigureAwait(false);
                if (token.IsCancellationRequested) return;

                // drop entries written by an older, undersized source so they get regenerated
                var isDiskThumbUsable = PhotoPreviewProvider.IsPreviewLargeEnough(diskThumb, Metadata, thumbSize);
                if (diskThumb is not null && !isDiskThumbUsable)
                {
                    diskThumb.Dispose();
                    diskThumb = null;
                }

                if (diskThumb is not null)
                {
                    PhotoTrace.Mark("thumb:disk-hit", null, $"{FilePath} @ {(int)thumbSize}px");
                    var avBitmapCached = await Task.Run(
                        () => SkiaCodec.ToWritableBitmap(diskThumb), token)
                        .ConfigureAwait(false);
                    diskThumb.Dispose();

                    if (token.IsCancellationRequested)
                    {
                        avBitmapCached?.Dispose();
                        return;
                    }

                    GalleryThumbnail?.Dispose();
                    GalleryThumbnail = avBitmapCached;
                    return;
                }


                // 5. get thumbnail from platform provider
                PhotoTrace.Mark("thumb:provider", null, $"{FilePath} @ {(int)thumbSize}px");
                var swThumb = PhotoTrace.Enabled ? Stopwatch.StartNew() : null;
                using var skThumb = await Task.Run(
                    () => Core.PreviewProvider.GetThumbnailAsync(Metadata, thumbSize, token), token)
                    .ConfigureAwait(false);
                if (token.IsCancellationRequested || skThumb.IsDisposed()) return;

                if (swThumb is not null)
                {
                    PhotoTrace.Mark("thumb:provider-done", null,
                        $"{FilePath} -> {skThumb.Width}x{skThumb.Height} in {swThumb.ElapsedMilliseconds}ms");
                }


                // 6. convert SKImage to Avalonia Bitmap
                var avBitmap = await Task.Run(
                    () => SkiaCodec.ToWritableBitmap(skThumb), token)
                    .ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    avBitmap?.Dispose();
                    return;
                }


                // 7. update the gallery thumbnail (triggers UI binding update)
                GalleryThumbnail?.Dispose();
                GalleryThumbnail = avBitmap;


                // 8. write to disk cache last, so encoding never delays the thumbnail appearing.
                // Awaited (not fire-and-forget) to keep skThumb alive until the encode is done.
                await ThumbnailDiskCache.PutAsync(FilePath, (int)thumbSize, skThumb, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌❌❌ {nameof(LoadThumbnailAsync)}: {ex.Message}");
            }
            finally
            {
                _thumbnailLock.Release();
            }
        }
        finally
        {
            _thumbnailThrottleLock.Release();
        }
    }


    /// <summary>
    /// Cancels pending thumbnail loading and disposes the cached thumbnail.
    /// </summary>
    public async Task UnloadThumbnailAsync()
    {
        // Signal cancellation first so any in-progress load
        // can exit early and release the lock sooner.
        _cancelThumbnailLoading?.Cancel();

        await _thumbnailLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _cancelThumbnailLoading?.Dispose();
            _cancelThumbnailLoading = null;

            GalleryThumbnail?.Dispose();
            GalleryThumbnail = null;
        }
        finally
        {
            _thumbnailLock.Release();
        }
    }


    #endregion // Public Functions


}


