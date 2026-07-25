# Windows MSIX packaging

Builds an MSIX of **ImageGlass.Win32** for `x64` and `arm64`, in two flavours:

| Flavour     | Signed? | Identity / Publisher                   | Artwork              | Destination     |
|-------------|---------|----------------------------------------|----------------------|-----------------|
| **msstore** | No      | Store-reserved name + publisher        | `Assets-msstore`     | Microsoft Store |
| **signed**  | Yes     | Plain name + cert Subject as publisher | `Assets-signed`      | GitHub Release  |

The Microsoft Store re-signs packages on submission, so the **msstore** build is
uploaded **unsigned**. The **signed** build (GitHub) is Authenticode-signed —
every payload `.exe`/`.dll` *and* the package itself.

## Files

- [`script-pack-win-msix.ps1`](script-pack-win-msix.ps1) — the packer (PowerShell 7+).
- [`script-generate-msix-assets.ps1`](script-generate-msix-assets.ps1) — renders the
  `Assets-signed` logo set from [`__assets/logo_c_512.png`](../logo_c_512.png).
- [`appxmanifest/AppxManifest.xml`](appxmanifest/AppxManifest.xml) — manifest template
  with `{{...}}` placeholders filled in at pack time.
- [`appxmanifest/Assets-msstore/`](appxmanifest/Assets-msstore/) — Store artwork (used by the msstore build).
- [`appxmanifest/Assets-signed/`](appxmanifest/Assets-signed/) — logo-rendered artwork (used by the signed build).

## Prerequisites

- **Windows 10/11 SDK** — provides `makeappx.exe`, `makepri.exe`, and `signtool.exe`.
  The script auto-locates the newest one under `Windows Kits\10\bin`; no PATH setup needed.
- **.NET 10 SDK** — for `dotnet publish`.
- **Code-signing certificate** (signed flavour only) — installed in
  `CurrentUser\My` / `LocalMachine\My` with its private key, or supplied as a PFX.
- **A signed Pro license** (msstore flavour only) — issue one from the website admin dashboard with
  `channel: msstore`, `versionScope: 10`, `initVersion: <current release>`, no email, no expiry, then
  drop the downloaded `<licenseId>.iglicense.json` into `__artifacts\store-license\` (git-ignored)
  or pass `-StoreLicenseFile`. The pack fails fast without it.

## Usage

Run from VS Code (Terminal -> Run Task) or the CLI:

```powershell
# Microsoft Store (unsigned)
pwsh __assets/win/script-pack-win-msix.ps1 -Platform x64
pwsh __assets/win/script-pack-win-msix.ps1 -Platform arm64

# GitHub Release (signed; cert selected by Subject substring)
pwsh __assets/win/script-pack-win-msix.ps1 -Platform x64   -Sign
pwsh __assets/win/script-pack-win-msix.ps1 -Platform arm64 -Sign

# One .msixbundle holding BOTH x64 + arm64
pwsh __assets/win/script-pack-win-msix.ps1 -Bundle -Sign   # signed, for GitHub
pwsh __assets/win/script-pack-win-msix.ps1 -Bundle         # unsigned, for the Store

# Sign with a PFX instead of a store certificate
pwsh __assets/win/script-pack-win-msix.ps1 -Platform x64 -Sign -CertFile C:\ig.pfx -CertPassword <pw>
```

VS Code tasks:

- **Self-host (GitHub):** `pack-win-x64-msix`, `pack-win-arm64-msix` — a signed `.msix` per arch.
- **Microsoft Store:** `pack-win-msstore-msixbundle` — one unsigned `.msixbundle` (x64 + arm64).
- **Everything:** `pack-win-all` — builds all three.

Output lands in `__artifacts/bundle/`:

- `ImageGlass_<version>_win-x64.msix` / `..._win-arm64.msix` — signed, for GitHub.
- `ImageGlass_<version>_win-msstore.msixbundle` — unsigned bundle, for the Store.

### .msix vs .msixbundle

A `.msixbundle` packs the x64 and arm64 `.msix` together; Windows installs the
architecture matching the device, so you publish one file instead of two. The
per-arch packages inside the bundle are payload-signed (their `.exe`/`.dll` carry
a trust chain) but **not** package-signed — only the `.msixbundle` itself is signed.

## Testing the msstore package locally

The msstore artifact is unsigned **on purpose**, and Windows refuses to install an unsigned
package, so it cannot be tested by double-clicking it.

Do not work around that by setting `Publisher` to your own certificate subject. Windows derives the
publisher id in the package family name from that DN, and `Win32AppIdentity.IsMsStorePackage`
checks it, so a re-published package reports itself as **not** a Store install and Pro stays off:
the test would measure the wrong thing. Both options below keep the identity intact.

**Self-signed with the Store publisher subject** (installs like the real thing):

```powershell
$subject = 'CN=29F1B9EC-D220-4DC3-BEDB-01A9CCA51904'   # must equal the manifest Publisher
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
    -CertStoreLocation Cert:\CurrentUser\My -FriendlyName 'ImageGlass msstore local test' `
    -TextExtension @('2.5.29.19={text}')

# trust it for install (needs admin), then sign a COPY, never the artifact you upload
Export-Certificate -Cert $cert -FilePath "$env:TEMP\ig-store-test.cer" | Out-Null
Import-Certificate -FilePath "$env:TEMP\ig-store-test.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Copy-Item __artifacts\bundle\ImageGlass_*_win-msstore.msixbundle __artifacts\bundle\local-test.msixbundle
signtool sign /fd SHA256 /sha1 $cert.Thumbprint __artifacts\bundle\local-test.msixbundle
Add-AppxPackage __artifacts\bundle\local-test.msixbundle
```

