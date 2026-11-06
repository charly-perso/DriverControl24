#!/bin/bash
# Construit le setup macOS (osx-x64) du driver Control24 pour Reaper : DriverReaperControl24-osx-x64-<version>.pkg
# Installe les fichiers dans ~/Library/Application Support/REAPER/UserPlugins de l'utilisateur qui lance l'installation.
# Usage : ./Setup/build-osx-x64.sh [version]
set -euo pipefail

VERSION="${1:-1.0.0}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$HERE/../.."
DRIVER_PROJ="$REPO/DriverReaperControl24/DriverReaperControl24.vbproj"
BRIDGE_PROJ="$REPO/ReaperC24Bridge/ReaperC24Bridge.csproj"
DRIVER_PUB="$HERE/out/publish-driver"      # publication autonome (runtime .NET embarqué), refaite à chaque build
BRIDGE_PUB="$REPO/ReaperC24Bridge/bin/Release/net10.0/osx-x64/publish"
EVENTS_DYLIB="$REPO/NativeReaperPluginDLL/build/reaper_c24events.dylib"
INI="$REPO/ReaperDriverControl24.ini"
OUT="$HERE/out"
STAGE="$OUT/root/Library/Application Support/Control24/ReaperPlugin"   # contenu copié par le postinstall
SCRIPTS="$OUT/scripts"

# Un dossier out/ créé par root (ancien installeur) bloquerait le nettoyage
rm -rf "$OUT" || { echo "Impossible de supprimer $OUT : sudo rm -rf \"$OUT\"" >&2; exit 1; }
mkdir -p "$STAGE" "$SCRIPTS"

# Publications : driver (autonome) toujours ; bridge NativeAOT seulement s'il n'a pas déjà été publié
dotnet publish "$DRIVER_PROJ" -r osx-x64 -c Release --self-contained -o "$DRIVER_PUB"
[ -e "$BRIDGE_PUB/reaper_c24bridge.dylib" ] || dotnet publish "$BRIDGE_PROJ" -r osx-x64 -c Release

for f in "$DRIVER_PUB" "$BRIDGE_PUB/reaper_c24bridge.dylib" "$EVENTS_DYLIB" "$INI"; do
  [ -e "$f" ] || { echo "Introuvable : $f" >&2; exit 1; }
done

# 1. Driver (publication) — sans symboles de debug
rsync -a --exclude '*.pdb' --exclude '*.dSYM' "$DRIVER_PUB/" "$STAGE/"
# 2. Bridge Reaper (NativeAOT)
rsync -a --exclude '*.pdb' --exclude '*.dSYM' "$BRIDGE_PUB/" "$STAGE/"
# 3. Plugin natif d'événements
cp "$EVENTS_DYLIB" "$STAGE/"
# 4. Configuration par défaut (copiée seulement si l'utilisateur n'en a pas déjà une)
#    La copie livrée a VSDEBUG=False : le driver démarre automatiquement depuis Reaper.
sed -E 's/^(VSDEBUG)=.*/\1=False/I' "$INI" > "$STAGE/ReaperDriverControl24.ini.default"
rm -f "$STAGE/ReaperDriverControl24.ini"
chmod +x "$STAGE/DriverReaperControl24"

cat > "$SCRIPTS/postinstall" <<'POST'
#!/bin/bash
SRC="/Library/Application Support/Control24/ReaperPlugin"
USER_NAME="$(stat -f%Su /dev/console)"
USER_HOME="$(dscl . -read "/Users/$USER_NAME" NFSHomeDirectory | awk '{print $2}')"
DEST="$USER_HOME/Library/Application Support/REAPER/UserPlugins"
mkdir -p "$DEST"
for f in "$SRC"/*; do
  name="$(basename "$f")"
  [ "$name" = "ReaperDriverControl24.ini.default" ] && continue
  rm -rf "$DEST/$name"
  cp -R "$f" "$DEST/$name"
done
# Configuration : ne jamais écraser celle de l'utilisateur
[ -f "$DEST/ReaperDriverControl24.ini" ] || cp "$SRC/ReaperDriverControl24.ini.default" "$DEST/ReaperDriverControl24.ini"
chmod +x "$DEST/DriverReaperControl24"
# Retire la quarantaine (paquet non signé)
xattr -dr com.apple.quarantine "$DEST/DriverReaperControl24" "$DEST"/*.dylib "$DEST"/*.dll 2>/dev/null
chown -R "$USER_NAME" "$DEST"
exit 0
POST
chmod +x "$SCRIPTS/postinstall"

pkgbuild --root "$OUT/root" --scripts "$SCRIPTS" --identifier com.control24.driverreaper \
  --version "$VERSION" --install-location / "$OUT/DriverReaperControl24-osx-x64-$VERSION.pkg"
echo "OK -> $OUT/DriverReaperControl24-osx-x64-$VERSION.pkg"
