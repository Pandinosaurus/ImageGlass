#!/usr/bin/env bash
#
# Sign and package ImageGlass.app into a Mac App Store .pkg, then optionally
# upload it to App Store Connect.
#
# This is a SEPARATE pipeline from script-pack-mac-arm64-dmg.sh (Developer ID / website).
# It reuses the same unsigned bundle produced by script-pack-mac-arm64-app.sh, but
# signs it with the App Store identities, embeds a provisioning profile, and
# applies the sandbox entitlements. App Store builds are NOT notarized — Apple's
# review replaces notarization.
#
# Prerequisites (one-time):
#   1. "Apple Distribution: Phap Duong (7DV5HBKZ58)"            (signs the .app)
#   2. "3rd Party Mac Developer Installer: Phap Duong (...)"    (signs the .pkg)
#        ^ NOT yet in your keychain. Create a "Mac Installer Distribution"
#          certificate at developer.apple.com and import it.
#   3. A Mac App Store provisioning profile bound to the Apple Distribution cert
#      and the App ID com.duongdieuphap.imageglass, saved to:
#        __assets/mac/appstore/ImageGlass_AppStore.provisionprofile
#
# Run AFTER the app bundle exists (task: pack-mac-arm64-app).
#
# To also upload to App Store Connect, set UPLOAD=1 and provide credentials:
#   UPLOAD=1 APPLE_ID="you@example.com" APPLE_APP_PASSWORD="app-specific-pw" \
#       ./script-pack-mac-arm64-pkg-appstore.sh
# (Or omit UPLOAD and submit __artifacts/dist/*.pkg via the Transporter app.)

set -euo pipefail

# ---------------------------------------------------------------------------
# Configuration (override via environment variables)
# ---------------------------------------------------------------------------
APP_SIGN_IDENTITY="${APP_SIGN_IDENTITY:-Apple Distribution: Phap Duong (7DV5HBKZ58)}"
INSTALLER_SIGN_IDENTITY="${INSTALLER_SIGN_IDENTITY:-3rd Party Mac Developer Installer: Phap Duong (7DV5HBKZ58)}"

WORKSPACE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP_DIR="$WORKSPACE_DIR/__artifacts/bundle/osx-arm64/ImageGlass.app"
ENTITLEMENTS_FILE="$WORKSPACE_DIR/__assets/mac/appstore/ImageGlass.AppStore.entitlements"
PROVISION_PROFILE="${PROVISION_PROFILE:-$WORKSPACE_DIR/__assets/mac/appstore/ImageGlass_AppStore.provisionprofile}"
BUILD_PROPS_FILE="$WORKSPACE_DIR/Directory.Build.props"
OUTPUT_DIR="$WORKSPACE_DIR/__artifacts/dist"

UPLOAD="${UPLOAD:-0}"

# ---------------------------------------------------------------------------
# Sanity checks
# ---------------------------------------------------------------------------
if [[ ! -d "$APP_DIR" ]]; then
	echo "Error: app bundle not found at $APP_DIR" >&2
	echo "       Run the 'pack-mac-arm64-app' task first." >&2
	exit 1
fi
if [[ ! -f "$ENTITLEMENTS_FILE" ]]; then
	echo "Error: entitlements file not found at $ENTITLEMENTS_FILE" >&2
	exit 1
fi
if [[ ! -f "$PROVISION_PROFILE" ]]; then
	echo "Error: provisioning profile not found at $PROVISION_PROFILE" >&2
	echo "       Create a 'Mac App Store' provisioning profile for App ID" >&2
	echo "       com.duongdieuphap.imageglass and save it there." >&2
	exit 1
fi
if ! security find-identity -v -p codesigning | grep -qF "$APP_SIGN_IDENTITY"; then
	echo "Error: app signing identity not found in keychain: $APP_SIGN_IDENTITY" >&2
	exit 1
fi
if ! security find-identity -v | grep -qF "$INSTALLER_SIGN_IDENTITY"; then
	echo "Error: installer signing identity not found in keychain: $INSTALLER_SIGN_IDENTITY" >&2
	echo "       Create a 'Mac Installer Distribution' certificate at" >&2
	echo "       developer.apple.com and import it into the login keychain." >&2
	exit 1
fi

IG_VERSION="$(sed -n 's:.*<IgVersion>\(.*\)</IgVersion>.*:\1:p' "$BUILD_PROPS_FILE" | head -n 1)"
if [[ -z "$IG_VERSION" ]]; then
	echo "Error: could not read IgVersion from $BUILD_PROPS_FILE" >&2
	exit 1
