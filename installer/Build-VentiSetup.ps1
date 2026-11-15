param(
    [Parameter(Mandatory)][string]$Compiler,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Destination,
    [string]$Dotnet = 'dotnet',
    [string]$LocalDialogueModelDirectory,
    [string]$AssetDirectory,
    [switch]$Fixture
)
$ErrorActionPreference = 'Stop'
$makensis = (Resolve-Path -LiteralPath $Compiler).ProviderPath
$compilerHome = Split-Path -Parent $makensis
$compilerLicense = Join-Path $compilerHome 'COPYING'
if (!(Test-Path -LiteralPath $compilerLicense -PathType Leaf)) {
    $compilerLicense = Join-Path (Split-Path -Parent $compilerHome) 'COPYING'
}
if (!(Test-Path -LiteralPath $compilerLicense -PathType Leaf)) {
    throw 'Use the complete NSIS distribution with its COPYING file.'
}
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
if ([IO.Path]::GetExtension($output) -ne '.exe' -or (Test-Path -LiteralPath $output)) {
    throw 'Choose a new .exe destination.'
}
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
$staging = (New-Item -ItemType Directory -Path "$PSScriptRoot/../artifacts/local/setup-build" -Force).FullName
$build = (New-Item -ItemType Directory -Path (Join-Path $staging ([IO.Path]::GetRandomFileName()))).FullName
$artwork = Join-Path $build 'artwork'
$published = Join-Path $build 'publish'
$payload = Join-Path $build 'payload'
$desktop = "$PSScriptRoot/../experiments/m1-wpf-window"

$art = @{ Destination = $artwork; CoverOnly = $true }
if ($AssetDirectory) { $art.Front = Join-Path $AssetDirectory 'front.png' }
& "$PSScriptRoot/New-SetupArtwork.ps1" @art
Copy-Item -LiteralPath "$PSScriptRoot/../art/branding/venti.ico" -Destination $artwork
& $Dotnet publish "$desktop/Aeris.WindowProbe.csproj" -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o $published
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
Copy-Item -LiteralPath $compilerLicense -Destination (Join-Path $published 'licenses/NSIS-COPYING.txt')
$pack = @{ PublishDirectory = $published; Destination = $payload }
if ($LocalDialogueModelDirectory) { $pack.LocalDialogueModelDirectory = $LocalDialogueModelDirectory }
if ($AssetDirectory) { $pack.AssetDirectory = $AssetDirectory }
& "$desktop/Export-VentiPreview.ps1" @pack | Out-Null
$flags = @('/WX', '/INPUTCHARSET', 'UTF8', "/DVERSION=$Version", "/DOUTPUT=$output",
    "/DARTWORK=$artwork", "/DPAYLOAD=$payload")
if ($Fixture) { $flags += '/DFIXTURE' }
$compilerTemp = (New-Item -ItemType Directory -Path (Join-Path $build 'compiler-temp')).FullName
$priorTemp, $priorTmp = $env:TEMP, $env:TMP
try {
    $env:TEMP = $env:TMP = $compilerTemp
    & $makensis @flags "$PSScriptRoot/VentiSetup.nsi"
} finally {
    $env:TEMP, $env:TMP = $priorTemp, $priorTmp
}
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $output -PathType Leaf)) {
    throw 'NSIS did not produce a setup file.'
}
Get-FileHash -LiteralPath $output
