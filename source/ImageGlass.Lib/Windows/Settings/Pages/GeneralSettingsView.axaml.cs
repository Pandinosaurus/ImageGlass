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
using System.IO;

namespace ImageGlass.Common.Windows;

/// <summary>
/// XAML UI for the "General" settings page. Wires its controls to the staging
/// <see cref="SettingsViewModel"/> and registers each row into the search index.
/// </summary>
public partial class GeneralSettingsView : PhControl
{
    private readonly SettingsViewModel _vm = null!;
    private readonly string _navId = string.Empty;
    private readonly LangId? _pageLabel;

    // link buttons whose Text must refresh on language change
    private readonly Dictionary<PhButton, LangId> _linkLabels = [];


    /// <summary>
    /// Parameterless constructor for the XAML loader / designer.
    /// </summary>
    public GeneralSettingsView()
    {
        InitializeComponent();
    }


    /// <summary>
    /// Creates the page bound to the given working-copy view model.
    /// </summary>
    public GeneralSettingsView(SettingsViewModel vm, string navId, LangId? pageLabel = null) : this()
    {
        _vm = vm;
        _navId = navId;
        _pageLabel = pageLabel;
        Build();
    }



    #region Override Methods

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        // PhTextBlock labels refresh themselves; only the link button texts need a nudge
        foreach (var (btn, label) in _linkLabels)
        {
            btn.Text = Core.Lang[label];
        }
    }

    #endregion // Override Methods



    #region Methods

    private void Build()
    {
        var startupDir = BHelper.BasePath;
        var configDir = BHelper.ConfigDir();
        var userConfig = BHelper.ConfigDir(Config.CONFIG_USER);

        // Locations
        BindLink(PART_StartupDir, LangId.FrmSettings_StartupDir, startupDir,
            () => BHelper.OpenFolderPath(startupDir));
        BindLink(PART_ConfigDir, LangId.FrmSettings_ConfigDir, configDir,
            () => BHelper.OpenFolderPath(configDir));
        BindLink(PART_UserConfig, LangId.FrmSettings_UserConfigFile, userConfig,
            () => OpenUserConfigFile(userConfig));

        // Startup
        BindToggle(PART_WelcomeImage, ConfigId.EnableWelcomeImage,
            LangId.FrmSettings_EnableWelcomeImage, LangId.FrmSettings_Startup);
        BindToggle(PART_LastSeenImage, ConfigId.EnableLastSeenImage,
            LangId.FrmSettings_EnableLastSeenImage, LangId.FrmSettings_Startup);
        BindToggle(PART_MultiInstances, ConfigId.EnableMultiInstances,
            LangId.FrmSettings_EnableMultiInstances, LangId.FrmSettings_Startup);

        // App update — AutoUpdate is stored as a date string; "0" means disabled.
        BindAutoUpdateToggle(PART_AutoUpdate, ConfigId.AutoUpdate,
            LangId.FrmSettings_AutoUpdate, LangId.FrmSettings_AppUpdate);

        // Others
        BindIntInput(PART_MsgDuration, ConfigId.InAppMessageDuration,
            LangId.FrmSettings_InAppMessageDuration, LangId.FrmSettings_Others);
    }


    /// <summary>
    /// Configures a link-style button: localized text, full-path tooltip, click action.
    /// The link appearance (accent foreground, hand cursor) comes from <see cref="PhButton.IsLink"/>.
    /// </summary>
    private void BindLink(PhButton btn, LangId label, string fullPath, Action onClick)
    {
        _linkLabels[btn] = label;
        btn.Text = Core.Lang[label];
        ToolTip.SetTip(btn, fullPath);
        btn.Click += (_, _) => onClick();

        Register(btn, label, null, null);
    }


    /// <summary>
    /// Binds a checkbox to a boolean config id (staged on change).
    /// </summary>
    private void BindToggle(CheckBox chk, ConfigId id, LangId label, LangId? section)
    {
        chk.IsChecked = _vm.GetValue(id, false);
        chk.IsCheckedChanged += (_, _) => _vm.SetValue(id, chk.IsChecked ?? false);

        Register(chk, label, id, section);
    }


    /// <summary>
    /// Binds a checkbox to the string-based <c>AutoUpdate</c> config (date string vs. "0").
    /// </summary>
    private void BindAutoUpdateToggle(CheckBox chk, ConfigId id, LangId label, LangId? section)
    {
        var current = _vm.GetValue(id, "0");
        chk.IsChecked = !string.Equals(current, "0", StringComparison.OrdinalIgnoreCase);
        chk.IsCheckedChanged += (_, _) =>
            _vm.SetValue(id, (chk.IsChecked ?? false) ? DateTime.UtcNow.ToString() : "0");

        Register(chk, label, id, section);
    }


    /// <summary>
    /// Binds a text box to an integer config id (staged on valid change).
    /// </summary>
    private void BindIntInput(PhTextBox box, ConfigId id, LangId label, LangId? section)
    {
        box.Text = _vm.GetValue(id, 0).ToString();
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var v)) _vm.SetValue(id, v);
        };

        Register(box, label, id, section);
    }


    private void Register(Control target, LangId label, ConfigId? id, LangId? section)
    {
        _vm.Index.Register(new SettingItem
        {
            Id = id,
            Label = label,
            PageNavId = _navId,
            Page = _pageLabel,
            Section = section,
            Target = target,
        });
    }


    /// <summary>
    /// Opens the user config file in the system's default editor.
    /// Falls back to revealing it in the file explorer when no editor is associated.
    /// </summary>
    private static async void OpenUserConfigFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            BHelper.OpenFolderPath(BHelper.ConfigDir());
            return;
        }

        try
        {
            if (Core.ShellProvider is not null)
            {
                await Core.ShellProvider.OpenDefaultEditingAppAsync(filePath);
                return;
            }
        }
        catch { }

        // no associated editor (or no shell provider) → reveal in explorer instead
        BHelper.OpenFilePath(filePath);
    }

    #endregion // Methods

}
