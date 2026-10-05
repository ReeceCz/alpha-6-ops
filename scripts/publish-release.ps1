<#
.SYNOPSIS
Builds the desktop installer, hashes it, and (only when asked) publishes it as a GitHub Release on the fork.

.DESCRIPTION
Without -Upload it only builds and prints what it would do; nothing leaves this PC. With -Upload it creates a
public pre-release `desktop-v<version>` on ReeceCz/alpha-6-ops with the installer attached. The tag points at the
current commit, which must already be pushed. It then prints the three Render environment values that switch on
the portal's Download page. It never changes Render itself.

The version comes from src/Alpha6Ops.Desktop/Alpha6Ops.Desktop.csproj. packaging/build-desktop.ps1 is called
as is (coordinate changes to it with Dan).

.EXAMPLE
powershell -File scripts/publish-release.ps1                 # build + hash, dry run
powershell -File scripts/publish-release.ps1 -SkipBuild      # hash the existing outputs/ installer, dry run
powershell -File scripts/publish-release.ps1 -SkipBuild -Upload
#>
param(
    [switch]$SkipBuild,
    [switch]$Upload,
    [string]$Installer = '',
    [string]$Repo = 'ReeceCz/alpha-6-ops',
    [string]$SimConnectDll = ''
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/Alpha6Ops.Desktop/Alpha6Ops.Desktop.csproj') -Raw
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not ($version -match '^\d+\.\d+\.\d+$')) { throw "Could not read a x.y.z <Version> from the desktop project (got '$version')." }

if (-not $SkipBuild) {
    $buildArgs = @{}
    if ($SimConnectDll) { $buildArgs.SimConnectDll = $SimConnectDll }
    & (Join-Path $root 'packaging/build-desktop.ps1') @buildArgs
}

if (-not $Installer) { $Installer = Join-Path $root "outputs/Sierra4-Setup-$version.exe" }
if (-not (Test-Path -LiteralPath $Installer -PathType Leaf)) {
    throw "Installer not found at '$Installer'. packaging/build-desktop.ps1 names its output after the version it has hard-coded; make sure that matches $version."
}
$fileName = [IO.Path]::GetFileName($Installer)
$sha = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
$sizeMb = [Math]::Round((Get-Item -LiteralPath $Installer).Length / 1MB, 1)
$tag = "desktop-v$version"
$url = "https://github.com/$Repo/releases/download/$tag/$fileName"

Write-Host ''
Write-Host "S4 OPS desktop $version"
Write-Host "  Installer : $Installer ($sizeMb MB)"
Write-Host "  SHA-256   : $sha"
Write-Host "  Release   : $tag on $Repo"
Write-Host "  URL       : $url"

if ($Upload) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI (gh) is required for -Upload.' }
    $commit = (git -C $root rev-parse HEAD).Trim()
    $notes = "Test build of the S4 OPS desktop app for Microsoft Flight Simulator 2024.`n`nSHA-256 of $fileName`:`n$sha`n`nNot code-signed yet: Windows will warn about an unknown publisher. Check the hash first (Get-FileHash .\$fileName)."
    & gh release create $tag $Installer --repo $Repo --target $commit --prerelease --title "S4 OPS $version (test build)" --notes $notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed. If it says the commit is unknown, push $commit to $Repo first." }
} else {
    Write-Host ''
    Write-Host 'Dry run: nothing was uploaded. Re-run with -Upload to create the GitHub pre-release.'
}

Write-Host ''
Write-Host 'Then set these on the Render service (Environment), and the Download page goes live:'
Write-Host "  Release__Version     = $version"
Write-Host "  Release__DownloadUrl = $url"
Write-Host "  Release__Sha256      = $sha"
