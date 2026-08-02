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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common.ServiceProviders.FileSearchService;


/// <summary>
/// Handles file searching, filtering, and sorting based on specified criteria.
/// </summary>
public partial class FileSearchProvider() : PhDisposable, IFileSearchProvider
{
    protected CancellationTokenSource? _cancelSearching;
    protected Action<FileSearchingEventArgs>? _progressFn;


    // Public Properties
    #region Public Properties

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public FileSearchOptions Options { get; protected set; } = new();


    /// <summary>
    /// Gets a value indicating whether the search operation has completed.
    /// </summary>
    public bool IsSearchEnded { get; protected set; } = false;


    #endregion // Public Properties



    #region Public Methods

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async virtual Task SearchAsync(IEnumerable<string> dirs, FileSearchOptions options, Action<FileSearchingEventArgs>? progressFn = null)
    {
        _progressFn = progressFn;
        Options = options;

        // cancel ongoing search
        CancelSearching();
        IsSearchEnded = false;

        // snapshot the collection to avoid modification during enumeration
        var dirList = dirs.ToList();

        // get files from the given directories
        try
        {
            await Task.Run(() =>
            {
                foreach (var dirPath in dirList)
                {
                    if (_cancelSearching.Token.IsCancellationRequested) break;
                    FindFiles(dirPath, _cancelSearching.Token);
                }
            }, _cancelSearching.Token);
        }
        catch { }

        IsSearchEnded = true;
    }


    /// <summary>
    /// Cancels an ongoing file searching operation.
    /// </summary>
    [MemberNotNull(nameof(_cancelSearching))]
    public virtual void CancelSearching()
    {
        _cancelSearching?.Cancel();
        _cancelSearching?.Dispose();
        _cancelSearching = new();
    }


    #endregion // Public Methods



    #region Protected Functions

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override void OnDisposing()
    {
        base.OnDisposing();

        _progressFn = null;
        CancelSearching();
    }


    /// <summary>
    /// Filters a collection of strings and returns the filtered results.
    /// </summary>
    protected virtual IEnumerable<string> OnFiltering(IEnumerable<string> fileList,
        FileSearchOptions options)
    {
        if (options.AllowedExtensions is null) return fileList;

        return fileList.Where(path =>
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();

            return options.AllowedExtensions.Contains(ext);
        });
    }


    /// <summary>
    /// Sorts a collection of image file paths based on provided criteria.
    /// </summary>
    protected virtual IOrderedEnumerable<string> OnSorting(IEnumerable<string> fileList,
        FileSearchOptions options)
    {
        return SortFiles(fileList, options);
    }


    /// <summary>
    /// Finds files in the given directory, emits <see cref="FileSearching"/> event.
    /// </summary>
    protected void FindFiles(string dirPath, CancellationToken token)
    {
        // cancel if requested
        if (token.IsCancellationRequested) return;

        // search files; hidden/system sub-folders are pruned from the recursion too
        var filePaths = Directory.EnumerateFiles(dirPath, "*",
            BHelper.GetEnumerationOptions(Options.IncludeHidden, Options.SearchSubDirectories));


        // cancel if requested
        if (token.IsCancellationRequested) return;

        // filter list
        filePaths = OnFiltering(filePaths, Options);


        // cancel if requested
        if (token.IsCancellationRequested) return;

        // sort list
        filePaths = OnSorting(filePaths, Options);


        // cancel if requested
        if (token.IsCancellationRequested) return;

        // emits results
        _progressFn?.Invoke(new FileSearchingEventArgs(filePaths, IsSearchEnded));
    }


    /// <summary>
    /// Sorts a collection of image file paths based on provided criteria.
    /// </summary>
    public static IOrderedEnumerable<string> SortFiles(IEnumerable<string> fileList, FileSearchOptions options)
    {
        var query = fileList;


        // Gets the file path comparer.
        var filePathComparer = new StringNaturalComparer(options.OrderType == ImageOrderType.Asc, StringComparison.OrdinalIgnoreCase);

        // Gets the directory path comparer.
        var dirPathComparer = options.GroupByDir
            ? new StringNaturalComparer(options.OrderType == ImageOrderType.Asc, StringComparison.OrdinalIgnoreCase)
            : (IComparer<string?>)Comparer<string>.Create((a, b) => 0);


        // sort by FileSize
        if (options.OrderBy == ImageOrderBy.FileSize)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => new FileInfo(f).Length)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).Length)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        // sort by DateCreated
        if (options.OrderBy == ImageOrderBy.DateCreated)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => new FileInfo(f).CreationTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).CreationTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        // sort by Extension
        if (options.OrderBy == ImageOrderBy.Extension)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).Extension, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).Extension, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        // sort by DateAccessed
        if (options.OrderBy == ImageOrderBy.DateAccessed)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => new FileInfo(f).LastAccessTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).LastAccessTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        // sort by DateModified
        if (options.OrderBy == ImageOrderBy.DateModified)
        {
            if (options.OrderType == ImageOrderType.Desc)
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
            else
            {
                return query
                    .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                    .ThenBy(f => new FileInfo(f).LastWriteTimeUtc)
                    .ThenBy(f => Path.GetFileName(f), filePathComparer);
            }
        }

        // sort by Random
        if (options.OrderBy == ImageOrderBy.Random)
        {
            // NOTE: ignoring the 'descending order' setting
            return query
                .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
                .ThenBy(_ => Guid.NewGuid());
        }


        // sort by Name (default)
        return query
            .OrderBy(f => Path.GetDirectoryName(f), dirPathComparer)
            .ThenBy(f => Path.GetFileName(f), filePathComparer);
    }


    #endregion // Protected Functions


}
