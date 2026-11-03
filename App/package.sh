#!/bin/bash
# Builds the macOS app: publishes it, puts it in a "Take a Break Goose.app" bundle and signs it ad
# hoc (Apple Silicon Macs won't run unsigned code at all). Runs on a Mac, since it needs codesign.
# Velopack then packs the bundle into the installer and the update (see .github/workflows/mac-build.yml).
#
# Usage: App/package.sh <arm64|x64> [version]
#   arm64 is for Apple Silicon Macs, x64 for Intel Macs. The version defaults to the one in
#   Directory.Build.props. The app ends up in dist/mac-<arch>/.
set -euo pipefail

# On GitHub Actions, a failure also shows the command and its last output as an annotation on the
# run's summary page, which (unlike the log) can be read without signing in.
if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
    log="$(mktemp)"
    exec > >(tee -a "$log") 2>&1
    # The pause lets the failed command's last output reach the log first.
    trap 'sleep 1; echo "::error title=package.sh failed at line $LINENO::$BASH_COMMAND%0A$(tail -n 20 "$log" | sed "s/%/%25/g" | awk "{printf \"%s%%0A\", \$0}")"' ERR
fi

arch="${1:-}"
case "$arch" in
    arm64 | x64) ;;
    *) echo "Usage: $0 <arm64|x64> [version]" >&2; exit 1 ;;
esac

root="$(cd "$(dirname "$0")/.." && pwd)"
version="${2:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")}"
work="$root/dist/mac-$arch"
app="$work/Take a Break Goose.app"

rm -rf "$work"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

# Self-contained, so friends don't need to install .NET first. Everything goes in Contents/MacOS,
# next to the executable, where .NET looks for it.
dotnet publish "$root/App/TakeABreakGoose.App.csproj" -c Release -r "osx-$arch" --self-contained true \
    -p:Version="$version" -o "$app/Contents/MacOS"
rm -f "$app/Contents/MacOS/"*.pdb

sed "s/{{VERSION}}/$version/g" "$root/App/Info.plist" > "$app/Contents/Info.plist"
cp "$root/App/Assets/goose.icns" "$app/Contents/Resources/goose.icns"

# Ad hoc signing: no Apple Developer ID, so macOS still asks before opening it the first time
# (see the README), but it runs. Every file next to the executable first, then the bundle around
# them: macOS counts everything in Contents/MacOS as code, .NET's .dll files too, and won't sign a
# bundle with unsigned code in it.
find "$app/Contents/MacOS" -type f ! -name "TakeABreakGoose" -exec codesign --force --sign - {} +
codesign --force --sign - "$app"
codesign --verify --strict "$app"

echo "Built $app"
