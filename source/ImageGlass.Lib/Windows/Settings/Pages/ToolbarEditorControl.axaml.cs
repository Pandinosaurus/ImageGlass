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
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using ImageGlass.Common;
using ImageGlass.Common.Localization;
using ImageGlass.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The drag-and-drop toolbar arranger used by the Toolbar settings page. It shows the toolbar's
/// "Current buttons" (split into the centered <c>Primary</c> group and the right-aligned
/// <c>Secondary</c> group) and an "Available buttons" list. Buttons can be reordered, moved between
/// groups, added or removed by dragging chips or via each chip's right-click menu. All edits are
/// staged in working copies and surfaced through <see cref="ButtonsChanged"/> /
/// <see cref="CurrentButtons"/>; the host page commits them to <see cref="Config"/> on Apply/OK.
/// </summary>
public partial class ToolbarEditorControl : PhControl
{
    /// <summary>
    /// Identifies one of the three button lists shown in the editor.
    /// </summary>
    private enum EditorGroup { Primary, Secondary, Available }

    // editor chip size (icon only); kept fixed so chips stay uniform regardless of the toolbar icon size
    private const double ICON_SIZE = 24;
    private const double CHIP_PADDING = 6;
    private const double DRAG_THRESHOLD = 3;

    // working copies (clones); the catalog is never mutated
    private readonly List<ToolbarItemModel> _primary = [];
    private readonly List<ToolbarItemModel> _secondary = [];
    private readonly List<ToolbarItemModel> _available = [];

    // cache of parsed SVG sources by icon path so re-renders never hit the disk (keeps drag/drop snappy)
    private readonly Dictionary<string, SvgSource?> _svgCache = new(StringComparer.OrdinalIgnoreCase);

    // the built-in button catalog, built once (Config.BuiltInToolbarItems rebuilds it on each access)
    private List<ToolbarItemModel>? _catalog;
    private List<ToolbarItemModel> Catalog => _catalog ??= [.. Config.BuiltInToolbarItems];

    // drag state
    private Control? _dragChip;
    private ToolbarItemModel? _dragModel;
    private EditorGroup _dragSource;
    private Point _dragStart;
    private bool _isDragging;

    // drag visuals
    private Border? _ghost;
    private Border? _marker; // insertion line; stays on PART_DragLayer (inside the editor)
    private Panel? _ghostHost; // hosts the ghost: the window OverlayLayer so it isn't clipped

    // the chip to briefly highlight after a move (set just before a re-render)
    private ToolbarItemModel? _justMoved;

    // the model whose chip should receive focus after the next re-render (keyboard edits)
    private ToolbarItemModel? _focusAfterRender;

    // the current button "picked up" for click/keyboard rearranging (single selection), or null
    private ToolbarItemModel? _pickedModel;


    /// <summary>
    /// Raised after any edit (drag, menu action, or reset) so the host can re-stage the buttons.
    /// </summary>
    public event EventHandler? ButtonsChanged;


