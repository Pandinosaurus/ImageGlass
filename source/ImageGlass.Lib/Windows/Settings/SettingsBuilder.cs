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
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImageGlass.Common.Localization;
using ImageGlass.UI;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageGlass.Common.Windows;

/// <summary>
/// Helper that builds themed, localized settings rows bound to a <see cref="SettingsViewModel"/>
/// working copy, and registers each row into the shared <see cref="SettingsIndex"/> for
/// search + navigate-by-config.
/// <para>
/// Controls bind to the VM staging store (not <see cref="Core.Config"/>), so edits only take
/// effect on OK/Apply.
/// </para>
/// </summary>
public sealed class SettingsBuilder
{
    private readonly SettingsViewModel _vm;
    private readonly string _navId;
    private readonly StackPanel _root;
    private LangId? _currentSection;


    public SettingsBuilder(SettingsViewModel vm, string navId)
    {
        _vm = vm;
        _navId = navId;
        _root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
        };
    }


    /// <summary>
    /// Returns the built content panel.
    /// </summary>
    public Control Build() => _root;


    /// <summary>
    /// Adds a section heading. Subsequent rows are tagged with this section for search grouping.
    /// </summary>
    public SettingsBuilder AddSection(LangId label)
    {
        _currentSection = label;
        _root.Children.Add(new PhTextBlock
        {
            LangKey = label,
            Opacity = 0.6,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 2),
        });
        return this;
    }


    /// <summary>
    /// Adds a boolean toggle (checkbox) bound to a config id.
    /// </summary>
    public SettingsBuilder AddToggle(ConfigId id, LangId label)
    {
        var chk = new CheckBox
        {
            IsChecked = _vm.GetValue(id, false),
            Content = new PhTextBlock { LangKey = label, TextWrapping = TextWrapping.Wrap },
        };
        chk.IsCheckedChanged += (_, _) => _vm.SetValue(id, chk.IsChecked ?? false);

        _root.Children.Add(chk);
        Register(id, label, chk);
        return this;
    }


    /// <summary>
    /// Adds a numeric input bound to a config id (stored as <see cref="double"/>).
    /// </summary>
    public SettingsBuilder AddNumber(ConfigId id, LangId label, double min, double max, double step = 1)
    {
        var num = new NumericUpDown
        {
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = (decimal)step,
            Value = (decimal)_vm.GetValue(id, 0d),
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        num.ValueChanged += (_, _) => _vm.SetValue(id, (double)(num.Value ?? 0));

        var row = LabeledRow(label, num);
        _root.Children.Add(row);
        Register(id, label, row);
        return this;
    }


    /// <summary>
    /// Adds a text input bound to a config id.
    /// </summary>
    public SettingsBuilder AddText(ConfigId id, LangId label)
    {
        var txt = new PhTextBox
        {
            Text = _vm.GetValue(id, string.Empty),
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        txt.TextChanged += (_, _) => _vm.SetValue(id, txt.Text ?? string.Empty);

        var row = LabeledRow(label, txt);
        _root.Children.Add(row);
        Register(id, label, row);
        return this;
    }


    /// <summary>
    /// Adds a dropdown bound to a config id. Each option pairs a value with a display text.
    /// </summary>
    public SettingsBuilder AddDropdown<T>(ConfigId id, LangId label,
        IReadOnlyList<(T Value, string Text)> options, T defaultValue)
    {
        var combo = new ComboBox
        {
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var current = _vm.GetValue(id, defaultValue);
        var selectedIndex = 0;
        for (var i = 0; i < options.Count; i++)
        {
            var (value, text) = options[i];
            combo.Items.Add(new ComboBoxItem { Content = text, Tag = value });
            if (EqualityComparer<T>.Default.Equals(value, current)) selectedIndex = i;
        }
        combo.SelectedIndex = selectedIndex;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T value })
            {
                _vm.SetValue(id, value);
            }
        };

        var row = LabeledRow(label, combo);
        _root.Children.Add(row);
        Register(id, label, row);
        return this;
    }


    /// <summary>
    /// Adds a clickable link/button row (non-config; not registered as a searchable setting).
    /// </summary>
    public SettingsBuilder AddLink(LangId label, Action onClick)
    {
        var btn = new PhButton
        {
            Text = Core.Lang[label],
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        btn.Click += (_, _) => onClick();

        _root.Children.Add(btn);
        return this;
    }


    /// <summary>
    /// Wraps a control in a label-left / control-right row.
    /// </summary>
    private static Grid LabeledRow(LangId label, Control control)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*, Auto"),
            ColumnSpacing = 12,
        };

        var lbl = new PhTextBlock
        {
            LangKey = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        return grid;
    }


    private void Register(ConfigId? id, LangId label, Control target)
    {
        _vm.Index.Register(new SettingItem
        {
            Id = id,
            Label = label,
            PageNavId = _navId,
            Section = _currentSection,
            Target = target,
        });
    }
}
