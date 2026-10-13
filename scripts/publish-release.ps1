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

The installer must point at the hosted portal, never at a developer's localhost. The build uses
src/Alpha6Ops.Desktop/alpha6-identity.hosted.json (gitignored; same shape as alpha6-identity.example.json) in place
of alpha6-identity.json, and puts the developer's own file back afterwards. The script then reads the configuration
out of the installer's payload and stops unless ApiBaseUrl and PortalUrl are exactly -PortalUrl.

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
    [string]$SimConnectDll = '',
    [string]$PortalUrl = 'https://app.s4ops.com/',
    [string]$DotNetRoot = ''   # passed to packaging/build-desktop.ps1, e.g. 'C:\Program Files\dotnet' in a worktree without work/dotnet
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# Throws unless the JSON is an identity configuration aimed at the hosted portal.
function Assert-HostedIdentity([string]$json, [string]$where) {
    $config = $json | ConvertFrom-Json
    foreach ($field in 'Authority', 'ClientId', 'Audience', 'ApiBaseUrl', 'PortalUrl') {
        if (-not $config.$field -or $config.$field -match 'YOUR-') { throw "$where has no real $field." }
    }
    foreach ($field in 'ApiBaseUrl', 'PortalUrl') {
        if ($config.$field -ne $PortalUrl) { throw "$where sets $field to '$($config.$field)', not '$PortalUrl'. Testers would not reach the portal." }
    }
}

[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/Alpha6Ops.Desktop/Alpha6Ops.Desktop.csproj') -Raw
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not ($version -match '^\d+\.\d+\.\d+$')) { throw "Could not read a x.y.z <Version> from the desktop project (got '$version')." }

if (-not $SkipBuild) {
    $desktop = Join-Path $root 'src/Alpha6Ops.Desktop'
    $hosted = Join-Path $desktop 'alpha6-identity.hosted.json'
    $active = Join-Path $desktop 'alpha6-identity.json'
    if (-not (Test-Path -LiteralPath $hosted -PathType Leaf)) {
        throw "Missing $hosted. Copy alpha6-identity.example.json there and fill in the Auth0 desktop (Native) application, with ApiBaseUrl and PortalUrl set to $PortalUrl."
    }
    Assert-HostedIdentity ([IO.File]::ReadAllText($hosted)) $hosted
    $developerCopy = $null
    if (Test-Path -LiteralPath $active -PathType Leaf) { $developerCopy = [IO.File]::ReadAllBytes($active) }
    Copy-Item -LiteralPath $hosted -Destination $active -Force
    try {
        $buildArgs = @{}
        if ($SimConnectDll) { $buildArgs.SimConnectDll = $SimConnectDll }
        if ($DotNetRoot) { $buildArgs.DotNetRoot = $DotNetRoot }
        & (Join-Path $root 'packaging/build-desktop.ps1') @buildArgs
    } finally {
        if ($null -ne $developerCopy) { [IO.File]::WriteAllBytes($active, $developerCopy) } else { Remove-Item -LiteralPath $active -Force }
    }
}

# Check what actually ships: the configuration inside the installer's payload.
$payload = Join-Path $root "outputs/Sierra4-Desktop-$version-win-x64.zip"
if (-not (Test-Path -LiteralPath $payload -PathType Leaf)) { throw "Payload not found at '$payload'; build first." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($payload)
try {
    $entry = $zip.Entries | Where-Object { $_.FullName -eq 'alpha6-identity.json' } | Select-Object -First 1
    if (-not $entry) { throw "$payload has no alpha6-identity.json, so the installed app could not sign in." }
    $reader = New-Object IO.StreamReader($entry.Open())
    try { $shipped = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $zip.Dispose() }
Assert-HostedIdentity $shipped "The installer payload's alpha6-identity.json"

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
