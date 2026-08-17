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

namespace ImageGlass.Common.ServiceProviders.FileSearchService;


/// <summary>
/// Defines a method to combine strings naturally.
/// <para>
/// This class is based on: <see href="https://github.com/GihanSoft/NaturalStringComparer"/>.
/// MIT license. Copyright(c) 2018 Mohammad Babayi.
/// </para>
/// </summary>
/// <param name="orderByAsc">String order mode</param>
/// <param name="compareMode">String comparison mode</param>
public sealed class StringNaturalComparer(
    bool orderByAsc = true,
    StringComparison compareMode = StringComparison.Ordinal) : IComparer<string?>, IComparer<ReadOnlyMemory<char>>
{
    /// <summary>
    /// Indicates whether the ordering should be in ascending order.
    /// </summary>
    public bool OrderByAsc { get; set; } = orderByAsc;

    /// <summary>
    /// Defines the mode of string comparison used.
    /// </summary>
    public StringComparison CompareMode { get; set; } = compareMode;


    /// <summary>
    /// Compares two strings based on a <see cref="OrderByAsc"/> and <see cref="CompareMode"/>.
    /// </summary>
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;

        if (OrderByAsc)
        {
            if (x is null) return -1;
            if (y is null) return 1;

            return Compare(x.AsSpan(), y.AsSpan(), CompareMode);
        }

        if (x is null) return 1;
        if (y is null) return -1;

        return Compare(y.AsSpan(), x.AsSpan(), CompareMode);
    }


    /// <summary>
    /// Compares two character spans based on a <see cref="OrderByAsc"/> and <see cref="CompareMode"/>.
    /// </summary>
    public int Compare(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        if (OrderByAsc)
        {
            return Compare(x, y, CompareMode);
        }

        return Compare(y, x, CompareMode);
    }


    /// <summary>
    /// Compares two character spans based on a <see cref="OrderByAsc"/> and <see cref="CompareMode"/>.
    /// </summary>
    public int Compare(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        if (OrderByAsc)
        {
            return Compare(x.Span, y.Span, CompareMode);
        }

        return Compare(y.Span, x.Span, CompareMode);
    }


    /// <summary>
    /// Compares two character spans.
    /// </summary>
    public static int Compare(ReadOnlySpan<char> x, ReadOnlySpan<char> y, StringComparison stringComparison)
    {
        var length = Math.Min(x.Length, y.Length);

        for (var i = 0; i < length; i++)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[i]))
            {
                var xOut = GetNumber(x.Slice(i), out var xNumAsSpan);
                var yOut = GetNumber(y.Slice(i), out var yNumAsSpan);

                var compareResult = CompareNumValues(xNumAsSpan, yNumAsSpan);

                if (compareResult != 0)
                {
                    return compareResult;
                }

                i = -1;
                length = Math.Min(xOut.Length, yOut.Length);

                x = xOut;
                y = yOut;
                continue;
            }

            var charCompareResult = x.Slice(i, 1).CompareTo(y.Slice(i, 1), stringComparison);
            if (charCompareResult != 0)
            {
                return charCompareResult;
            }
        }

        return x.Length.CompareTo(y.Length);
    }


    private static ReadOnlySpan<char> GetNumber(ReadOnlySpan<char> span, out ReadOnlySpan<char> number)
    {
        var i = 0;
        while (i < span.Length && char.IsDigit(span[i]))
        {
            i++;
        }

        number = span.Slice(0, i);
        return span.Slice(i);
    }


    private static int CompareNumValues(ReadOnlySpan<char> numValue1, ReadOnlySpan<char> numValue2)
    {
        var num1AsSpan = numValue1.TrimStart('0');
        var num2AsSpan = numValue2.TrimStart('0');

        if (num1AsSpan.Length < num2AsSpan.Length)
        {
            return -1;
        }

        if (num1AsSpan.Length > num2AsSpan.Length)
        {
            return 1;
        }

        var compareResult = num1AsSpan.CompareTo(num2AsSpan, StringComparison.Ordinal);

        if (compareResult != 0)
        {
            return Math.Sign(compareResult);
        }

        if (numValue2.Length == numValue1.Length)
        {
            return compareResult;
        }

        return numValue2.Length < numValue1.Length ? -1 : 1; // "033" < "33" === true
    }

}
