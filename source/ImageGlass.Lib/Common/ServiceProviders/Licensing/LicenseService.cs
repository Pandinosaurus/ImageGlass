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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ImageGlass.Common.ServiceProviders.Licensing;


/// <summary>
/// Finds and verifies the active Pro license (local file + embedded key, no network).
/// </summary>
public static class LicenseService
{
    /// <summary>
    /// File-name suffix of a license file.
    /// </summary>
    public const string LICENSE_FILE_EXTENSION = ".iglicense.json";

    private const string LICENSE_FILE_PATTERN = "*.iglicense.json";

    /// <summary>
    /// Days a subscription keeps working past expiry before downgrading.
    /// </summary>
    private const int GRACE_DAYS = 14;

    /// <summary>
    /// Maps a license keyId to its embedded public-key resource. Add keys here to rotate.
    /// </summary>
    private static readonly Dictionary<string, string> _keyResources = new(StringComparer.Ordinal)
    {
        ["ig_license_2026"] = "ig_license_2026.spki.pem",
    };

    private static readonly Dictionary<string, string?> _pemCache = new(StringComparer.Ordinal);
    private static readonly Lock _lock = new();


    /// <summary>
    /// Finds the active license: install folder first, then the config folder. Returns the
    /// license that should enable Pro, or null to run as Classic. Never throws.
    /// </summary>
    public static LicenseInfo? LoadActive()
    {
        try
        {
            // install folder wins, so a machine-wide deployed license takes precedence
            foreach (var dir in new[] { BHelper.BasePath, BHelper.ConfigPath })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                List<string> files;
                try
                {
                    files = new List<string>(Directory.EnumerateFiles(dir, LICENSE_FILE_PATTERN));
                }
                catch
                {
                    continue;
                }

                // newest first
                files.Sort(static (a, b) => LastWrite(b).CompareTo(LastWrite(a)));

                foreach (var path in files)
                {
                    if (TryVerify(path, out var lic) && IsWithinValidity(lic)) return lic;
                }
            }
        }
        catch
        {
            // on any failure, run as Classic
        }

        return null;
    }


    /// <summary>
    /// Parses and verifies a license file's signature. Ignores expiry (see <see cref="IsWithinValidity"/>).
    /// </summary>
    public static bool TryVerify(string filePath, out LicenseInfo license)
    {
        license = null!;

        try
        {
            if (!File.Exists(filePath)) return false;

            var lic = BHelper.ReadJsonFromFile(filePath, LicenseJsonContext.Default.LicenseInfo);
            if (lic is null) return false;
            if (!string.Equals(lic.Product, "ImageGlass", StringComparison.Ordinal)) return false;
            if (string.IsNullOrEmpty(lic.KeyId) || string.IsNullOrEmpty(lic.Signature)) return false;

            var pem = GetPublicKeyPem(lic.KeyId);
            if (pem is null) return false;

            byte[] signature;
            try { signature = Convert.FromBase64String(lic.Signature); }
            catch { return false; }

            var payload = Encoding.UTF8.GetBytes(LicenseSigningPayload.Build(lic));
            if (!LicenseVerifier.Verify(payload, signature, pem)) return false;

            license = lic;
            return true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// True when the license is perpetual or still within the expiry grace period.
    /// </summary>
    public static bool IsWithinValidity(LicenseInfo license)
    {
        if (string.IsNullOrEmpty(license.ExpiresAt)) return true;

        // lenient: an unparseable expiry counts as valid (trust-based)
        if (!DateTimeOffset.TryParse(license.ExpiresAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiresAt))
        {
            return true;
        }

        return DateTimeOffset.UtcNow <= expiresAt.AddDays(GRACE_DAYS);
    }


    private static DateTime LastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }


    /// <summary>
    /// Reads and caches the embedded public-key PEM for a keyId.
    /// </summary>
    private static string? GetPublicKeyPem(string keyId)
    {
        lock (_lock)
        {
            if (_pemCache.TryGetValue(keyId, out var cached)) return cached;

            string? pem = null;
            if (_keyResources.TryGetValue(keyId, out var resourceName))
            {
                try
                {
                    using var stream = typeof(LicenseService).Assembly.GetManifestResourceStream(resourceName);
                    if (stream is not null)
                    {
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        pem = reader.ReadToEnd();
                    }
                }
                catch
                {
                    pem = null;
                }
            }

            _pemCache[keyId] = pem;
            return pem;
        }
    }
}
