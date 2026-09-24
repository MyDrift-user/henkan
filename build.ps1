<#
.SYNOPSIS
    Builds Henkan end to end: shell extension (NativeAOT), app, and optionally the MSIX.

.DESCRIPTION
    Plain `dotnet build Henkan.sln` is enough to compile and run the app without
    the Explorer context menu. This script adds the two steps that need more:

      1. publishes Henkan.ShellExtension with NativeAOT, which requires the
         Visual C++ build tools, and
      2. builds the app so that the published DLL (and any bundled tools that
         tools\fetch-deps.ps1 has placed under build\tools) are picked up.

    Nothing here is required for the app to work. Missing tools only remove the
    conversions that would have used them.

.PARAMETER Configuration
    Debug or Release. Default Release.

.PARAMETER Package
    Also produce a signed MSIX under artifacts\. Needs a code signing
    certificate; see -CertificatePath. For local testing create one with
    tools\new-dev-cert.ps1.

.PARAMETER CertificatePath
    PFX used to sign the package. Default build\henkan-dev.pfx.

.PARAMETER SkipShellExtension
    Skip the NativeAOT publish, for machines without the C++ toolchain.

.PARAMETER FetchDeps
    Run tools\fetch-deps.ps1 first.

.EXAMPLE
    .\build.ps1 -FetchDeps -Package
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Package,
    [string]$CertificatePath = 'build\henkan-dev.pfx',
    [string]$CertificatePassword = '',
    [switch]$SkipShellExtension,
    [switch]$FetchDeps
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

if ($FetchDeps) {
    Step 'Fetching bundled tools'
    & "$root\tools\fetch-deps.ps1"
}

if (-not $SkipShellExtension) {
    Step 'Publishing the shell extension (NativeAOT)'

    # vcvarsall.bat in Visual Studio 18 calls vswhere.exe by bare name and
    # expects the Installer folder on PATH. Without this the ILCompiler
    # targets splice an error message into the linker path.
    $installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
    if ((Test-Path $installer) -and ($env:PATH -notlike "*$installer*")) {
        $env:PATH = "$installer;$env:PATH"
    }

    dotnet publish "$root\src\Henkan.ShellExtension\Henkan.ShellExtension.csproj" -c Release -r win-x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Shell extension publish failed.' }

    $dll = "$root\src\Henkan.ShellExtension\bin\publish\Henkan.ShellExtension.dll"
    if (-not (Test-Path $dll)) { throw "Expected $dll after publish." }
}

Step "Building the command line ($Configuration)"
dotnet build "$root\src\Henkan.Cli\Henkan.Cli.csproj" -c $Configuration -p:Platform=x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Command line build failed.' }

Step "Building the app ($Configuration)"
dotnet build "$root\src\Henkan.App\Henkan.App.csproj" -c $Configuration -p:Platform=x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }

Step 'Running tests'
dotnet test "$root\tests\Henkan.Core.Tests\Henkan.Core.Tests.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

if ($Package) {
    Step 'Packaging MSIX'

    if (-not (Test-Path $CertificatePath)) {
        throw "No certificate at $CertificatePath. Run tools\new-dev-cert.ps1 to create a development certificate, or pass -CertificatePath."
    }

    $artifacts = "$root\artifacts"
    # A package left over from a previous run makes signtool fail with an
    # unexplained internal error instead of simply overwriting it.
    Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $artifacts | Out-Null

    dotnet build "$root\src\Henkan.App\Henkan.App.csproj" -c $Configuration -p:Platform=x64 --nologo `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageDir="$artifacts\" `
        -p:AppxBundle=Never `
        -p:UapAppxPackageBuildMode=SideloadOnly `
        -p:AppxPackageSigningEnabled=true `
        -p:PackageCertificateKeyFile="$((Resolve-Path $CertificatePath).Path)" `
        -p:PackageCertificatePassword="$CertificatePassword"
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }

    Get-ChildItem $artifacts -Recurse -Filter *.msix | ForEach-Object { Write-Host "Package: $($_.FullName)" }
}

Step 'Done'