fi

PKG_NAME="ImageGlass_${IG_VERSION}_mac-arm64.pkg"
PKG_PATH="$OUTPUT_DIR/$PKG_NAME"

echo "==> Packaging ImageGlass $IG_VERSION (arm64) for the Mac App Store"
echo "    App identity       : $APP_SIGN_IDENTITY"
echo "    Installer identity : $INSTALLER_SIGN_IDENTITY"
echo "    Provisioning       : $PROVISION_PROFILE"

# ---------------------------------------------------------------------------
# 1. Strip debug artifacts that should not ship (and break signing if signed).
# ---------------------------------------------------------------------------
echo "==> Removing debug artifacts from bundle"
find "$APP_DIR" -type f -name "*.pdb" -delete
find "$APP_DIR" -type d -name "*.dSYM" -exec rm -rf {} +

# ---------------------------------------------------------------------------
# 2. Embed the provisioning profile (App Store requires this inside the .app).
# ---------------------------------------------------------------------------
echo "==> Embedding provisioning profile"
cp "$PROVISION_PROFILE" "$APP_DIR/Contents/embedded.provisionprofile"

# ---------------------------------------------------------------------------
# 2b. Strip extended attributes (com.apple.quarantine, etc.) BEFORE signing.
#     Files copied from downloads (e.g. the provisioning profile) carry the
#     quarantine xattr, which the App Store rejects (ITMS-91109). Must run
#     before codesign so the signature is computed over clean files.
# ---------------------------------------------------------------------------
echo "==> Stripping extended attributes (quarantine, etc.)"
xattr -cr "$APP_DIR"

# ---------------------------------------------------------------------------
# 3. Codesign nested Mach-O binaries first (inside-out), then the bundle.
#    Nested dylibs get only the identity; the main bundle carries the sandbox
#    entitlements. All are signed by the same team, so library validation passes.
# ---------------------------------------------------------------------------
echo "==> Signing nested native libraries"
while IFS= read -r -d '' lib; do
	echo "    sign: ${lib#"$APP_DIR/"}"
	codesign --force --timestamp --options runtime \
		--sign "$APP_SIGN_IDENTITY" "$lib"
done < <(find "$APP_DIR/Contents/MacOS" -type f \( -name "*.dylib" -o -name "*.so" \) -print0)

echo "==> Signing app bundle (sandbox + entitlements + embedded profile)"
codesign --force --timestamp --options runtime \
	--entitlements "$ENTITLEMENTS_FILE" \
	--sign "$APP_SIGN_IDENTITY" "$APP_DIR"

echo "==> Verifying code signature"
codesign --verify --deep --strict --verbose=2 "$APP_DIR"

# ---------------------------------------------------------------------------
# 4. Build the App Store installer package (installs into /Applications).
# ---------------------------------------------------------------------------
echo "==> Building signed .pkg"
mkdir -p "$OUTPUT_DIR"
rm -f "$PKG_PATH"
productbuild \
	--component "$APP_DIR" /Applications \
	--sign "$INSTALLER_SIGN_IDENTITY" \
	"$PKG_PATH"

echo ""
echo "Built: $PKG_PATH"

# ---------------------------------------------------------------------------
# 5. Optionally validate + upload to App Store Connect.
# ---------------------------------------------------------------------------
if [[ "$UPLOAD" != "1" ]]; then
	echo ""
	echo "To submit: set UPLOAD=1 with credentials, or drag the .pkg into Transporter.app."
	exit 0
fi

if [[ -z "${APPLE_ID:-}" || -z "${APPLE_APP_PASSWORD:-}" ]]; then
	echo "Error: UPLOAD=1 requires APPLE_ID and APPLE_APP_PASSWORD (app-specific password)." >&2
	exit 1
fi

echo "==> Validating package with App Store Connect"
xcrun altool --validate-app -f "$PKG_PATH" -t macos \
	--apple-id "$APPLE_ID" --password "$APPLE_APP_PASSWORD"

echo "==> Uploading package to App Store Connect"
xcrun altool --upload-app -f "$PKG_PATH" -t macos \
	--apple-id "$APPLE_ID" --password "$APPLE_APP_PASSWORD"

echo ""
echo "Done: uploaded $PKG_PATH to App Store Connect."
