# Creates the certificate that encrypts the server's data-protection key ring and prints it as base64 PFX for
# the DataProtection__CertificateBase64 setting. Run once per environment; keep the output in the host's secret
# store only. Windows PowerShell 5.1.
param([string]$Password = [Guid]::NewGuid().ToString('N'), [int]$Years = 5)
$ErrorActionPreference = 'Stop'
$cert = New-SelfSignedCertificate -Subject 'CN=Alpha 6 OPS key ring' -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable `
    -KeyUsage KeyEncipherment, DataEncipherment -NotAfter (Get-Date).AddYears($Years) -CertStoreLocation 'Cert:\CurrentUser\My'
try {
    $secure = ConvertTo-SecureString -String $Password -AsPlainText -Force
    $bytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $secure)
    $base64 = [Convert]::ToBase64String($bytes)
    $out = Join-Path (Split-Path -Parent $PSScriptRoot) 'work\keyring-certificate.txt'
    New-Item -ItemType Directory -Force (Split-Path -Parent $out) | Out-Null
    @("DataProtection__CertificateBase64=$base64", "DataProtection__CertificatePassword=$Password") | Set-Content -LiteralPath $out -Encoding ascii
    Write-Host ("Wrote {0} ({1} chars). Paste both values into the host's environment; then delete the file." -f $out, $base64.Length)
}
finally {
    # The certificate lives only in the exported PFX; nothing stays in the Windows store.
    Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $cert.Thumbprint) -Force
}
