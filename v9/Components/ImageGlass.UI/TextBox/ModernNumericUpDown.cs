/*
ImageGlass Project - Image viewer for Windows
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
using ImageGlass.Base;
using ImageGlass.Base.WinApi;
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ImageGlass.UI;

public class ModernNumericUpDown : NumericUpDown
{
    private bool _mouseHover = false;
    private bool _darkMode = false;
    private IColors ColorPalatte => BHelper.GetThemeColorPalatte(_darkMode);
    private static float BorderRadius => BHelper.IsOS(WindowsOS.Win11OrLater) ? 1f : 0;


    // Public properties
    #region Public properties

    /// <summary>
    /// Toggles dark mode for this <see cref="ModernButton"/> control.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DarkMode
    {
        get => _darkMode;
        set
        {
            _darkMode = value;

            Controls[1].BackColor = ColorPalatte.ControlBg;
            Controls[1].ForeColor = ColorPalatte.AppText;

            Invalidate();
        }
    }


    /// <summary>
    /// Gets, sets value indicates that the text should be selected if the control is focused or clicked.
    /// </summary>
    [DefaultValue(true)]
    public bool SelectAllTextOnFocus { get; set; } = true;


    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Color ForeColor { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Color BackColor { get; set; }

    #endregion // Public properties


    public ModernNumericUpDown()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
               ControlStyles.ResizeRedraw |
               ControlStyles.UserPaint, true);

        base.ForeColor = ColorPalatte.AppText;
        base.BackColor = ColorPalatte.ControlBg;

        Controls[0].MouseEnter += Control_MouseEnter;
        Controls[0].MouseLeave += Control_MouseLeave;
        Controls[1].MouseEnter += Control_MouseEnter;
        Controls[1].MouseLeave += Control_MouseLeave;
    }

    private void Control_MouseEnter(object? sender, EventArgs e)
    {
        _mouseHover = true;
        this.Invalidate();
    }

    private void Control_MouseLeave(object? sender, EventArgs e)
    {
        _mouseHover = false;
        this.Invalidate();
    }


    // Protected override methods
    #region Protected override methods

    protected override void Dispose(bool disposing)
    {
        Controls[0].MouseEnter -= Control_MouseEnter;
        Controls[0].MouseLeave -= Control_MouseLeave;
        Controls[1].MouseEnter -= Control_MouseEnter;
        Controls[1].MouseLeave -= Control_MouseLeave;

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rect = e.ClipRectangle;
        var borderRect = new Rectangle(rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);


        // fill background
        using (var brush = new SolidBrush(Controls[1].BackColor))
        {
            g.FillRoundedRectangle(brush, borderRect, BorderRadius);
        }


        // draw border
        var borderColor = ColorPalatte.ControlBorder;
        if (Focused && TabStop)
        {
            borderColor = ColorPalatte.ControlBorderAccent;
        }
        else if (_mouseHover)
        {
            borderColor = borderColor.WithBrightness(0.3f);
        }

        using (var pen = new Pen(borderColor, DpiApi.Scale(1.1f)))
        {
            pen.Alignment = PenAlignment.Outset;
            pen.LineJoin = LineJoin.Round;
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawRoundedRectangle(pen, borderRect, BorderRadius);
            g.SmoothingMode = SmoothingMode.None;
        }
    }


    protected override void OnMouseMove(MouseEventArgs e)
    {
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _mouseHover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseHover = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);

        if (SelectAllTextOnFocus)
        {
            Select(0, Text.Length);
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();

        if (SelectAllTextOnFocus)
        {
            Select(0, Text.Length);
        }
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);

        // restore display text if user deletes the value
        if (string.IsNullOrWhiteSpace(Controls[1].Text))
        {
            Controls[1].Text = Value.ToString();
        }
        Invalidate();
    }

    protected override void OnTextBoxLostFocus(object? source, EventArgs e)
    {
        base.OnTextBoxLostFocus(source, e);
        Invalidate();
    }

    #endregion // Protected override methods

}
