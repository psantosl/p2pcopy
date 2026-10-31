# Homebrew formula for p2pcopy. The release workflow fills in the version and hashes
# and attaches the result to every GitHub release; copy it into a tap repository
# (github.com/<you>/homebrew-tap, file Formula/p2pcopy.rb) and users can run:
#
#   brew install <you>/tap/p2pcopy
class P2pcopy < Formula
  desc "Peer-to-peer file copy behind firewalls, with no server in between"
  homepage "https://github.com/psantosl/p2pcopy"
  version "@VERSION@"
  license "MIT"

  on_macos do
    on_arm do
      url "https://github.com/psantosl/p2pcopy/releases/download/v@VERSION@/p2pcopy-@VERSION@-osx-arm64.tar.gz"
      sha256 "@SHA_OSX_ARM64@"
    end
    on_intel do
      url "https://github.com/psantosl/p2pcopy/releases/download/v@VERSION@/p2pcopy-@VERSION@-osx-x64.tar.gz"
      sha256 "@SHA_OSX_X64@"
    end
  end

  on_linux do
    on_arm do
      url "https://github.com/psantosl/p2pcopy/releases/download/v@VERSION@/p2pcopy-@VERSION@-linux-arm64.tar.gz"
      sha256 "@SHA_LINUX_ARM64@"
    end
    on_intel do
      url "https://github.com/psantosl/p2pcopy/releases/download/v@VERSION@/p2pcopy-@VERSION@-linux-x64.tar.gz"
      sha256 "@SHA_LINUX_X64@"
    end
  end

  def install
    bin.install "p2pcopy"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/p2pcopy --version")
  end
end