**Or register the loose layout** (no certificate, needs Developer Mode):

```powershell
makeappx unbundle /p __artifacts\bundle\ImageGlass_<version>_win-msstore.msixbundle /d "$env:TEMP\igb"
makeappx unpack   /p "$env:TEMP\igb\ImageGlass-x64.msix" /d "$env:TEMP\igx"
Add-AppxPackage -Register "$env:TEMP\igx\AppxManifest.xml"
```

Either way, check that Help shows **Manage Pro license**, the Pro features are unlocked, and the
licensed-to row shows the bundled license's `customerName`. Afterwards `Remove-AppxPackage` the
package, delete the signed copy, and remove the test certificate from `Cert:\CurrentUser\My` and
`Cert:\LocalMachine\TrustedPeople`.

## Notes

- **Version.** Both flavours use `<Major>.<Minor>.<IgBundleBuild>.0`, derived from
  `Directory.Build.props` (e.g. short `10.0.2` + build `535` -> `10.0.535.0`). The
  build number lives in the 3rd part because the Microsoft Store reserves the 4th
  (revision) part, which must be `0`. Bump `<IgBundleBuild>` per release. Override
  the whole value with `-PackageVersion`.
- **File type associations** are kept in sync with `Const.IMAGE_FORMATS`
  ([`ImageGlass.Lib/Common/Types/Const.cs`](../../ImageGlass.Lib/Common/Types/Const.cs)).
  If that list changes, update the `<uap:FileType>` entries in the manifest template.
- **Signed artwork.** `Assets-signed` is generated from the app logo. Re-run
  `script-generate-msix-assets.ps1` after changing `__assets/logo_c_512.png`; it mirrors
  the `Assets-msstore` filename set so the manifest resolves identically.
- **Publisher must match the certificate.** For the signed build the script reads
  the certificate's exact Subject DN and writes it into the manifest `Publisher`;
  a mismatch makes the package un-installable. For the msstore build the Publisher
  is the Partner-Center-assigned value (`-MsStorePublisher`).
- **No certificate?** The signed build is still produced, just left UNSIGNED (with
  a warning). Sign it before publishing — an unsigned MSIX cannot be installed.
- **Faster iteration.** Pass `-SkipPublish` to reuse an existing
  `__artifacts/publish/win-<arch>` instead of re-publishing.
- **The bundled license is export-only.** The msstore payload carries the signed license in
  `ImageGlass\_store\`, a subfolder the app's license scan never looks in, so it grants nothing by
  itself; the Store identity does that. It exists purely so a Store customer can save a copy for
  their macOS/Linux machines, which is why it is scoped to the major line they bought. The signed
  flavour refuses to build if a `_store` folder is present, so a stale `-SkipPublish` reuse cannot
  leak it into a GitHub package. Anyone can still unzip it out of the Store package: that is
  accepted, and the response to a leak is to bundle a fresh `licenseId` in the next submission.
- **The msstore identity IS the Pro entitlement.** `Win32AppIdentity.IsMsStorePackage` requires the
  running package's Identity Name to equal `-MsStoreIdentityName` **and** its publisher id to equal
  the hash of `-MsStorePublisher`, and `Win32StoreEntitlementProvider` treats that as proof of a Pro
  purchase. So changing either parameter silently turns Pro off for every Store customer; update
  `MSSTORE_IDENTITY_NAME` / `MSSTORE_PUBLISHER_ID` in the same commit
  ([`Win32AppIdentity.cs`](../../ImageGlass.Win32/Common/WinAPI/Win32AppIdentity.cs)). The publisher
  id is the first 8 bytes of the SHA-256 of the publisher DN in UTF-16LE, base32-encoded; it is the
  trailing segment of a package full name, so the simplest way to re-derive it is to read it off an
  installed package.
  The same identity also makes the build skip the license version-scope check
  ([`LicenseScope.IsScopeExempt`](../../ImageGlass.Lib/Common/ServiceProviders/Licensing/LicenseScope.cs)),
  which is what gives a Store customer Pro on every future version on Windows even though the
  bundled file is scoped to major 10. That scope still applies to the exported copy on macOS/Linux.
  This works only while the Store listing stays a **paid app with a time-limited trial**:
  Windows refuses to launch it once the trial lapses, which is what makes "the process is
  running" equivalent to "the customer is licensed". If the listing ever becomes free, or gains
  an unlimited trial, that shortcut has to be replaced with a live Store license query.
