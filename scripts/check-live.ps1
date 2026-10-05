<#
.SYNOPSIS
Checks the live S4 OPS sites: every link on s4ops.com, the portal's health and release state, and key headers.

.DESCRIPTION
Read-only. Run it after any DNS, Cloudflare, Render or release change. Exit code 0 means everything passed;
1 means at least one check failed (listed at the end). Uses curl.exe, which ships with Windows 10 and 11.

.EXAMPLE
powershell -File scripts/check-live.ps1
powershell -File scripts/check-live.ps1 -Site http://127.0.0.1:8787   # a local `wrangler dev`
#>
param(
    [string]$Site = 'https://s4ops.com',
    [string]$App = 'https://app.s4ops.com',
    [int]$TimeoutSeconds = 75   # Render Free can take about a minute to wake up
)
$ErrorActionPreference = 'Stop'
$results = New-Object System.Collections.Generic.List[object]
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('s4-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null

function Get-Url([string]$url) {
    $body = Join-Path $scratch 'body'; $head = Join-Path $scratch 'head'
    $code = & curl.exe -s -o $body -D $head -w '%{http_code}' --max-time $TimeoutSeconds $url
    $curlExit = $LASTEXITCODE
    $text = ''; $headers = ''
    if (Test-Path -LiteralPath $body) { $text = [IO.File]::ReadAllText($body) }
    if (Test-Path -LiteralPath $head) { $headers = [IO.File]::ReadAllText($head) }
    $reason = ''
    if ($curlExit -eq 6) { $reason = 'DNS name does not resolve' }
    elseif ($curlExit -eq 28) { $reason = "no answer within $TimeoutSeconds s" }
    elseif ($curlExit -ne 0) { $reason = "curl exit $curlExit" }
    [pscustomobject]@{ Code = [int]$code; Body = $text; Headers = $headers; Reason = $reason }
}

function Add-Result([string]$check, [bool]$ok, [string]$detail) {
    $status = 'FAIL'
    if ($ok) { $status = 'ok' }
    $results.Add([pscustomobject]@{ Status = $status; Check = $check; Detail = $detail })
}

function Test-Reachable([string]$url) {
    $r = Get-Url $url
    $detail = "HTTP $($r.Code)"
    if ($r.Reason) { $detail = $r.Reason }
    Add-Result $url ($r.Code -ge 200 -and $r.Code -lt 400) $detail
}

try {
    # 1. The landing page and its headers.
    $landing = Get-Url "$Site/"
    $landingDetail = "HTTP $($landing.Code)"
    if ($landing.Reason) { $landingDetail = $landing.Reason }
    Add-Result "$Site/" ($landing.Code -eq 200) $landingDetail
    foreach ($header in 'Content-Security-Policy', 'Strict-Transport-Security', 'X-Content-Type-Options') {
        if ($Site.StartsWith('https://')) {
            Add-Result "$Site sends $header" ($landing.Headers -match "(?im)^$header\s*:") ''
        }
    }

    # 2. Every link on it, once each (anchors skipped; relative links resolved against the site).
    $links = [regex]::Matches($landing.Body, '(?:href|src)="([^"#]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $absolutes = foreach ($link in $links) {
        if ($link -match '^(mailto|tel|javascript):') { continue }
        if ($link.StartsWith('/')) { $Site.TrimEnd('/') + $link }
        elseif ($link -notmatch '^https?://') { $Site.TrimEnd('/') + '/' + $link }
        else { $link }
    }
    foreach ($absolute in ($absolutes | Sort-Object -Unique)) {
        if ($absolute -ne "$Site/") { Test-Reachable $absolute }
    }
    Test-Reachable "$Site/robots.txt"
    Test-Reachable "$Site/sitemap.xml"
    $missing = Get-Url "$Site/this-page-does-not-exist"
    Add-Result "$Site unknown path answers 404" ($missing.Code -eq 404) "HTTP $($missing.Code)"

    # 3. The portal: health, Cloudflare in front, release state.
    $health = Get-Url "$App/healthz"
    $healthDetail = "HTTP $($health.Code) '$($health.Body.Trim())'"
    if ($health.Reason) { $healthDetail = $health.Reason }
    Add-Result "$App/healthz is ok" ($health.Code -eq 200 -and $health.Body.Trim() -eq 'ok') $healthDetail
    if ($health.Code -eq 200) {
        Add-Result "$App is served through Cloudflare" ($health.Headers -match '(?im)^cf-ray\s*:') ''
        $release = Get-Url "$App/api/v1/release"
        $published = $release.Code -eq 200
        $releaseDetail = 'no installer published yet (Release__* not set)'
        if ($published) {
            $info = $release.Body | ConvertFrom-Json
            $releaseDetail = "version $($info.version)"
            Test-Reachable $info.downloadUrl
        }
        $results.Add([pscustomobject]@{ Status = 'info'; Check = "$App release"; Detail = $releaseDetail })
    }
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Status -eq 'FAIL' })
if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) check(s) failed."
    exit 1
}
Write-Host 'All checks passed.'
exit 0
