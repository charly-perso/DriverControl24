#!/bin/bash
# Construit le setup macOS (osx-x64) de DriverConfig.Web : Control24 Config.app + DriverConfig.Web-osx-x64.pkg
# Usage : ./Setup/build-osx-x64.sh [version]
set -euo pipefail

VERSION="${1:-1.0.0}"
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$HERE/.."
OUT="$HERE/out"
APP="$OUT/root/Applications/Control24 Config.app"
URL="http://127.0.0.1:5119"

rm -rf "$OUT"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# 1. Publication autonome (le runtime .NET est embarqué, rien à installer côté utilisateur)
dotnet publish "$PROJ/DriverConfig.Web.csproj" -c Release -r osx-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=none -o "$APP/Contents/Resources/app"

# 2. Lanceur : démarre le serveur local (si absent) puis ouvre le navigateur
cat > "$APP/Contents/MacOS/Control24Config" <<LAUNCH
#!/bin/bash
DIR="\$(cd "\$(dirname "\$0")/../Resources/app" && pwd)"
if curl -s -o /dev/null --max-time 1 $URL; then
  open $URL
  exit 0
fi
( sleep 2; open $URL ) &
cd "\$DIR"
exec "\$DIR/DriverConfig.Web"
LAUNCH
chmod +x "$APP/Contents/MacOS/Control24Config" "$APP/Contents/Resources/app/DriverConfig.Web"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Control24 Config</string>
  <key>CFBundleDisplayName</key><string>Control24 Config</string>
  <key>CFBundleIdentifier</key><string>com.control24.driverconfig.web</string>
  <key>CFBundleExecutable</key><string>Control24Config</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
</dict></plist>
PLIST

# 3. Icône (.ico -> .icns) si possible
ICO="$PROJ/../digidesign-control-24.ico"
if [ -f "$ICO" ]; then
  TMP="$OUT/icon"; mkdir -p "$TMP/AppIcon.iconset"
  if sips -s format png "$ICO" --out "$TMP/icon.png" >/dev/null 2>&1; then
    sips -z 512 512 "$TMP/icon.png" --out "$TMP/AppIcon.iconset/icon_512x512.png" >/dev/null
    iconutil -c icns "$TMP/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns" 2>/dev/null || true
  fi
  rm -rf "$TMP"
fi

# 4. Paquet d'installation (installe dans /Applications)
# BundleIsRelocatable=false : sinon l'installeur "met à jour" une copie de même identifiant trouvée ailleurs
# (ex. le dossier de build) au lieu d'écrire dans /Applications.
PLIST_COMP="$OUT/components.plist"
pkgbuild --analyze --root "$OUT/root" "$PLIST_COMP" >/dev/null
/usr/libexec/PlistBuddy -c "Set :0:BundleIsRelocatable false" "$PLIST_COMP"
/usr/libexec/PlistBuddy -c "Set :0:BundleOverwriteAction upgrade" "$PLIST_COMP"
pkgbuild --root "$OUT/root" --component-plist "$PLIST_COMP" --identifier com.control24.driverconfig.web \
  --version "$VERSION" --install-location / "$OUT/DriverConfig.Web-osx-x64-$VERSION.pkg"

echo "OK -> $OUT/DriverConfig.Web-osx-x64-$VERSION.pkg"
