#!/usr/bin/env bash
# Copy the plugin to a local Rust test server and print the compile/load result.
# Usage: RUST_TEST_SERVER=/path/to/RustServer tools/deploy-test.sh
set -u
S="${RUST_TEST_SERVER:?set RUST_TEST_SERVER to your test server folder}"
L="$S/server-log.txt"
n=$(($(wc -l < "$L") + 1))
cp "$(dirname "$0")/../plugin/WallpaperStudio.cs" "$S/oxide/plugins/"
for i in $(seq 1 90); do
  out=$(tail -n +$n "$L" | grep -E "WallpaperStudio|Wallpaper Studio" | grep -iE "error|loaded|failed|exception")
  if [ -n "$out" ]; then
    tail -n +$n "$L" | grep -E -A3 "WallpaperStudio|Wallpaper Studio"
    echo "$out" | grep -qi "error|failed|exception" && exit 1
    exit 0
  fi
  sleep 1
done
echo "no result after 90s"; tail -n +$n "$L" | tail -5
