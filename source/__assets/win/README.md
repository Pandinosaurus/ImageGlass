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
