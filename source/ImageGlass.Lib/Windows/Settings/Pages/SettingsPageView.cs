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
using Avalonia.Controls;
using ImageGlass.Common.Localization;
using ImageGlass.UI;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ImageGlass.Common.Windows;

/// <summary>
/// Base class for every settings page view (the XAML content of one settings tab).
/// <para>
/// It centralizes everything the pages share so it is written once: the working-copy
/// <see cref="SettingsViewModel"/>, the search-index registration, the localized
/// control-binding helpers (toggles, numeric/text inputs, enum dropdowns, link buttons),
/// and the language-change refresh plumbing.
/// </para>
/// <para>
/// To add a new page: derive from this class, set the XAML root element to
/// <c>windows:SettingsPageView</c>, call <see cref="Initialize"/> from the
/// <c>(vm, navId, pageLabel)</c> constructor, and create the rows in <see cref="Build"/>
/// using the <c>Bind*</c> helpers. Do NOT re-implement those helpers on the page.
/// </para>
/// </summary>
public abstract class SettingsPageView : PhControl
{
    /// <summary>
    /// Gets the staging working-copy view model the page binds to.
    /// </summary>
    protected SettingsViewModel VM { get; private set; } = null!;

    /// <summary>
    /// Gets the nav id of the hosting page.
    /// </summary>
    protected string NavId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the localized label key of the hosting page (used for search breadcrumbs).
    /// </summary>
    protected LangId? PageLabel { get; private set; }

    // re-applies localized text to controls that don't self-refresh (buttons, combo items)
    private readonly List<Action> _langRefreshers = [];


    /// <summary>
    /// Wires the page to its working copy and builds the rows. Call from the derived
    /// <c>(vm, navId, pageLabel)</c> constructor right after <c>InitializeComponent()</c>.
    /// </summary>
    protected void Initialize(SettingsViewModel vm, string navId, LangId? pageLabel)
    {
        VM = vm;
        NavId = navId;
        PageLabel = pageLabel;
        Build();
    }


    /// <summary>
    /// Creates and binds the page's setting rows, using the <c>Bind*</c> helpers below.
    /// </summary>
    protected abstract void Build();


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        // PhTextBlock labels refresh themselves; controls registered below need a nudge
        foreach (var refresh in _langRefreshers) refresh();
    }



    #region Binding helpers

    /// <summary>
    /// Registers a callback that re-applies localized text on language change, and runs it once now.
    /// </summary>
    protected void AddLangRefresher(Action refresh)
    {
        _langRefreshers.Add(refresh);
        refresh();
    }


    /// <summary>
    /// Sets a button's text to a localized string and keeps it refreshed on language change.
    /// </summary>
    protected void SetLocalizedText(PhButton btn, LangId key)
        => AddLangRefresher(() => btn.Text = Core.Lang[key]);


    /// <summary>
    /// Binds a checkbox to a boolean config id (staged on change).
    /// </summary>
    protected void BindToggle(CheckBox chk, ConfigId id, LangId label, LangId? section, bool defaultValue = false)
    {
        chk.IsChecked = VM.GetValue(id, defaultValue);
        chk.IsCheckedChanged += (_, _) => VM.SetValue(id, chk.IsChecked ?? false);

        Register(chk, label, id, section);
    }


    /// <summary>
    /// Binds a text box to an integer config id (staged on valid change).
    /// </summary>
    protected void BindIntInput(PhTextBox box, ConfigId id, LangId label, LangId? section, int defaultValue = 0)
    {
        box.Text = VM.GetValue(id, defaultValue).ToString(CultureInfo.InvariantCulture);
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var v)) VM.SetValue(id, v);
        };

        Register(box, label, id, section);
    }


    /// <summary>
    /// Binds a text box to an unsigned-integer config id (staged on valid change).
    /// </summary>
    protected void BindUIntInput(PhTextBox box, ConfigId id, LangId label, LangId? section, uint defaultValue = 0)
    {
        box.Text = VM.GetValue(id, defaultValue).ToString(CultureInfo.InvariantCulture);
        box.TextChanged += (_, _) =>
        {
            if (uint.TryParse(box.Text, out var v)) VM.SetValue(id, v);
        };

        Register(box, label, id, section);
    }


    /// <summary>
    /// Binds a text box to a double config id (staged on valid change).
    /// </summary>
    protected void BindDoubleInput(PhTextBox box, ConfigId id, LangId label, LangId? section, double defaultValue = 0)
    {
        box.Text = VM.GetValue(id, defaultValue).ToString(CultureInfo.InvariantCulture);
        box.TextChanged += (_, _) =>
        {
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                VM.SetValue(id, v);
        };

        Register(box, label, id, section);
    }


    /// <summary>
    /// Populates an enum dropdown with localized labels (from the <c>{EnumType}_{Value}</c>
    /// language key, falling back to the raw name) and binds the selection to a config id.
    /// </summary>
    protected void BindEnumDropdown<TEnum>(ComboBox combo, ConfigId id, TEnum defaultValue,
        LangId label, LangId? section) where TEnum : struct, Enum
    {
        var current = VM.GetValue(id, defaultValue);
        var selectedIndex = 0;

        var names = Enum.GetNames<TEnum>();
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var value = Enum.Parse<TEnum>(name);
            var item = new ComboBoxItem { Tag = value };

            BindComboItemText(item, Lang.GetKey($"{typeof(TEnum).Name}_{name}"), name);
            combo.Items.Add(item);
            if (EqualityComparer<TEnum>.Default.Equals(value, current)) selectedIndex = i;
        }
        combo.SelectedIndex = selectedIndex;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: TEnum value }) VM.SetValue(id, value);
        };

        Register(combo, label, id, section);
    }


    /// <summary>
    /// Sets a combo item's display text to a localized string (falling back to
    /// <paramref name="fallback"/> when there's no key) and keeps it refreshed on language change.
    /// </summary>
    protected void BindComboItemText(ComboBoxItem item, LangId? key, string fallback)
        => AddLangRefresher(() => item.Content = key is { } k ? Core.Lang[k] : fallback);


    /// <summary>
    /// Configures a link-style button: localized text (kept refreshed), full-path tooltip, click action.
    /// </summary>
    protected void BindLink(PhButton btn, LangId label, string tooltip, Action onClick)
    {
        SetLocalizedText(btn, label);
        ToolTip.SetTip(btn, tooltip);
        btn.Click += (_, _) => onClick();

        Register(btn, label, null, null);
    }


    /// <summary>
    /// Registers a setting row into the shared search index.
    /// </summary>
    protected void Register(Control target, LangId label, ConfigId? id, LangId? section)
    {
        VM.Index.Register(new SettingItem
        {
            Id = id,
            Label = label,
            PageNavId = NavId,
            Page = PageLabel,
            Section = section,
            Target = target,
        });
    }

    #endregion // Binding helpers

}
