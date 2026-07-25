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
    /// Product name a license must carry to be accepted.
    /// </summary>
    private const string PRODUCT_NAME = "ImageGlass";

    /// <summary>
    /// Schema version stamped on a license the app builds for itself.
    /// </summary>
    private const int LICENSE_SCHEMA_VERSION = 1;

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
    /// Finds the active license: the store entitlement, then the install folder, then the config
    /// folder. Returns the license that should enable Pro, or null to run as Classic. Never throws.
    /// </summary>
    /// <param name="outOfScopeLicense">
    /// The first authentic license that does not cover this app version, so the caller can
    /// name it in the upgrade prompt. Null when there is none.
    /// </param>
    public static LicenseInfo? LoadActive(out LicenseInfo? outOfScopeLicense)
    {
        outOfScopeLicense = null;

        try
        {
            // a store purchase is proven by package identity, so it outranks any file on disk
            var storeLicense = LoadStoreEntitlement();
            if (storeLicense is not null)
            {
                var storeCoversThisApp = LicenseScope.CoversRunningApp(storeLicense);
                if (storeCoversThisApp) return storeLicense;

                outOfScopeLicense = storeLicense;
            }

            // install folder wins, so a machine-wide deployed license takes precedence
            foreach (var dir in new[] { BHelper.BasePath, BHelper.ConfigPath })
            {
                var files = EnumerateLicenseFiles(dir);

                foreach (var path in files)
                {
                    var isAuthentic = TryVerify(path, out var lic);
                    if (!isAuthentic) continue;

                    var isWithinValidity = IsWithinValidity(lic);
                    if (!isWithinValidity) continue;

                    // authentic and current, but bought for another version line
                    var coversThisApp = LicenseScope.CoversRunningApp(lic);
                    if (!coversThisApp)
                    {
                        outOfScopeLicense ??= lic;
                        continue;
                    }

                    return lic;
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
    /// Builds the license for a platform-store install, or null when this is not a store build.
    /// </summary>
    private static LicenseInfo? LoadStoreEntitlement()
    {
        // a faulty store provider must not stop the folder scan from finding a license
        try
        {
            var provider = Core.StoreEntitlementProvider;
            if (provider is null) return null;

            var isStoreEntitled = provider.IsStoreEntitled;
            if (!isStoreEntitled) return null;

            // the bundled file only supplies the details shown to the user; the store grants Pro
            var bundledDir = provider.BundledLicenseDirectory;
            var bundled = TryLoadFirstValidLicense(bundledDir);
            if (bundled is not null) return bundled;

            return CreateStoreEntitlementLicense(provider);
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// Minimal license standing in for a store entitlement when no bundled file can be read. It
    /// carries no signature, so it must never be written to disk or exported. The version scope is
    /// left blank on purpose: the store exemption in <see cref="LicenseScope"/> is the one place
    /// that grants every release.
    /// </summary>
    private static LicenseInfo CreateStoreEntitlementLicense(IStoreEntitlementProvider provider) => new()
    {
        Product = PRODUCT_NAME,
        LicenseVersion = LICENSE_SCHEMA_VERSION,
        Plan = provider.PlanName,
        SeatCount = 1,
        Channel = provider.ChannelId,
    };


    /// <summary>
    /// Returns the newest authentic, in-validity license in a folder. Version scope is left to the
    /// caller.
    /// </summary>
    private static LicenseInfo? TryLoadFirstValidLicense(string? dir)
    {
        var files = EnumerateLicenseFiles(dir);

        foreach (var path in files)
        {
            var isAuthentic = TryVerify(path, out var lic);
            if (!isAuthentic) continue;

            var isWithinValidity = IsWithinValidity(lic);
            if (!isWithinValidity) continue;

            return lic;
        }

        return null;
    }


    /// <summary>
    /// Lists the license files directly in a folder, newest first. Not recursive, so a license in
    /// a subfolder is never picked up by accident. Returns empty on any problem.
    /// </summary>
    private static List<string> EnumerateLicenseFiles(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return [];

        var dirExists = Directory.Exists(dir);
        if (!dirExists) return [];

        List<string> files;
        try
        {
            files = new List<string>(Directory.EnumerateFiles(dir, LICENSE_FILE_PATTERN));
        }
        catch
        {
            return [];
        }

        files.Sort(static (a, b) => LastWrite(b).CompareTo(LastWrite(a)));
        return files;
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
            if (!string.Equals(lic.Product, PRODUCT_NAME, StringComparison.Ordinal)) return false;
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
