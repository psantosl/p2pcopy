#!/bin/sh
# Fills the Homebrew formula and Scoop manifest templates from a SHA256SUMS file.
# usage: packaging/render.sh <version> <SHA256SUMS> <output dir>
set -eu
version="$1"; sums="$2"; out="$3"
here=$(dirname "$0")
sha() { grep " p2pcopy-$version-$1\.\(tar\.gz\|zip\)\$" "$sums" | awk '{print $1}'; }
mkdir -p "$out"
sed -e "s/@VERSION@/$version/g" \
    -e "s/@SHA_OSX_ARM64@/$(sha osx-arm64)/" \
    -e "s/@SHA_OSX_X64@/$(sha osx-x64)/" \
    -e "s/@SHA_LINUX_ARM64@/$(sha linux-arm64)/" \
    -e "s/@SHA_LINUX_X64@/$(sha linux-x64)/" \
    "$here/homebrew/p2pcopy.rb" > "$out/p2pcopy.rb"
sed -e "s/@VERSION@/$version/g" \
    -e "s/@SHA_WIN_X64@/$(sha win-x64)/" \
    -e "s/@SHA_WIN_ARM64@/$(sha win-arm64)/" \
    "$here/scoop/p2pcopy.json" > "$out/p2pcopy.json"
echo "rendered $out/p2pcopy.rb and $out/p2pcopy.json"