    public ToolbarEditorControl()
    {
        InitializeComponent();

        PART_AddCustomBtn.Click += (_, _) => OnAddCustomClicked();
        PART_ResetBtn.Click += (_, _) => ResetToDefault();

        // keyboard-only arranging: Tab moves between groups, arrow keys move within a group.
        // The control itself is focusable so settings-search navigation can land here (forwarded
        // into the Current buttons by OnGotFocus).
        Focusable = true;
        KeyboardNavigation.SetTabNavigation(PART_PrimaryGroup, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetTabNavigation(PART_SecondaryGroup, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetTabNavigation(PART_AvailableGroup, KeyboardNavigationMode.Once);

        // tunnel + handledEventsToo so we see arrows / Delete / Enter before the focused chip
        // (a button) consumes them
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }


    #region Public API

    /// <summary>
    /// Loads the given toolbar buttons into the editor (cloned into working copies) and renders them.
    /// </summary>
    public void LoadButtons(IEnumerable<ToolbarItemModel> current)
    {
        _pickedModel = null;
        _primary.Clear();
        _secondary.Clear();

        foreach (var item in current)
        {
            var clone = Clone(item);
            if (clone.Alignment == ToolbarItemAlignment.Right) _secondary.Add(clone);
            else _primary.Add(clone);
        }

        RecomputeAvailable();
        RenderAll();
    }


    /// <summary>
    /// Gets the current buttons as a flat collection (primary group first, then secondary),
    /// with each item's <see cref="ToolbarItemModel.Alignment"/> set to match its group.
    /// </summary>
    public ObservableCollection<ToolbarItemModel> CurrentButtons
    {
        get
        {
            var list = new ObservableCollection<ToolbarItemModel>();
            foreach (var m in _primary) { m.Alignment = ToolbarItemAlignment.Left; list.Add(m); }
            foreach (var m in _secondary) { m.Alignment = ToolbarItemAlignment.Right; list.Add(m); }
            return list;
        }
    }

    #endregion // Public API


    #region Control events

    protected override void OnLoaded(RoutedEventArgs e)
    {
        // the theme (and thus icon paths) may have changed while this page was detached;
        // drop cached icons so they reload for the current dark/light pack
        _svgCache.Clear();
        base.OnLoaded(e); // base triggers OnIgLanguageChanged → RenderAll with fresh icons
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        PART_AddCustomBtn.Text = Core.Lang[LangId.FrmSettings_Toolbar_AddCustomButton];
        PART_ResetBtn.Text = Core.Lang[LangId._ResetToDefault];

        // tooltips and the available-list sort order are language-dependent
        RecomputeAvailable();
        RenderAll();
    }


    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);

        // theme icon paths change with the dark/light pack: drop the cache and reload icons
        _svgCache.Clear();
        RenderAll();
    }


    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);

        // settings-search navigation focuses the editor itself; forward focus into the
        // Current buttons so keyboard users start where they can arrange the toolbar
        // (guard on IsFocused so a quick Tab-through that already left isn't yanked back)
        if (ReferenceEquals(e.Source, this))
        {
            Dispatcher.UIThread.Post(() => { if (IsFocused) FocusFirstButton(); }, DispatcherPriority.Input);
        }
    }

    #endregion // Control events


    #region Model helpers

    private List<ToolbarItemModel> ListFor(EditorGroup g) => g switch
    {
        EditorGroup.Primary => _primary,
        EditorGroup.Secondary => _secondary,
        _ => _available,
    };

    private WrapPanel PanelFor(EditorGroup g) => g switch
    {
        EditorGroup.Primary => PART_PrimaryGroup,
        EditorGroup.Secondary => PART_SecondaryGroup,
        _ => PART_AvailableGroup,
    };

    private Border ZoneFor(EditorGroup g) => g switch
    {
        EditorGroup.Primary => PART_PrimaryZone,
        EditorGroup.Secondary => PART_SecondaryZone,
        _ => PART_AvailableZone,
    };

    private Rectangle DashFor(EditorGroup g) => g switch
    {
        EditorGroup.Primary => PART_PrimaryDash,
        EditorGroup.Secondary => PART_SecondaryDash,
        _ => PART_AvailableDash,
    };


    /// <summary>
    /// Creates a shallow copy of a toolbar item (the click action is shared, it is never mutated).
    /// </summary>
    private static ToolbarItemModel Clone(ToolbarItemModel m) => new()
    {
        Id = m.Id,
        Image = m.Image,
        Text = m.Text,
        ShowText = m.ShowText,
        ConfigBinding = m.ConfigBinding,
        ConfigBindingValue = m.ConfigBindingValue,
        Alignment = m.Alignment,
        OnClick = m.OnClick,
    };


    /// <summary>
    /// Rebuilds the "Available" list: a separator template followed by every built-in button
    /// not already in the current toolbar, sorted by localized name.
    /// </summary>
    private void RecomputeAvailable()
    {
        _available.Clear();
        _available.Add(ToolbarItemModel.Separator);

        var currentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _primary) if (!m.IsSeparator) currentIds.Add(m.Id);
        foreach (var m in _secondary) if (!m.IsSeparator) currentIds.Add(m.Id);

        var others = Catalog
            .Where(b => !currentIds.Contains(b.Id))
            .OrderBy(b => b.DisplayText, StringComparer.CurrentCultureIgnoreCase);
        _available.AddRange(others);
    }


    /// <summary>
    /// Gets a parsed SVG source for the given icon path, caching it so repeated renders avoid disk I/O.
    /// </summary>
    private SvgSource? GetSvg(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_svgCache.TryGetValue(path, out var cached)) return cached;

        SvgSource? src = null;
        try { src = SvgSource.Load(path); }
        catch { }

        _svgCache[path] = src;
        return src;
    }

    #endregion // Model helpers


    #region Rendering

    private void RenderAll()
    {
        RenderGroup(EditorGroup.Primary);
        RenderGroup(EditorGroup.Secondary);
        RenderGroup(EditorGroup.Available);
        _justMoved = null;

        // a keyboard edit asked to keep focus on a specific button: re-focus it once the
        // fresh chips are in the tree (only set by keyboard/menu edits, never by mouse drag)
        var focusModel = _focusAfterRender;
        _focusAfterRender = null;
        if (focusModel is not null)
        {
            Dispatcher.UIThread.Post(() => FocusChipFor(focusModel), DispatcherPriority.Input);
        }
    }


    private void RenderGroup(EditorGroup g)
    {
        var panel = PanelFor(g);
        panel.Children.Clear();

        foreach (var model in ListFor(g))
        {
            panel.Children.Add(BuildChip(model));
        }
    }


    /// <summary>
    /// Builds a draggable chip (icon only, with a tooltip) for a toolbar item.
    /// </summary>
    private Control BuildChip(ToolbarItemModel model)
    {
        var name = model.IsSeparator ? Core.Lang[LangId._Separator] : model.DisplayText;

        var chip = new PhToolButton
        {
            Tag = model,
            Padding = new Thickness(CHIP_PADDING),
            Margin = new Thickness(3),
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true, // keyboard arranging: chips are arrow-navigable within their group
            IsChecked = ReferenceEquals(model, _pickedModel), // checked highlight = selected to move
            Content = BuildIconVisual(model),
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) }],
        };

        ToolTip.SetTip(chip, name);
        AutomationProperties.SetName(chip, name); // screen-reader label

        // PhToolButton (a button) marks pointer events handled, so listen on the tunnel route
        // with handledEventsToo to still drive the drag
        chip.AddHandler(PointerPressedEvent, Chip_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        chip.AddHandler(PointerMovedEvent, Chip_PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        chip.AddHandler(PointerReleasedEvent, Chip_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        chip.AddHandler(PointerCaptureLostEvent, Chip_PointerCaptureLost, handledEventsToo: true);

        // brief fade-in on the chip that was just moved/added so the landing spot is easy to spot
        if (ReferenceEquals(model, _justMoved))
        {
            chip.Opacity = 0.25;
            Dispatcher.UIThread.Post(() => chip.Opacity = 1, DispatcherPriority.Background);
        }

        return chip;
    }


    /// <summary>
    /// Builds the icon visual for a chip / ghost: the button's SVG icon, or a thin line for a separator.
    /// </summary>
    private Control BuildIconVisual(ToolbarItemModel model)
    {
        if (model.IsSeparator)
        {
            // inline (not a style class) so it also renders when the ghost is hosted on the overlay
            var lineBrush = (this.TryFindResource("TextControlForeground", out var fg) ? fg as IBrush : null)
                ?? Brushes.Gray;
            return new Border
            {
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                Child = new Border
                {
                    Width = 2,
                    Height = ICON_SIZE * 0.7,
                    CornerRadius = new CornerRadius(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.5,
                    Background = lineBrush,
                },
            };
        }

        var src = GetSvg(model.ImagePath);
        if (src is null) return new Border { Width = ICON_SIZE, Height = ICON_SIZE };

        return new Image
        {
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            Source = new SvgImage { Source = src },
        };
    }


    #endregion // Rendering


    #region Edit operations

    private void ResetToDefault()
    {
        LoadButtons(Config.DefaultToolbarItems);
        ButtonsChanged?.Invoke(this, EventArgs.Empty);
    }


    private void OnAddCustomClicked()
    {
        // TODO: open the custom-button editor dialog (placeholder).
    }


    /// <summary>
    /// Recomputes the available list, re-renders, and notifies the host of the change.
    /// </summary>
    private void Commit()
    {
        RecomputeAvailable();
        RenderAll();
        ButtonsChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion // Edit operations


    #region Drag and drop

    private void Chip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control chip || chip.Tag is not ToolbarItemModel model) return;

        // left button starts a drag; right button falls through to the context menu
        if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;

        _dragChip = chip;
        _dragModel = model;
        _dragSource = GroupOfChip(chip);
        _dragStart = e.GetPosition(PART_DragLayer);
        _isDragging = false;

        // don't mark handled: let the button show its pressed state
        e.Pointer.Capture(chip);
    }


    private void Chip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragChip is null || !ReferenceEquals(sender, _dragChip)) return;
        if (!ReferenceEquals(e.Pointer.Captured, _dragChip)) return;

        var pos = e.GetPosition(PART_DragLayer);

        // ignore tiny moves so a click doesn't start a drag
        if (!_isDragging)
        {
            var delta = pos - _dragStart;
            if (Math.Abs(delta.X) < DRAG_THRESHOLD && Math.Abs(delta.Y) < DRAG_THRESHOLD) return;
            StartDrag(e);
        }

        // float the ghost centered under the cursor (in its host's coordinate space)
        var host = _ghostHost ?? (Panel)PART_DragLayer;
        var gpos = e.GetPosition(host);
        Canvas.SetLeft(_ghost!, gpos.X - _ghost!.Width / 2);
        Canvas.SetTop(_ghost, gpos.Y - _ghost.Height / 2);

        UpdateDropTarget(e);
    }


    private void Chip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragChip is null || !ReferenceEquals(sender, _dragChip)) return;

        if (!_isDragging)
        {
            // a plain click (not a drag): select / deselect the button — the mouse equivalent
            // of pressing Space/Enter on it
            e.Pointer.Capture(null);
            var clickedModel = _dragModel;
            _dragChip = null;
            _dragModel = null;

            if (clickedModel is not null) TogglePick(clickedModel);
            return;
        }

        var srcModel = _dragModel!;
        var srcGroup = _dragSource;
        var target = HitZone(e);
        var dropIndex = target is EditorGroup tg && tg != EditorGroup.Available
            ? ComputeInsertIndex(PanelFor(tg), e.GetPosition(PanelFor(tg)))
            : 0;

        e.Pointer.Capture(null);
        EndDrag();

        _dragChip = null;
        _dragModel = null;
        _isDragging = false;

        if (target is EditorGroup dst)
        {
            PerformDrop(srcModel, srcGroup, dst, dropIndex);
        }
        else
        {
            // dropped outside any zone: just restore the dimmed chip
            RenderAll();
        }
    }


    private void Chip_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(sender, _dragChip)) return;

        var wasDragging = _isDragging;
        EndDrag();
        _dragChip = null;
        _dragModel = null;
        _isDragging = false;

        if (wasDragging) RenderAll();
    }


    /// <summary>
    /// Applies a drop: dropping onto the Available zone removes the button from the toolbar; dropping
    /// onto a group adds (from Available) or moves (from another/the same group) at the given index.
    /// </summary>
    private void PerformDrop(ToolbarItemModel src, EditorGroup srcGroup, EditorGroup dst, int index)
    {
        if (dst == EditorGroup.Available)
        {
            // only current buttons can be dropped here (Available isn't a valid target for itself)
            if (srcGroup == EditorGroup.Available) { RenderAll(); return; }
            ListFor(srcGroup).Remove(src);
        }
        else
        {
            var dstList = ListFor(dst);
            var dstAlignment = dst == EditorGroup.Secondary
                ? ToolbarItemAlignment.Right
                : ToolbarItemAlignment.Left;

            if (srcGroup == EditorGroup.Available)
            {
                // adding a copy from the catalog (a fresh instance for separators)
                var clone = src.IsSeparator ? ToolbarItemModel.Separator : Clone(src);
                index = Math.Clamp(index, 0, dstList.Count);
                dstList.Insert(index, clone);
                clone.Alignment = dstAlignment;
                _justMoved = clone;
            }
            else
            {
                // moving an existing button (reorder within a group, or across groups)
                var srcList = ListFor(srcGroup);
                var oldIndex = srcList.IndexOf(src);
                if (oldIndex < 0) { RenderAll(); return; }

                srcList.RemoveAt(oldIndex);
                if (srcGroup == dst && oldIndex < index) index--;
                index = Math.Clamp(index, 0, dstList.Count);
                dstList.Insert(index, src);
                src.Alignment = dstAlignment;
                _justMoved = src;
            }
        }

        Commit();
    }


    private void StartDrag(PointerEventArgs e)
    {
        _isDragging = true;
        _dragChip!.Opacity = 0.35;

        // a drag supersedes any keyboard selection
        if (_pickedModel is not null) { _pickedModel = null; RefreshCheckedStates(); }

        // host the cursor-following visuals on the window overlay so they aren't clipped by the
        // settings section / scroll viewer; fall back to the local drag layer if unavailable
        _ghostHost = OverlayLayer.GetOverlayLayer(this) ?? (Panel)PART_DragLayer;

        // the ghost lives outside this control's <Styles> scope (it's on the overlay), so style it
        // inline. Use the themed neutral background (not accent) so it reads right in light/dark.
        var accent = (this.TryFindResource("ZoneAccentBorder", out var a) ? a as IBrush : null)
            ?? new SolidColorBrush(Color.FromRgb(0x33, 0x77, 0xCC));
        var ghostBg = (this.TryFindResource("IG_BackgroundNeutralBrush", out var b) ? b as IBrush : null)
            ?? new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

        // floating ghost
        _ghost = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = accent,
            Background = ghostBg,
            BoxShadow = BoxShadows.Parse("0 4 12 0 #40000000"),
            Width = _dragChip.Bounds.Width,
            Height = _dragChip.Bounds.Height,
            IsHitTestVisible = false,
            Child = BuildIconVisual(_dragModel!),
        };
        _ghostHost.Children.Add(_ghost);

        // outline valid drop zones (not the group the button came from), setting the hover state
        // for the zone already under the cursor in one shot to avoid a flash on the first move
        var hovered = HitZone(e);
        foreach (var g in ValidTargets())
        {
            var show = g != _dragSource;
            SetZoneState(g, valid: show, hover: show && g == hovered);
        }
    }


    private void EndDrag()
    {
        var host = _ghostHost ?? (Panel)PART_DragLayer;

        if (_ghost is not null)
        {
            host.Children.Remove(_ghost);
            _ghost = null;
        }
        _ghostHost = null;

        HideMarker();
        ResetZoneStates();

        if (_dragChip is not null) _dragChip.Opacity = 1;
    }


    /// <summary>
    /// Gets the groups a button can be dropped onto: a button from Available can only land in a group;
    /// a current button can also be dropped on the Available zone to remove it.
    /// </summary>
    private IEnumerable<EditorGroup> ValidTargets()
    {
        yield return EditorGroup.Primary;
        yield return EditorGroup.Secondary;
        if (_dragSource != EditorGroup.Available) yield return EditorGroup.Available;
    }


    /// <summary>
    /// Highlights the hovered zone (and positions the insertion marker for a group), keeping the
    /// dashed "valid" outline on the other valid zones.
    /// </summary>
    private void UpdateDropTarget(PointerEventArgs e)
    {
        HideMarker();

        var hovered = HitZone(e);
        foreach (var g in ValidTargets())
        {
            // don't outline the group the button came from
            var show = g != _dragSource;
            SetZoneState(g, valid: show, hover: show && g == hovered);
        }

        if (hovered is EditorGroup hg && hg != EditorGroup.Available)
        {
            var panel = PanelFor(hg);
            ShowMarker(panel, ComputeInsertIndex(panel, e.GetPosition(panel)));
        }
    }


    /// <summary>
    /// Returns the valid drop group whose zone is under the pointer, or <c>null</c>.
    /// </summary>
    private EditorGroup? HitZone(PointerEventArgs e)
    {
        foreach (var g in ValidTargets())
        {
            var zone = ZoneFor(g);
            var p = e.GetPosition(zone);
            if (p.X >= 0 && p.Y >= 0 && p.X <= zone.Bounds.Width && p.Y <= zone.Bounds.Height)
            {
                return g;
            }
        }

        return null;
    }


    private EditorGroup GroupOfChip(Control chip)
    {
        if (PART_PrimaryGroup.Children.Contains(chip)) return EditorGroup.Primary;
        if (PART_SecondaryGroup.Children.Contains(chip)) return EditorGroup.Secondary;
        return EditorGroup.Available;
    }


    private void SetZoneState(EditorGroup g, bool valid, bool hover)
    {
        SetClass(ZoneFor(g), "valid", valid);
        SetClass(ZoneFor(g), "hover", hover);
        SetClass(DashFor(g), "valid", valid);
        SetClass(DashFor(g), "hover", hover);
    }


    private void ResetZoneStates()
    {
        SetZoneState(EditorGroup.Primary, false, false);
        SetZoneState(EditorGroup.Secondary, false, false);
        SetZoneState(EditorGroup.Available, false, false);
    }


    /// <summary>
    /// Computes the index at which a dropped chip should be inserted, based on the pointer position
    /// over the wrapped chips (row-aware: respects line wrapping).
    /// </summary>
    private static int ComputeInsertIndex(WrapPanel panel, Point p)
    {
        var chips = panel.Children.Where(c => c.Tag is ToolbarItemModel).ToList();
        var n = chips.Count;
        if (n == 0) return 0;

        for (var i = 0; i < n; i++)
        {
            var b = chips[i].Bounds;

            // chip's row is entirely above the pointer: keep scanning
            if (p.Y > b.Bottom) continue;

            // pointer is above this chip's row entirely: insert before it
            if (p.Y < b.Top) return i;

            // pointer is within this chip's row
            if (p.X < b.X + b.Width / 2) return i;

            // past this chip; if it's the last on its row, insert after it
            var lastInRow = i == n - 1 || chips[i + 1].Bounds.Top > b.Bottom - 0.5;
            if (lastInRow) return i + 1;
        }

        return n;
    }


    private void ShowMarker(WrapPanel panel, int index)
    {
        EnsureMarker();

        var chips = panel.Children.Where(c => c.Tag is ToolbarItemModel).ToList();
        double x, y, height;

        if (chips.Count == 0)
        {
            var topLeft = panel.TranslatePoint(default, PART_DragLayer) ?? default;
            x = topLeft.X;
            y = topLeft.Y;
            height = ICON_SIZE + CHIP_PADDING * 2;
        }
        else if (index >= chips.Count)
        {
            var last = chips[^1];
            var corner = last.TranslatePoint(new Point(last.Bounds.Width, 0), PART_DragLayer) ?? default;
            x = corner.X + 1;
            y = corner.Y;
            height = last.Bounds.Height;
        }
        else
        {
            var chip = chips[index];
            var corner = chip.TranslatePoint(default, PART_DragLayer) ?? default;
            x = corner.X - 4;
            y = corner.Y;
            height = chip.Bounds.Height;
        }

        _marker!.Height = height;
        Canvas.SetLeft(_marker, x);
        Canvas.SetTop(_marker, y);
        _marker.IsVisible = true;
    }


    private void EnsureMarker()
    {
        if (_marker is not null) return;

        _marker = new Border { Classes = { "dropMarker" }, IsVisible = false };
        PART_DragLayer.Children.Add(_marker);
    }


    private void HideMarker()
    {
        if (_marker is not null) _marker.IsVisible = false;
    }


    private static void SetClass(StyledElement el, string className, bool on)
    {
        // only mutate when the state actually changes (avoids duplicate classes + redundant
        // style invalidation on every pointer move while dragging)
        var has = el.Classes.Contains(className);
        if (on && !has) el.Classes.Add(className);
        else if (!on && has) el.Classes.Remove(className);
    }

    #endregion // Drag and drop


    #region Keyboard navigation

    /// <summary>
    /// Drives keyboard-only arranging from the focused chip. Space/Enter (or a click) selects / deselects
    /// a button. While a button is selected the arrows move it: Left/Right shift a current button (hopping
    /// between the Primary and Secondary groups at the inner edge), Up promotes an available button into
    /// Current, Down sends a current button to Available — and it stays selected so moves can continue.
    /// Otherwise the arrows move focus between chips. Delete removes a current button; Escape deselects.
    /// Runs on the tunnel route so it beats the button's own key handling.
    /// </summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control focused
            || focused.Tag is not ToolbarItemModel model)
            return;

        var group = GroupOfChip(focused);
        var picked = _pickedModel is not null && ReferenceEquals(_pickedModel, model);

        switch (e.Key)
        {
            // when selected, the arrows move the button; otherwise they move focus
            case Key.Left:
                if (picked) { ShiftPicked(-1); e.Handled = true; }
                else e.Handled = MoveFocusHorizontal(focused, group, -1);
                break;

            case Key.Right:
                if (picked) { ShiftPicked(+1); e.Handled = true; }
                else e.Handled = MoveFocusHorizontal(focused, group, +1);
                break;

            case Key.Up:
                // a selected available button is promoted up into the Current section
                if (picked) { PromotePickedToCurrent(); e.Handled = true; }
                else e.Handled = MoveFocusVertical(focused, group, up: true);
                break;

            case Key.Down:
                // a selected current button is sent down into the Available section
                if (picked) { DemotePickedToAvailable(); e.Handled = true; }
                else e.Handled = MoveFocusVertical(focused, group, up: false);
                break;

            case Key.Enter:
            case Key.Space:
                TogglePick(model); // select / deselect (does not add — Up promotes an available button)
                e.Handled = true;
                break;

            case Key.Escape:
                if (_pickedModel is not null) { ClearPick(); e.Handled = true; }
                break;

            case Key.Delete:
            case Key.Back:
                if (group != EditorGroup.Available) e.Handled = RemoveByKeyboard(model, group);
                break;
        }
    }


    /// <summary>
    /// Moves focus to the previous/next chip in the group, flowing across the two side-by-side
    /// Current groups at their inner edge. Returns whether focus actually moved.
    /// </summary>
    private bool MoveFocusHorizontal(Control chip, EditorGroup group, int delta)
    {
        var chips = ChipsOf(PanelFor(group));
        var i = chips.IndexOf(chip);
        if (i < 0) return false;

        var target = i + delta;
        if (target >= 0 && target < chips.Count)
        {
            chips[target].Focus(NavigationMethod.Tab);
            return true;
        }

        // hop between Primary and Secondary at their inner edge
        Control? cross = null;
        if (group == EditorGroup.Primary && delta > 0) cross = FirstChip(PART_SecondaryGroup);
        else if (group == EditorGroup.Secondary && delta < 0) cross = LastChip(PART_PrimaryGroup);

        if (cross is null) return false;
        cross.Focus(NavigationMethod.Tab);
        return true;
    }


    /// <summary>
    /// Moves focus to the chip on the nearest adjacent row whose horizontal centre is closest
    /// (so Up/Down feel natural across wrapped rows). Returns whether focus moved.
    /// </summary>
    private bool MoveFocusVertical(Control chip, EditorGroup group, bool up)
    {
        var chips = ChipsOf(PanelFor(group));
        if (chips.Count == 0) return false;

        var cur = chip.Bounds;
        var cx = cur.X + cur.Width / 2;

        // find the nearest adjacent row by its Top
        double? rowTop = null;
        foreach (var c in chips)
        {
            if (ReferenceEquals(c, chip)) continue;
            var top = c.Bounds.Top;
            if (up)
            {
                if (top >= cur.Top - 0.5) continue;                 // not on a higher row
                if (rowTop is null || top > rowTop) rowTop = top;   // closest above = largest Top
            }
            else
            {
                if (top <= cur.Top + 0.5) continue;                 // not on a lower row
                if (rowTop is null || top < rowTop) rowTop = top;   // closest below = smallest Top
            }
        }
        if (rowTop is null) return false;

        // on that row, pick the chip whose horizontal centre is closest
        Control? best = null;
        var bestDx = double.PositiveInfinity;
        foreach (var c in chips)
        {
            if (ReferenceEquals(c, chip)) continue;
            var b = c.Bounds;
            if (Math.Abs(b.Top - rowTop.Value) > 1) continue;
            var dx = Math.Abs((b.X + b.Width / 2) - cx);
            if (dx < bestDx) { bestDx = dx; best = c; }
        }

        best?.Focus(NavigationMethod.Tab);
        return best is not null;
    }


    /// <summary>
    /// Selects / deselects a button for click or keyboard rearranging (single selection: picking one
    /// drops any other). A selected button shows the checked highlight; arrow keys then move it — a
    /// current button shifts left/right, an available button is promoted to Current with Up.
    /// </summary>
    private void TogglePick(ToolbarItemModel model)
    {
        _pickedModel = ReferenceEquals(_pickedModel, model) ? null : model;
        RefreshCheckedStates();
    }


    /// <summary>
    /// Drops the picked-up button (clears the selection highlight).
    /// </summary>
    private void ClearPick()
    {
        if (_pickedModel is null) return;
        _pickedModel = null;
        RefreshCheckedStates();
    }


    /// <summary>
    /// Syncs every chip's checked highlight to the picked-up button (no re-render).
    /// </summary>
    private void RefreshCheckedStates()
    {
        foreach (var panel in new Panel[] { PART_PrimaryGroup, PART_SecondaryGroup, PART_AvailableGroup })
        {
            foreach (var c in panel.Children)
            {
                if (c is PhToolButton tb) tb.IsChecked = ReferenceEquals(c.Tag, _pickedModel);
            }
        }
    }


    /// <summary>
    /// Shifts the picked-up button one slot left/right within its group, hopping between the Primary
    /// and Secondary groups at the inner edge. Keeps it picked + focused. Returns whether it moved.
    /// </summary>
    private bool ShiftPicked(int delta)
    {
        if (_pickedModel is null) return false;
        if (GroupOf(_pickedModel) is not EditorGroup g || g == EditorGroup.Available) return false;

        var list = ListFor(g);
        var i = list.IndexOf(_pickedModel);
        if (i < 0) return false;

        var target = i + delta;
        if (target >= 0 && target < list.Count)
        {
            // reorder within the group
            list.RemoveAt(i);
            list.Insert(target, _pickedModel);
        }
        else if (g == EditorGroup.Primary && delta > 0)
        {
            _primary.RemoveAt(i);
            _pickedModel.Alignment = ToolbarItemAlignment.Right;
            _secondary.Insert(0, _pickedModel);
        }
        else if (g == EditorGroup.Secondary && delta < 0)
        {
            _secondary.RemoveAt(i);
            _pickedModel.Alignment = ToolbarItemAlignment.Left;
            _primary.Add(_pickedModel);
        }
        else
        {
            return false; // outer edge: nowhere to go
        }

        _justMoved = _pickedModel;
        _focusAfterRender = _pickedModel; // stays picked (checked) + focused after the re-render
        Commit();
        return true;
    }


    /// <summary>
    /// Promotes the selected available button up into the Current section (appended to Primary),
    /// keeping it selected so the user can keep moving it. Returns whether a button was promoted.
    /// </summary>
    private bool PromotePickedToCurrent()
    {
        if (_pickedModel is null || GroupOf(_pickedModel) != EditorGroup.Available) return false;

        var clone = _pickedModel.IsSeparator ? ToolbarItemModel.Separator : Clone(_pickedModel);
        clone.Alignment = ToolbarItemAlignment.Left;
        _primary.Add(clone);

        _pickedModel = clone; // selection follows the button into Current
        _justMoved = clone;
        _focusAfterRender = clone;
        Commit();
        return true;
    }


    /// <summary>
    /// Sends the selected current button down into the Available list (removing it from the toolbar),
    /// keeping it selected on its Available entry so the user can keep moving it. Returns whether a
    /// button was demoted.
    /// </summary>
    private bool DemotePickedToAvailable()
    {
        if (_pickedModel is null) return false;

        var g = GroupOf(_pickedModel);
        if (g is not (EditorGroup.Primary or EditorGroup.Secondary)) return false;

        var model = _pickedModel;
        ListFor(g.Value).Remove(model);
        RecomputeAvailable(); // the button reappears in Available (by id, or the separator template)

        // keep the selection on the same button, now represented in the Available list
        _pickedModel = model.IsSeparator
            ? _available.FirstOrDefault(m => m.IsSeparator)
            : _available.FirstOrDefault(m => m.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
        _justMoved = _pickedModel;
        _focusAfterRender = _pickedModel;

        RenderAll();
        ButtonsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }


    /// <summary>
    /// Returns which working list currently holds the given model, or <c>null</c>.
    /// </summary>
    private EditorGroup? GroupOf(ToolbarItemModel model)
    {
        if (_primary.Contains(model)) return EditorGroup.Primary;
        if (_secondary.Contains(model)) return EditorGroup.Secondary;
        if (_available.Contains(model)) return EditorGroup.Available;
        return null;
    }


    /// <summary>
    /// Removes a current button (keyboard), keeping focus on a neighbour, or the first button if the
    /// group is now empty.
    /// </summary>
    private bool RemoveByKeyboard(ToolbarItemModel model, EditorGroup group)
    {
        var list = ListFor(group);
        var i = list.IndexOf(model);
        if (i < 0) return false;

        if (ReferenceEquals(_pickedModel, model)) _pickedModel = null;
        list.Remove(model);
        _focusAfterRender = list.Count > 0 ? list[Math.Clamp(i, 0, list.Count - 1)] : null;
        var emptied = _focusAfterRender is null;
        Commit();

        if (emptied) Dispatcher.UIThread.Post(FocusFirstButton, DispatcherPriority.Input);
        return true;
    }


    /// <summary>
    /// Focuses the first button in the editor (Primary, else Secondary, else Available). Called when
    /// the page is navigated to so keyboard users land on the Current buttons.
    /// </summary>
    public void FocusFirstButton()
        => (FirstChip(PART_PrimaryGroup) ?? FirstChip(PART_SecondaryGroup) ?? FirstChip(PART_AvailableGroup))
            ?.Focus(NavigationMethod.Tab);


    /// <summary>
    /// Focuses the chip currently bound to the given model (across all three groups).
    /// </summary>
    private void FocusChipFor(ToolbarItemModel model)
    {
        foreach (var panel in new Panel[] { PART_PrimaryGroup, PART_SecondaryGroup, PART_AvailableGroup })
        {
            foreach (var c in panel.Children)
            {
                if (ReferenceEquals(c.Tag, model)) { c.Focus(NavigationMethod.Tab); return; }
            }
        }
    }


    private static List<Control> ChipsOf(Panel panel)
        => panel.Children.Where(c => c.Tag is ToolbarItemModel).ToList();

    private static Control? FirstChip(Panel panel)
        => panel.Children.FirstOrDefault(c => c.Tag is ToolbarItemModel);

    private static Control? LastChip(Panel panel)
        => panel.Children.LastOrDefault(c => c.Tag is ToolbarItemModel);

    #endregion // Keyboard navigation

}
