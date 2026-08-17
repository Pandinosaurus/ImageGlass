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
using Avalonia.Input;
using ImageGlass.Common.Types.JsonTypeConverters;
using System.Text.Json.Serialization;

namespace ImageGlass.Common.Types;


[JsonConverter(typeof(JsonStringToHotkeyConverter))]
public class Hotkey
{
    /// <summary>
    /// The default modifier key for "Control" action, which is "Control" key on Windows and "Command (Meta)" key on macOS.
    /// </summary>
    public static readonly KeyModifiers Ctrl = BHelper.OS == OSType.Mac ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>
    /// The default hotkey for "Delete" action, which is "Delete" key on Windows and "Backspace" key on macOS.
    /// </summary>
    public static readonly Key Delete = BHelper.OS == OSType.Mac ? Key.Back : Key.Delete;


    /// <summary>
    /// Gets, sets the virtual key.
    /// </summary>
    public Key Key { get; set; } = Key.None;

    /// <summary>
    /// Gets, sets the key modifiers.
    /// </summary>
    public KeyModifiers Modifiers { get; set; } = KeyModifiers.None;
    public bool Control => Modifiers.HasFlag(KeyModifiers.Control);
    public bool Shift => Modifiers.HasFlag(KeyModifiers.Shift);
    public bool Alt => Modifiers.HasFlag(KeyModifiers.Alt);

    public string KeyString => ToString(this);



    public Hotkey() { }


    public Hotkey(Key key)
    {
        Key = key;
    }


    public Hotkey(KeyModifiers modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }


    public Hotkey(KeyGesture kg)
    {
        Key = kg.Key;
        Modifiers = kg.KeyModifiers;
    }


    public Hotkey(KeyEventArgs e)
    {
        Key = e.Key;
        Modifiers = e.KeyModifiers;
    }


    #region Methods

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public override string ToString() => KeyString;


    /// <summary>
    /// Checks if the provided hotkey is same.
    /// </summary>
    public bool IsSame(Hotkey? hk)
    {
        if (hk is null) return false;

        return IsSame(hk.Key, hk.Modifiers);
    }


    /// <summary>
    /// Checks if the provided hotkey is same.
    /// </summary>
    public bool IsSame(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (Key != key) return false;
        if (Control && !modifiers.HasFlag(KeyModifiers.Control)) return false;
        if (Shift && !modifiers.HasFlag(KeyModifiers.Shift)) return false;
        if (Alt && !modifiers.HasFlag(KeyModifiers.Alt)) return false;

        return true;
    }


    /// <summary>
    /// Converts to <see cref="KeyGesture"/>.
    /// </summary>
    public KeyGesture ToGesture()
    {
        return new KeyGesture(Key, Modifiers);
    }


    /// <summary>
    /// Parses string to <see cref="Hotkey"/> instance.
    /// </summary>
    public static Hotkey? ParseFrom(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        var kg = KeyGesture.Parse(s);
        if (kg is null) return null;

        return new Hotkey(kg);
    }


    /// <summary>
    /// Parse <see cref="Hotkey"/> to string.
    /// </summary>
    public static string ToString(Hotkey hotkey)
    {
        var kg = new KeyGesture(hotkey.Key, hotkey.Modifiers);
        return kg.ToString("p", null);
    }


    #endregion // Methods


}
