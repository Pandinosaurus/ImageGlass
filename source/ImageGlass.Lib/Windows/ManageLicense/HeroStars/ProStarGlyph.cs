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
using Avalonia.Media;
using ImageGlass.UI;
using System;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The Pro mark pinned to the license hero logo: the same star, drawn once at rest.
/// </summary>
public sealed class ProStarGlyph : PhControl
{
    private Geometry? _geometry;
    private IBrush? _brush;


    public ProStarGlyph()
    {
        IsHitTestVisible = false;
    }


    public override void Render(DrawingContext c)
    {
        base.Render(c);

        // purely cosmetic, so a failure here must not reach the license window
        try
        {
            _geometry ??= HeroStar.TryGetGeometry();
            _brush ??= HeroStar.CreateFixedBrush();
            if (_geometry is null || _brush is null) return;

            var size = Math.Min(Bounds.Width, Bounds.Height);
            if (size <= 0) return;

            HeroStar.Draw(c, _geometry, _brush, Bounds.Width / 2, Bounds.Height / 2, size, 0);
        }
        catch { }
    }
}
