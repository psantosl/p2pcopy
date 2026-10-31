# p2pcopy installer for Windows.
#
#   irm https://raw.githubusercontent.com/psantosl/p2pcopy/master/install.ps1 | iex
#
# Options (environment variables):
#   P2PCOPY_VERSION      version to install, e.g. 2.0.0 (default: latest release)
#   P2PCOPY_INSTALL_DIR  where to put p2pcopy.exe (default: %LOCALAPPDATA%\Programs\p2pcopy)
$ErrorActionPreference = 'Stop'

$repo = 'psantosl/p2pcopy'
$version = if ($env:P2PCOPY_VERSION) { $env:P2PCOPY_VERSION } else { 'latest' }
$installDir = if ($env:P2PCOPY_INSTALL_DIR) { $env:P2PCOPY_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\p2pcopy' }

$arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$rid = switch ($arch) {
    'AMD64' { 'win-x64' }
    'ARM64' { 'win-arm64' }
    default { throw "unsupported architecture: $arch" }
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if ($version -eq 'latest') {
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'p2pcopy-installer' }
    $version = $release.tag_name
}
$version = $version.TrimStart('v')

$base = "https://github.com/$repo/releases/download/v$version"
$archive = "p2pcopy-$version-$rid.zip"
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("p2pcopy-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    Write-Host "Downloading p2pcopy $version for $rid..."
    Invoke-WebRequest -UseBasicParsing -Uri "$base/$archive" -OutFile (Join-Path $tmp $archive)
    Invoke-WebRequest -UseBasicParsing -Uri "$base/SHA256SUMS" -OutFile (Join-Path $tmp 'SHA256SUMS')

    $line = Get-Content (Join-Path $tmp 'SHA256SUMS') | Where-Object { $_ -match "\s$([regex]::Escape($archive))$" } | Select-Object -First 1
    if (-not $line) { throw "$archive is not listed in SHA256SUMS" }
    $expected = ($line -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -Algorithm SHA256 (Join-Path $tmp $archive)).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { throw "checksum mismatch for $archive" }

    Expand-Archive -Path (Join-Path $tmp $archive) -DestinationPath $tmp -Force
    $exe = Join-Path $tmp 'p2pcopy.exe'
    if (-not (Test-Path $exe)) { throw 'archive did not contain p2pcopy.exe' }

    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Move-Item -Force $exe (Join-Path $installDir 'p2pcopy.exe')

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($userPath -split ';') -notcontains $installDir) {
        [Environment]::SetEnvironmentVariable('Path', "$installDir;$userPath", 'User')
        $env:Path = "$installDir;$env:Path"
        Write-Host "Added $installDir to your user PATH (open a new terminal to pick it up)."
    }

    Write-Host "Installed $(Join-Path $installDir 'p2pcopy.exe') ($(& (Join-Path $installDir 'p2pcopy.exe') --version))"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
