<#
.SYNOPSIS
    Copies the bundled tools and their source code into a GitHub release.

.DESCRIPTION
    Reads tools\deps.json, downloads every tool from its upstream address,
    checks it against the pinned SHA-256, downloads its source archives, and
    uploads all of it to the repository and release named by "mirror",
    creating the release when it does not exist yet. A SHA256SUMS.txt file lists every file.

    Two reasons for the mirror. Upstream build services delete old builds, so
    a release built next month would otherwise fail to download the tools it
    pins. And FFmpeg, Ghostscript and 7-Zip are passed on in binary form inside
    Henkan's package, which obliges whoever distributes it to make their source
    available; the mirror keeps the exact source next to the exact binaries.

    Meant for the workflow in the mirror repository, which has the GitHub CLI
    and a token for it. Runs locally too, given `gh auth login`.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$deps = Get-Content (Join-Path $PSScriptRoot 'deps.json') -Raw | ConvertFrom-Json
$Repository = $deps.mirror.repository
$tag = $deps.mirror.tag
$work = Join-Path ([System.IO.Path]::GetTempPath()) "henkan-mirror-$tag"
New-Item -ItemType Directory -Force $work | Out-Null

function Get-Sha256([string]$path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$files = @()

foreach ($tool in $deps.tools) {
    $binary = Join-Path $work $tool.file
    Write-Host "$($tool.name): $($tool.upstream)"
    Invoke-WebRequest -Uri $tool.upstream -OutFile $binary -UseBasicParsing

    $actual = Get-Sha256 $binary
    if ($actual -ne $tool.sha256.ToLowerInvariant()) {
        throw "$($tool.name): SHA-256 mismatch.`n  expected $($tool.sha256)`n  actual   $actual"
    }
    $files += $binary

    foreach ($source in $tool.sources) {
        $path = Join-Path $work $source.file
        Write-Host "  source: $($source.upstream)"
        Invoke-WebRequest -Uri $source.upstream -OutFile $path -UseBasicParsing
        $files += $path
    }
}

$sums = Join-Path $work 'SHA256SUMS.txt'
($files | ForEach-Object { "$(Get-Sha256 $_)  $([System.IO.Path]::GetFileName($_))" }) -join "`n" | Set-Content -Path $sums -NoNewline
$files += $sums

$notes = @"
The third-party programs that come with Henkan, and their source code.

Henkan's package includes FFmpeg, Ghostscript and 7-Zip. They are free software, and their licenses (GPL-3.0, AGPL-3.0 and LGPL-2.1) ask that their source code be available wherever the programs are passed on. This release keeps the exact builds Henkan uses together with that source code, and Henkan's builds download the programs from here.

| Program | Version | License | Program file | Source code |
|---|---|---|---|---|
$(($deps.tools | ForEach-Object { "| $($_.name) | $($_.version) | $($_.license) | ``$($_.file)`` | $((($_.sources | ForEach-Object { '``' + $_.file + '``' }) -join ', ')) |" }) -join "`n")

The FFmpeg build is made by [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds). Its build recipe, included above, pins the upstream version of every library the build links in.

``SHA256SUMS.txt`` lists the checksum of every file.
"@

gh release view $tag -R $Repository *> $null
if ($LASTEXITCODE -ne 0) {
    gh release create $tag -R $Repository --title "Bundled tools ($tag)" --notes $notes --latest=false
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the release.' }
}
else {
    gh release edit $tag -R $Repository --notes $notes
}

gh release upload $tag -R $Repository --clobber @files
if ($LASTEXITCODE -ne 0) { throw 'Upload failed.' }

Write-Host "Mirrored $($files.Count) files to $tag."
