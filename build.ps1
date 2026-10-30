# AudioClipExtractor - self-contained single-file publish (win-x64)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$project = Join-Path $root 'AudioClipExtractor.csproj'
$outDir  = Join-Path $root 'dist\AudioClipExtractor'

Write-Host '============================================================'
Write-Host ' AudioClipExtractor - self-contained single-file publish'
Write-Host ' Target: win-x64'
Write-Host '============================================================'
Write-Host ''

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host '[ERROR] dotnet not found. Install .NET 8 SDK first:'
    Write-Host '        https://dotnet.microsoft.com/download/dotnet/8.0'
    exit 1
}
if (-not (Test-Path $project)) {
    Write-Host "[ERROR] AudioClipExtractor.csproj not found in $root"
    exit 1
}

Write-Host '[1/3] Cleaning old output ...'
foreach ($d in 'dist', 'bin', 'obj') {
    $p = Join-Path $root $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

Write-Host '[2/3] Publishing ...'
$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $outDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host '[ERROR] dotnet publish failed.'
    exit 1
}

Write-Host '[3/3] Cleaning intermediate files ...'
foreach ($d in 'bin', 'obj') {
    $p = Join-Path $root $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

$entries = @(Get-ChildItem $outDir -Force -ErrorAction SilentlyContinue)
Write-Host ''
Write-Host '============================================================'
if ((Test-Path (Join-Path $outDir 'AudioClipExtractor.exe')) -and $entries.Count -eq 1) {
    Write-Host ' SUCCESS - clean output:'
    Write-Host " $outDir\AudioClipExtractor.exe"
    Write-Host '============================================================'
    explorer $outDir
    exit 0
} else {
    Write-Host " [WARN] dist is not clean: $($entries.Count) entries found, expected 1."
    Write-Host '============================================================'
    $entries | ForEach-Object { Write-Host ('  ' + $_.Name) }
    exit 1
}