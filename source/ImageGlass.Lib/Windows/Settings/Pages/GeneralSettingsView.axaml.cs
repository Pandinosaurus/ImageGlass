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
using System;
using System.IO;

namespace ImageGlass.Common.Windows;

/// <summary>
/// The "General" settings page.
/// </summary>
public partial class GeneralSettingsView : SettingsPageView
{
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
        Initialize(vm, navId, pageLabel);
    }


    protected override void Build()
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
    /// Binds a checkbox to the string-based <c>AutoUpdate</c> config (date string vs. "0").
    /// </summary>
    private void BindAutoUpdateToggle(CheckBox chk, ConfigId id, LangId label, LangId? section)
    {
        var current = VM.GetValue(id, "0");
        chk.IsChecked = !string.Equals(current, "0", StringComparison.OrdinalIgnoreCase);
        chk.IsCheckedChanged += (_, _) =>
            VM.SetValue(id, (chk.IsChecked ?? false) ? DateTime.UtcNow.ToString() : "0");

        Register(chk, label, id, section);
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
            // open the JSON file in the app associated with its file type (shell-execute)
            var exitCode = await BHelper.RunExeAsync(filePath, "");
            if (exitCode == 0) return;
        }
        catch { }

        // no associated editor → reveal in explorer instead
        BHelper.OpenFilePath(filePath);
    }

}
