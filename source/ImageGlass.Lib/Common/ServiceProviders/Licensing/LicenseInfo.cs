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
using System.Text.Json.Serialization;

namespace ImageGlass.Common.ServiceProviders.Licensing;


/// <summary>
/// Source-generated JSON context for the license model.
/// </summary>
[JsonSerializable(typeof(LicenseInfo))]
public partial class LicenseJsonContext : JsonSerializerContext;


/// <summary>
/// A Pro license. Every field except <see cref="Signature"/> is covered by the signature.
/// </summary>
public sealed class LicenseInfo
{
    [JsonPropertyName("product")]
    public string Product { get; set; } = string.Empty;

    [JsonPropertyName("licenseVersion")]
    public int LicenseVersion { get; set; }

    [JsonPropertyName("licenseId")]
    public string LicenseId { get; set; } = string.Empty;

    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("organizationName")]
    public string? OrganizationName { get; set; }

    [JsonPropertyName("plan")]
    public string Plan { get; set; } = string.Empty;

    [JsonPropertyName("versionScope")]
    public string VersionScope { get; set; } = string.Empty;

    [JsonPropertyName("seatCount")]
    public int SeatCount { get; set; }

    [JsonPropertyName("supportLevel")]
    public string SupportLevel { get; set; } = string.Empty;

    [JsonPropertyName("purchaseDate")]
    public string PurchaseDate { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("sourceLicense")]
    public bool SourceLicense { get; set; }

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}
