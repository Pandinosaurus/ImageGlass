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

namespace ImageGlass.Common.Windows;

/// <summary>
/// The "Edit" settings page: saving, clipboard, and editing-apps options. The UI is authored in
/// <see cref="EditSettingsView"/> (XAML); this page wraps it so it plugs into the shared
/// <see cref="SettingsPage"/> navigation/search infrastructure.
/// </summary>
public sealed class EditSettingsPage : SettingsPage
{
    public EditSettingsPage(SettingsViewModel vm, string navId) : base(vm, navId)
    {
    }


    protected override Control BuildContent() => new EditSettingsView(VM, NavId, NavLabel);
}
