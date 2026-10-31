#!/bin/sh
# p2pcopy installer for Linux and macOS.
#
#   curl -fsSL https://raw.githubusercontent.com/psantosl/p2pcopy/master/install.sh | sh
#
# Options (environment variables):
#   P2PCOPY_VERSION      version to install, e.g. 2.0.0 (default: latest release)
#   P2PCOPY_INSTALL_DIR  where to put the binary (default: /usr/local/bin if writable, else ~/.local/bin)
set -eu

REPO="psantosl/p2pcopy"
VERSION="${P2PCOPY_VERSION:-${1:-latest}}"
INSTALL_DIR="${P2PCOPY_INSTALL_DIR:-}"

say() { printf '%s\n' "$*" >&2; }
die() { say "error: $*"; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || die "'$1' is required"; }

need curl
need tar

os=$(uname -s)
arch=$(uname -m)
case "$os" in
  Linux)
    case "$arch" in
      x86_64|amd64)
        if ldd --version 2>&1 | grep -qi musl; then rid="linux-musl-x64"; else rid="linux-x64"; fi ;;
      aarch64|arm64) rid="linux-arm64" ;;
      *) die "unsupported Linux architecture: $arch" ;;
    esac ;;
  Darwin)
    case "$arch" in
      arm64) rid="osx-arm64" ;;
      x86_64) rid="osx-x64" ;;
      *) die "unsupported macOS architecture: $arch" ;;
    esac ;;
  *) die "unsupported OS: $os (on Windows use install.ps1)" ;;
esac

if [ "$VERSION" = "latest" ]; then
  VERSION=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
    | sed -n 's/.*"tag_name": *"v\{0,1\}\([^"]*\)".*/\1/p' | head -n 1)
  [ -n "$VERSION" ] || die "could not determine the latest release; set P2PCOPY_VERSION"
fi
VERSION="${VERSION#v}"

base="https://github.com/$REPO/releases/download/v$VERSION"
archive="p2pcopy-$VERSION-$rid.tar.gz"

tmp=$(mktemp -d 2>/dev/null || mktemp -d -t p2pcopy)
trap 'rm -rf "$tmp"' EXIT INT TERM

say "Downloading p2pcopy $VERSION for $rid…"
curl -fsSL -o "$tmp/$archive" "$base/$archive" || die "download failed: $base/$archive"
curl -fsSL -o "$tmp/SHA256SUMS" "$base/SHA256SUMS" || die "download failed: $base/SHA256SUMS"

expected=$(grep " $archive\$" "$tmp/SHA256SUMS" | awk '{print $1}')
[ -n "$expected" ] || die "$archive is not listed in SHA256SUMS"
if command -v sha256sum >/dev/null 2>&1; then
  actual=$(sha256sum "$tmp/$archive" | awk '{print $1}')
else
  actual=$(shasum -a 256 "$tmp/$archive" | awk '{print $1}')
fi
[ "$expected" = "$actual" ] || die "checksum mismatch for $archive"

tar -xzf "$tmp/$archive" -C "$tmp"
[ -f "$tmp/p2pcopy" ] || die "archive did not contain the p2pcopy binary"

if [ -z "$INSTALL_DIR" ]; then
  if [ -d /usr/local/bin ] && [ -w /usr/local/bin ]; then
    INSTALL_DIR=/usr/local/bin
  else
    INSTALL_DIR="$HOME/.local/bin"
  fi
fi
mkdir -p "$INSTALL_DIR"
chmod 755 "$tmp/p2pcopy"
mv -f "$tmp/p2pcopy" "$INSTALL_DIR/p2pcopy"

say "Installed $INSTALL_DIR/p2pcopy ($("$INSTALL_DIR/p2pcopy" --version 2>/dev/null || echo "$VERSION"))"
case ":$PATH:" in
  *":$INSTALL_DIR:"*) ;;
  *) say "note: $INSTALL_DIR is not in your PATH. Add it, for example:"
     say "  export PATH=\"$INSTALL_DIR:\$PATH\"" ;;
esac
