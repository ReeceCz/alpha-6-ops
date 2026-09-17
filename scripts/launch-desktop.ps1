param([ValidateSet('Login', 'Dashboard')][string]$View = 'Login')

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$launchOutput = Join-Path $workspaceRoot ('work\desktop-launcher\' + $runName)
$launchLog = Join-Path $launchOutput 'build.log'
try {
    New-Item -ItemType Directory -Path $launchOutput -Force | Out-Null
    Push-Location $workspaceRoot
    try {
        # Separate outputs allow a fresh build while an older app is running.
        & dotnet build src/Alpha6Ops.Desktop/Alpha6Ops.Desktop.csproj --output $launchOutput *> $launchLog
        if ($LASTEXITCODE -ne 0) { throw ('Build failed. Details: ' + $launchLog) }
    }
    finally { Pop-Location }
    $launchExe = Join-Path $launchOutput 'Alpha6OPS.exe'
    $launchOptions = @{ FilePath = $launchExe; WorkingDirectory = $launchOutput; WindowStyle = 'Normal'; PassThru = $true }
    if ($View -eq 'Login') { $launchOptions.ArgumentList = '--preview-login' }
    $launchProcess = Start-Process @launchOptions
    $exited = $launchProcess.WaitForExit(2500)
    # Normal desktop launch exits successfully after activating an existing instance.
    if ($exited -and ($View -eq 'Login' -or $launchProcess.ExitCode -ne 0)) { throw ('The app exited during startup. Build: ' + $launchOutput) }
    [pscustomobject]@{ ProcessId = $launchProcess.Id; Executable = $launchExe; View = $View; Arguments = $(if ($View -eq 'Login') { '--preview-login' } else { '' }); Exited = $exited } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $launchOutput 'launch.json') -Encoding UTF8
}
catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($_.Exception.Message, 'Alpha 6 OPS could not start', 'OK', 'Error') | Out-Null
    exit 1
}
