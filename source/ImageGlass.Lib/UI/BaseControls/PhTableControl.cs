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
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using ImageGlass.Common.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageGlass.UI;


/// <summary>
/// A read-only data table.
/// </summary>
public class PhTableControl : PhControl
{
    private static readonly Thickness CELL_PADDING = new(10, 6);
    private static readonly TimeSpan REVEAL_DURATION = TimeSpan.FromMilliseconds(120);

    private readonly Border _frame;
    private readonly Grid _grid;
    private readonly TextBlock _emptyLabel;

    private readonly List<RowVisual> _rows = [];
    private int _hoveredRow = -1;
    private int _focusedRow = -1;


    #region Public Properties

    /// <summary>
    /// Gets, sets the text shown (in place of the table) when there are no rows.
    /// </summary>
    public string EmptyText
    {
        get => _emptyLabel.Text ?? string.Empty;
        set => _emptyLabel.Text = value;
    }

    #endregion // Public Properties


    public PhTableControl()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;

        // transparent background so the whole area (incl. gaps) reports pointer moves for row hit-testing
        _grid = new Grid { Background = Brushes.Transparent };
        _grid.PointerMoved += Grid_PointerMoved;
        _grid.PointerExited += Grid_PointerExited;

        _frame = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = _grid,
        };
        _frame[!Border.BackgroundProperty] = Resx.CreateBinding(ResxId.IG_BackgroundNeutralBrush);
        _frame[!Border.BorderBrushProperty] = Resx.CreateBinding(ResxId.IG_BorderControlBrush);
        _frame[!Border.CornerRadiusProperty] = Resx.CreateBinding(ResxId.ControlCornerRadius);

        _emptyLabel = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            FontStyle = FontStyle.Italic,
            Opacity = 0.6,
            IsVisible = false,
        };

        Content = new Panel { Children = { _frame, _emptyLabel } };
    }


    #region Public Methods

    /// <summary>
    /// Rebuilds the whole table: a header row from <paramref name="columns"/> plus one row per
    /// entry in <paramref name="rows"/> (an implicit actions column is appended after the columns).
    /// Shows <see cref="EmptyText"/> instead when <paramref name="rows"/> is empty.
    /// </summary>
    public void Build(IReadOnlyList<PhTableColumn> columns, IReadOnlyList<PhTableRow> rows)
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        _rows.Clear();
        _hoveredRow = _focusedRow = -1;

        var hasRows = rows.Count > 0;
        _emptyLabel.IsVisible = !hasRows;
        _frame.IsVisible = hasRows;
        if (!hasRows) return;

        var contentCols = columns.Count;
        var totalCols = contentCols + 1; // + actions column

        foreach (var col in columns)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition(
                col.Star ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
        }
        _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        // header row + underline spanning all columns
        _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < contentCols; c++) AddCell(HeaderCell(columns[c].Header), 0, c);
        AddCell(HLine(ResxId.IG_BorderControlBrush, VerticalAlignment.Bottom), 0, 0, totalCols);

        // data rows
        for (var i = 0; i < rows.Count; i++)
        {
            var spec = rows[i];
            var row = i + 1;
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // full-row layer behind the cells: tints on hover and reports the row's bounds for hit-testing.
            // uses the sidebar ListBoxItem's Fluent 2 SubtleFill (neutral, not accent) for a matching hover.
            var highlight = new Border { IsHitTestVisible = false, Opacity = 0 };
            highlight[!Border.BackgroundProperty] = new DynamicResourceExtension("PhListItemFillSecondary");
            highlight.Transitions = new Transitions { FadeTransition() };
            AddCell(highlight, row, 0, totalCols);

            // separator above every row except the first
            if (i > 0) AddCell(HLine(ResxId.IG_BorderNeutralBrush, VerticalAlignment.Top), row, 0, totalCols);

            for (var c = 0; c < contentCols && c < spec.Cells.Count; c++) AddCell(spec.Cells[c], row, c);

            var (actionsCell, buttons) = BuildActionsCell(spec.Actions, i);
            AddCell(actionsCell, row, contentCols);

            _rows.Add(new RowVisual(highlight, actionsCell, buttons));
        }
    }


    /// <summary>
    /// Builds a text cell that truncates with an ellipsis (optionally capped to <paramref name="maxWidth"/>)
    /// and shows the full text in a tooltip. Pass <paramref name="selectable"/> for copy-able text,
    /// or <paramref name="muted"/> for an italic placeholder (e.g. an empty value).
    /// </summary>
    public static Control TextCell(string text, double maxWidth = 0,
        bool selectable = false, bool muted = false, FontFamily? font = null)
    {
        TextBlock tb = selectable ? new SelectableTextBlock() : new TextBlock();
        tb.Text = text;
        tb.Padding = CELL_PADDING;
        tb.VerticalAlignment = VerticalAlignment.Top;
        tb.TextTrimming = TextTrimming.CharacterEllipsis;
        tb.IsTabStop = false; // only the action buttons take tab focus
        if (maxWidth > 0) tb.MaxWidth = maxWidth;
        if (font is not null) tb.FontFamily = font;

        if (muted)
        {
            tb.FontStyle = FontStyle.Italic;
            tb.Opacity = 0.6;
        }
        else if (!string.IsNullOrEmpty(text))
        {
            ToolTip.SetTip(tb, text);
        }

        return tb;
    }


    /// <summary>
    /// Wraps custom cell <paramref name="content"/> with the standard cell padding, top-aligned.
    /// </summary>
    public static Border WrapCell(Control content)
    {
        content.VerticalAlignment = VerticalAlignment.Top;
        return new Border { Padding = CELL_PADDING, Child = content };
    }

    #endregion // Public Methods


    #region Hover / focus reveal

    private void Grid_PointerMoved(object? sender, PointerEventArgs e)
    {
        var y = e.GetPosition(_grid).Y;
        var index = RowAt(y);
        if (index == _hoveredRow) return;

        _hoveredRow = index;
        UpdateReveal();
    }


    private void Grid_PointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredRow == -1) return;

        _hoveredRow = -1;
        UpdateReveal();
    }


    /// <summary>
    /// Returns the data-row index whose vertical band contains <paramref name="y"/> (grid space), or -1.
    /// </summary>
    private int RowAt(double y)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var b = _rows[i].Highlight.Bounds;
            if (y >= b.Top && y < b.Bottom) return i;
        }
        return -1;
    }


    /// <summary>
    /// Shows the actions (and hover tint) for the hovered/focused row; hides everyone else's.
    /// </summary>
    private void UpdateReveal()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            r.Actions.Opacity = i == _hoveredRow || i == _focusedRow ? 1 : 0;
            r.Highlight.Opacity = i == _hoveredRow ? 1 : 0;
        }
    }


    private bool RowHasFocus(int row) => _rows[row].Buttons.Any(b => b.IsFocused);

    #endregion // Hover / focus reveal


    #region Cell builders

    private void AddCell(Control content, int row, int col, int colSpan = 1)
    {
        Grid.SetRow(content, row);
        Grid.SetColumn(content, col);
        if (colSpan > 1) Grid.SetColumnSpan(content, colSpan);
        _grid.Children.Add(content);
    }


    private static TextBlock HeaderCell(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Padding = CELL_PADDING,
        VerticalAlignment = VerticalAlignment.Top,
    };


    /// <summary>
    /// A 1px horizontal rule whose color follows the theme (via a dynamic resource binding).
    /// </summary>
    private static Border HLine(ResxId brushId, VerticalAlignment align)
    {
        var line = new Border { Height = 1, VerticalAlignment = align };
        line[!Border.BackgroundProperty] = Resx.CreateBinding(brushId);
        return line;
    }


    /// <summary>
    /// Builds the actions cell (right-aligned icon buttons) and returns its hover-revealed
    /// wrapper plus the buttons (for focus tracking).
    /// </summary>
    private (Border cell, List<PhToolButton> buttons) BuildActionsCell(
        IReadOnlyList<PhTableAction> actions, int rowIndex)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var buttons = new List<PhToolButton>(actions.Count);
        foreach (var action in actions)
        {
            var btn = BuildActionButton(action, rowIndex);
            buttons.Add(btn);
            panel.Children.Add(btn);
        }

        var cell = new Border { Padding = new Thickness(8, 2), Opacity = 0, Child = panel };
        cell.Transitions = new Transitions { FadeTransition() };

        return (cell, buttons);
    }


    /// <summary>
    /// Builds one action button: a filled icon glyph, a tooltip, the click action, and the
    /// focus hooks that reveal the row while the button is focused.
    /// </summary>
    private PhToolButton BuildActionButton(PhTableAction action, int rowIndex)
    {
        var glyph = new Path
        {
            Width = Const.FONT_SIZE_BODY,
            Height = Const.FONT_SIZE_BODY,
            Data = Resx.GetIcon(action.Icon),
            Stretch = Stretch.Uniform,
        };
        glyph[!Shape.FillProperty] = Resx.CreateBinding(ResxId.TextControlForeground);

        var btn = new PhToolButton
        {
            Padding = new Thickness(7),
            VerticalAlignment = VerticalAlignment.Center,
            Content = glyph,
        };
        if (!string.IsNullOrEmpty(action.Tooltip)) ToolTip.SetTip(btn, action.Tooltip);

        var click = action.Click;
        btn.Click += (_, _) => click?.Invoke();

        // keep hidden actions reachable by Tab: reveal the row on focus, hide again when focus leaves it
        btn.GotFocus += (_, _) =>
        {
            _focusedRow = rowIndex;
            UpdateReveal();
        };
        btn.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_focusedRow == rowIndex && !RowHasFocus(rowIndex))
            {
                _focusedRow = -1;
                UpdateReveal();
            }
        });

        return btn;
    }


    private static DoubleTransition FadeTransition() => new()
    {
        Property = OpacityProperty,
        Duration = REVEAL_DURATION,
    };

    #endregion // Cell builders


    /// <summary>
    /// Per-row visuals the reveal logic toggles.
    /// </summary>
    private sealed record RowVisual(Border Highlight, Border Actions, List<PhToolButton> Buttons);
}




