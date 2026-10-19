<#
.SYNOPSIS
Builds the desktop installer and uploads it to the private R2 bucket behind the portal's staff-only Builds page.

.DESCRIPTION
The build and its checks are publish-release.ps1's dry run: it builds against alpha6-identity.hosted.json and stops
unless the installer points at the hosted portal. Nothing is published on GitHub. The installer is then uploaded as
preview/Sierra4-Setup-<version>-<commit>.exe with wrangler, so each upload names the commit it was built from.
Staff download it from https://app.s4ops.com/Staff/Builds.

Needs `npx wrangler login` once, and CLOUDFLARE_ACCOUNT_ID set to the account that holds the bucket.

.EXAMPLE
powershell -File scripts/upload-preview-build.ps1                  # build, check, upload
powershell -File scripts/upload-preview-build.ps1 -SkipBuild       # upload the installer already in outputs/
#>
param(
    [switch]$SkipBuild,
    [string]$Bucket = 's4ops-builds',
    [string]$DotNetRoot = '',
    [string]$SimConnectDll = ''
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

$releaseArgs = @{}
if ($SkipBuild) { $releaseArgs.SkipBuild = $true }
if ($DotNetRoot) { $releaseArgs.DotNetRoot = $DotNetRoot }
if ($SimConnectDll) { $releaseArgs.SimConnectDll = $SimConnectDll }
& (Join-Path $PSScriptRoot 'publish-release.ps1') @releaseArgs
Write-Host ''
Write-Host 'Preview build: ignore the Release__ values above. They publish a public release; this upload stays private.'

[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/Alpha6Ops.Desktop/Alpha6Ops.Desktop.csproj') -Raw
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$installer = Join-Path $root "outputs/Sierra4-Setup-$version.exe"
$commit = (git -C $root rev-parse --short=7 HEAD).Trim()
$dirty = git -C $root status --porcelain --untracked-files=no
if ($dirty) { $commit = "$commit-dirty" }
$key = "preview/Sierra4-Setup-$version-$commit.exe"

if (-not $env:CLOUDFLARE_ACCOUNT_ID) { throw 'Set CLOUDFLARE_ACCOUNT_ID to the Cloudflare account that holds the bucket.' }
Write-Host ''
Write-Host "Uploading to r2://$Bucket/$key"
& npx wrangler r2 object put "$Bucket/$key" --file "$installer" --content-type application/vnd.microsoft.portable-executable --remote
if ($LASTEXITCODE -ne 0) { throw 'wrangler r2 object put failed. Run npx wrangler login, and check CLOUDFLARE_ACCOUNT_ID and the bucket name.' }
Write-Host ''
Write-Host 'Uploaded. Download it from https://app.s4ops.com/Staff/Builds'
