<#
.SYNOPSIS
  Writes GitHub Actions step outputs so dotnet build/publish can embed the same version as the pushed manager tag.

.DESCRIPTION
  For refs like manager-v2.0.4 or v2.0.4, sets apply=true and semver/assembly_version/informational for -p: overrides.
  For workflow_dispatch or non-matching refs, sets apply=false (use IcarusServerManager.csproj defaults).
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $RefName,

    [Parameter(Mandatory = $true)]
    [string] $Sha,

    [string] $OutputFile = $env:GITHUB_OUTPUT
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    throw 'GITHUB_OUTPUT is not set. This script is intended for GitHub Actions.'
}

$sem = $null
if ($RefName -match '^manager-v(\d+\.\d+\.\d+)$') {
    $sem = $Matches[1]
}
elseif ($RefName -match '^v(\d+\.\d+\.\d+)$') {
    $sem = $Matches[1]
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
if ($sem) {
    $asm = "$sem.0"
    $info = "$sem+$Sha"
    [System.IO.File]::AppendAllText($OutputFile, "apply=true`n", $utf8)
    [System.IO.File]::AppendAllText($OutputFile, "semver=$sem`n", $utf8)
    [System.IO.File]::AppendAllText($OutputFile, "assembly_version=$asm`n", $utf8)
    [System.IO.File]::AppendAllText($OutputFile, "informational=$info`n", $utf8)
    Write-Host "Manager release build: embedding version $sem from tag '$RefName'."
}
else {
    [System.IO.File]::AppendAllText($OutputFile, "apply=false`n", $utf8)
    Write-Host "Manager release build: no tag version override for ref '$RefName' (using csproj defaults)."
}