/// <summary>
/// A content column of a <see cref="PhTableControl"/>.
/// </summary>
public sealed class PhTableColumn
{
    /// <summary>
    /// Gets, sets the (already localized) header text.
    /// </summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Gets, sets whether the column fills remaining width (<c>*</c>); otherwise it auto-fits its content.
    /// </summary>
    public bool Star { get; set; }
}


/// <summary>
/// An action (icon button) shown in a <see cref="PhTableControl"/> row's actions column.
/// </summary>
public sealed class PhTableAction
{
    /// <summary>
    /// Gets, sets the button's icon glyph.
    /// </summary>
    public ResxIconId Icon { get; set; }

    /// <summary>
    /// Gets, sets the (already localized) tooltip text.
    /// </summary>
    public string Tooltip { get; set; } = string.Empty;

    /// <summary>
    /// Gets, sets the click handler.
    /// </summary>
    public Action? Click { get; set; }
}


/// <summary>
/// One data row for <see cref="PhTableControl.Build"/>: the content cells (one per column) and
/// the row's actions.
/// </summary>
public sealed class PhTableRow
{
    /// <summary>
    /// Gets, sets the content cells, one per column (build with <see cref="PhTableControl.TextCell"/>
    /// / <see cref="PhTableControl.WrapCell"/>).
    /// </summary>
    public IReadOnlyList<Control> Cells { get; set; } = [];

    /// <summary>
    /// Gets, sets the row's actions, rendered as hover/focus-revealed icon buttons.
    /// </summary>
    public IReadOnlyList<PhTableAction> Actions { get; set; } = [];
}
