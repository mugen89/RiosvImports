<#
.SYNOPSIS
    Прави двойката ключове, с която сървърът подписва правото на ползване.

.DESCRIPTION
    Прави се ВЕДНЪЖ. Публичната половина влиза в AccountOptions.Default в кода
    на програмата; частната се качва като тайна на сървъра и никъде другаде.

    Смяна на ключа обезсилва всички издадени права наведнъж — затова частният
    ключ не бива да попада в хранилище, в архив или в чат.

.EXAMPLE
    .\tools\new-signing-key.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDir = (Join-Path $env:USERPROFILE '.riosv-keys')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Security

# P-256 + SHA-256 — това е ES256, каквото EntitlementVerifier очаква.
$key = [System.Security.Cryptography.ECDsa]::Create(
    [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)

try {
    $public  = $key.ExportSubjectPublicKeyInfoPem()
    $private = $key.ExportPkcs8PrivateKeyPem()
}
finally {
    $key.Dispose()
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$privatePath = Join-Path $OutputDir 'entitlement-private.pem'

Set-Content -Path $privatePath -Value $private -Encoding utf8

# Достъп само за текущия потребител: файлът е равносилен на всички абонаменти.
icacls $privatePath /inheritance:r /grant:r "$($env:USERNAME):(R)" | Out-Null

Write-Host ''
Write-Host 'ПУБЛИЧЕН КЛЮЧ — сложете го в AccountOptions.Default:' -ForegroundColor Green
Write-Host ''
Write-Host $public
Write-Host 'ЧАСТЕН КЛЮЧ — записан в:' -ForegroundColor Yellow
Write-Host "  $privatePath"
Write-Host ''
Write-Host 'Качете съдържанието му като тайна на сървъра (например ENTITLEMENT_SIGNING_KEY)' -ForegroundColor Yellow
Write-Host 'и след това го изтрийте оттук. Не влиза в git, в архиви и в писма.'
