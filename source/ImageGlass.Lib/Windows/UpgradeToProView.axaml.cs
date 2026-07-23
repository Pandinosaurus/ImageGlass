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
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Svg.Skia;
using ImageGlass.Common.AppThemes;
using ImageGlass.Common.Localization;
using ImageGlass.Common.ServiceProviders.Licensing;
using ImageGlass.Common.Types;
using ImageGlass.UI;
using ImageGlass.UI.Windowing;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ImageGlass.Common.Windows;

public partial class UpgradeToProView : PhControl
{
    public UpgradeToProView()
    {
        InitializeComponent();

        var isPro = Core.IsProEnabled;
        PART_UpgradeBody.IsVisible = !isPro;
        PART_ManageBody.IsVisible = isPro;

        UpdateLogo();
        if (isPro) FillLicenseInfo();

        PART_BtnCompare.Click += (_, _) => OpenUrl("https://imageglass.org/pricing#comparison");
        PART_BtnBuyOnline.Click += (_, _) => OpenUrl("https://imageglass.org/pricing");
        PART_BtnImportLicense.Click += async (_, _) => await ImportLicenseAsync();
        PART_BtnRetrieveEmail.Click += (_, _) => OpenUrl("https://imageglass.org/pro/retrieve");
        PART_BtnChangeLicense.Click += async (_, _) => await ImportLicenseAsync();
        PART_BtnUpgradePlan.Click += (_, _) => OpenUrl("https://imageglass.org/pricing");
    }


    #region Overrides

    protected override void OnIgThemeChanged(ThemePackChangedEventArgs e)
    {
        base.OnIgThemeChanged(e);
        UpdateLogo();
    }


    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        // the license values are not localized, but the "Perpetual" fallback is
        if (Core.IsProEnabled) FillLicenseInfo();

        PART_LblHeading.Text = Core.Lang[Core.IsProEnabled
            ? LangId.Menu_MnuManageProLicense
            : LangId.Menu_MnuUpgradeToPro];
        PART_BtnCompare.Text = Core.Lang[LangId.Menu_MnuUpgradeToPro_CompareFeatures];
    }

    #endregion // Overrides



    #region Methods

    private void FillLicenseInfo()
    {
        var lic = Core.AppLicense;

        PART_ValLicensedTo.Text = lic?.CustomerName ?? string.Empty;
        PART_ValPlan.Text = lic?.Plan ?? string.Empty;
        PART_ValSeats.Text = (lic?.SeatCount ?? 1).ToString();
        PART_ValExpires.Text = string.IsNullOrEmpty(lic?.ExpiresAt)
            ? Core.Lang[LangId.Menu_MnuManageProLicense_Perpetual]
            : FormatDate(lic.ExpiresAt);
        PART_ValLicenseId.Text = lic?.LicenseId ?? string.Empty;
    }


    private void OpenUrl(string url)
    {
        var campaign = Core.IsProEnabled ? "from_manage_license" : "from_upgrade_dialog";
        _ = BHelper.OpenUrlAsync(this, url, campaign);
    }


    // pick a license file, verify its signature, install it and offer a restart
    private async Task ImportLicenseAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var owner = topLevel as PhWindow;
        var heading = Core.Lang[Core.IsProEnabled
            ? LangId.Menu_MnuManageProLicense
            : LangId.Menu_MnuUpgradeToPro];

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Core.Lang[LangId.Menu_MnuManageProLicense_LicenseFileType])
                {
                    Patterns = ["*" + LicenseService.LICENSE_FILE_EXTENSION],
                },
            ],
        });

        var path = (files.Count > 0 ? files[0] : null)?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        // the signature is the source of truth; reject anything that doesn't verify
        if (!LicenseService.TryVerify(path, out var lic))
        {
            await ShowError(owner, heading, LangId.Menu_MnuManageProLicense_ImportFailed);
            return;
        }

        // authentic but out of validity (expired past grace) can't activate Pro
        if (!LicenseService.IsWithinValidity(lic))
        {
            await ShowError(owner, heading, LangId.Menu_MnuManageProLicense_ImportExpired);
            return;
        }

        // copy into the user config dir so LoadActive() picks it up next launch
        try
        {
            var destPath = BHelper.ConfigDir(lic.LicenseId + LicenseService.LICENSE_FILE_EXTENSION);
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(path, destPath, true);
            }
        }
        catch
        {
            await ShowError(owner, heading, LangId.Menu_MnuManageProLicense_ImportFailed);
            return;
        }

        // the license only loads at startup, so offer to restart now
        var result = await ModalWindow.ShowInfoAsync(owner, new ModalWindowOptions
        {
            Title = heading,
            Heading = heading,
            Description = Core.Lang[LangId.Menu_MnuManageProLicense_ImportSuccess],
        }, ModalWindowButton.Yes_No);

        if (result.ExitCode == DialogExitCode.OK) BHelper.RestartApp();
    }


    private static async Task ShowError(PhWindow? owner, string heading, LangId messageKey)
    {
        await ModalWindow.ShowErrorAsync(owner, new ModalWindowOptions
        {
            Title = heading,
            Heading = heading,
            Description = Core.Lang[messageKey],
        });
    }


    private static string FormatDate(string iso)
    {
        return DateTimeOffset.TryParse(iso, out var dt)
            ? dt.ToLocalTime().ToString("yyyy-MM-dd")
            : iso;
    }


    private void UpdateLogo()
    {
        if (PART_Logo is null) return;

        // try the theme logo first
        try
        {
            var iconPath = Core.Theme.GetIconPath(IgThemeIcon.AppLogo);
            PART_Logo.Source = new SvgImage
            {
                Source = SvgSource.Load(iconPath),
            };
        }
        catch { }

        // fall back to the default logo
        if (PART_Logo.Source is null)
        {
            using var stream = Resx.GetDefaultWindowIconAsStream();
            if (stream is not null)
            {
                PART_Logo.Source = Bitmap.DecodeToHeight(stream, 256);
            }
        }
    }

    #endregion // Methods

}
