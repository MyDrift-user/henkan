<#
.SYNOPSIS
    Creates a self-signed certificate for signing local MSIX builds.

.DESCRIPTION
    The subject must match the Publisher in Package.appxmanifest (CN=MyDrift).
    The certificate is exported to build\henkan-dev.pfx and, with -Trust, added
    to the local machine's Trusted People store so the package installs.
    Trusting needs an elevated prompt.

    This is for development only. A release build is signed with a real
    certificate through build.ps1 -CertificatePath.
#>
[CmdletBinding()]
param(
    [string]$OutputPath = "$PSScriptRoot\..\build\henkan-dev.pfx",
    [string]$Password = 'henkan',
    [switch]$Trust
)

$ErrorActionPreference = 'Stop'
$subject = 'CN=MyDrift'

New-Item -ItemType Directory -Force (Split-Path $OutputPath) | Out-Null

$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $subject `
    -KeyUsage DigitalSignature `
    -FriendlyName 'Henkan development signing' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
    -NotAfter (Get-Date).AddYears(3)

$secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $OutputPath -Password $secure | Out-Null
Write-Host "Wrote $OutputPath (password: $Password)"

if ($Trust) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPeople', 'LocalMachine')
    $store.Open('ReadWrite')
    $store.Add($cert)
    $store.Close()
    Write-Host 'Added to LocalMachine\TrustedPeople. MSIX packages signed with it will now install.'
}
else {
    Write-Host 'Run again with -Trust from an elevated prompt to let signed packages install.'
}
