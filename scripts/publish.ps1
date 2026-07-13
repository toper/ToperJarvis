#requires -Version 7.0
<#
.SYNOPSIS
    Buduje samodzielną (self-contained) dystrybucję ToperJarvis dla Windows.

.DESCRIPTION
    Publikuje aplikację jako pojedynczy plik wykonywalny self-contained (bez wymaganego .NET
    na maszynie docelowej), kopiuje obok katalog assets/ (modele Whisper/Piper są poza repo,
    kopiowane tylko jeśli istnieją lokalnie) i usuwa deweloperski appsettings.Local.json.

.PARAMETER Configuration
    Konfiguracja kompilacji (domyślnie Release).

.PARAMETER Runtime
    RID środowiska docelowego (domyślnie win-x64).

.PARAMETER Output
    Katalog wyjściowy (domyślnie 'publish' w katalogu repo).

.PARAMETER ReadyToRun
    Dołącz prekompilację ReadyToRun (szybszy start kosztem rozmiaru).

.EXAMPLE
    pwsh scripts/publish.ps1
    pwsh scripts/publish.ps1 -ReadyToRun -Output C:\Dist\ToperJarvis
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "publish",
    [switch]$ReadyToRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/ToperJarvis.App/ToperJarvis.App.csproj"
$outDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repoRoot $Output }

Write-Host "Publikuję ToperJarvis ($Configuration / $Runtime) → $outDir" -ForegroundColor Cyan

# Czyszczenie katalogu docelowego — kompletny, świeży build (spójne z profilem VS: DeleteExistingFiles).
if (Test-Path $outDir) {
    Write-Host "Czyszczę $outDir ..." -ForegroundColor DarkGray
    Remove-Item (Join-Path $outDir "*") -Recurse -Force -ErrorAction SilentlyContinue
}

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $outDir
)
if ($ReadyToRun) { $publishArgs += "-p:PublishReadyToRun=true" }

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish nie powiódł się (kod $LASTEXITCODE)." }

# Kopiowanie assets/ obok exe robi target MSBuild "PrepareCompletePublish" (ToperJarvis.App.csproj),
# wspólny dla publikacji z VS i CLI. appsettings.Local.json (sekrety) jest dołączany do publikacji
# przez CopyToOutputDirectory — build lokalny. Przy dystrybucji na inną maszynę usuń go stamtąd.

$exe = Join-Path $outDir "ToperJarvis.App.exe"
Write-Host ""
Write-Host "Gotowe. Plik wykonywalny: $exe" -ForegroundColor Green
Write-Host "Wymagania runtime na maszynie docelowej:" -ForegroundColor Yellow
Write-Host "  - osiągalny endpoint vLLM (Jarvis:Llm:BaseUrl)"
Write-Host "  - zawartość assets/ (modele Whisper/Piper) — zob. assets/SETUP.md"
Write-Host "  - ffmpeg na PATH (tylko dla audio/wideo w file_processor)"
