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

using ImageGlass.Common.Types.JsonTypeConverters;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common;

public partial class BHelper
{

    public static JsonSerializerOptions CreateJsonOptions()
    {
        return new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,

            Converters =
            {
                new JsonDateTimeConverter(),
            },

            // ignoring policy
            IgnoreReadOnlyProperties = true,
            IgnoreReadOnlyFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }


    /// <summary>
    /// Reads a JSON file into a <see cref="JsonDocument"/>.
    /// </summary>
    public static JsonDocument? ReadJsonDocFromFile(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath)) return null;

        using var stream = File.OpenRead(jsonFilePath);

        return JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
    }


    /// <summary>
    /// Reads JSON file and parses to object.
    /// </summary>
    public static T? ReadJsonFromFile<T>(string jsonFilePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var stream = File.OpenRead(jsonFilePath);

        return JsonSerializer.Deserialize<T>(stream, jsonTypeInfo);
    }


    /// <summary>
    /// Reads JSON file and parses to object.
    /// </summary>
    public static async Task<T?> ReadJsonFromFileAsync<T>(string jsonFilePath, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var stream = File.OpenRead(jsonFilePath);

        return await JsonSerializer.DeserializeAsync<T>(stream, jsonTypeInfo);
    }


    /// <summary>
    /// Writes an object value to JSON file.
    /// </summary>
    public static async Task WriteJsonToFileAsync<T>(string jsonFilePath, T value, JsonTypeInfo<T> jsonTypeInfo, CancellationToken token = default)
    {
        var jsonString = JsonSerializer.Serialize(value, jsonTypeInfo);

        await File.WriteAllTextAsync(jsonFilePath, jsonString, Encoding.UTF8, token);
    }
}

