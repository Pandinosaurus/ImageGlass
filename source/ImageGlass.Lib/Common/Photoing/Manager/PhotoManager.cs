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
using Avalonia.Collections;
using ImageGlass.Common.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ImageGlass.Common.Photoing;

/// <summary>
/// Class for managing a collection of photos.
/// </summary>
public partial class PhotoManager : PhDisposable
{
    // photo list
    protected AvaloniaList<Photo> _items = [];
    protected readonly ConcurrentDictionary<string, int> _dict = new(StringComparer.OrdinalIgnoreCase);
    protected readonly Lock _lock = new();



    // Public Properties
    #region Public Properties

    /// <summary>
    /// Gets the number of photos currently in the collection.
    /// </summary>
    public uint Count => (uint)Items.Count;

    /// <summary>
    /// Gets a list of photos.
    /// </summary>
    public AvaloniaList<Photo> Items
    {
        get => _items;
        set
        {
            if (_items != value)
            {
                _items = value;
                _ = OnPropertyChanged();
                _ = OnPropertyChanged(nameof(Count));
            }
        }
    }

    /// <summary>
    /// Gets, sets the distinct directories list.
    /// </summary>
    public List<string> DistinctDirs { get; set; } = [];

    #endregion // Public Properties



    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public PhotoManager(IEnumerable<string>? list = null)
    {
        // stable delegate instances required by BHelper.Debounce
        _processAddedFilesAction = ProcessPendingAdds;
        _processChangedFilesAction = ProcessPendingChanges;

        if (list is not null) Add(list);
    }



    // Abstract / Virtual functions
    #region Abstract / Virtual functions

    /// <summary>
    /// Clears and disposes the resources.
    /// </summary>
    protected override void OnDisposing()
    {
        base.OnDisposing();

        CancelCaching();
        DisposeFileWatcher();
        Clear();
    }

    #endregion // Abstract / Virtual functions




    /// <summary>
    /// Tries to get & select a photo at a position offset from the current index by the specified step value.
    /// </summary>
    public bool GetByStep(int step, bool loopBackNavigation, out Photo? outputPhoto)
    {
        outputPhoto = null;

        // calculate new index
        var newIndex = CurrentIndex + step;
        var safeIndex = BHelper.ComputeIndexInRange(newIndex, Count, loopBackNavigation);
        if (safeIndex == CurrentIndex) return false;

        outputPhoto = Select(safeIndex);

        return true;
    }


}


