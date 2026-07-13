#requires -Version 7.0
<#
.SYNOPSIS
    Pobiera modele ONNX potrzebne do neuralnego endpointingu (Smart Turn v3 + Silero VAD).

.DESCRIPTION
    Modele są duże i nie są trzymane w repo (patrz .gitignore, ten sam wzorzec co
    assets/whisper/*.bin). Ten skrypt pobiera je z Hugging Face do:
      - assets/smartturn/smart-turn-v3.1-cpu.onnx  (klasyfikator końca wypowiedzi)
      - assets/silero/silero_vad.onnx               (VAD)

    Dokładna sygnatura I/O obu grafów i wymagany preprocessing są opisane w
    docs/superpowers/notes/smartturn-io.md — przeczytaj to przed użyciem modeli w kodzie.

.EXAMPLE
    pwsh assets/download-endpointing-models.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$smartTurnDir = Join-Path $repoRoot "assets/smartturn"
$sileroDir = Join-Path $repoRoot "assets/silero"

New-Item -ItemType Directory -Force -Path $smartTurnDir | Out-Null
New-Item -ItemType Directory -Force -Path $sileroDir | Out-Null

$downloads = @(
    @{
        Name = "Smart Turn v3.1 (CPU)"
        Url  = "https://huggingface.co/pipecat-ai/smart-turn-v3/resolve/main/smart-turn-v3.1-cpu.onnx"
        Dest = Join-Path $smartTurnDir "smart-turn-v3.1-cpu.onnx"
    },
    @{
        Name = "Silero VAD"
        Url  = "https://huggingface.co/onnx-community/silero-vad/resolve/main/onnx/model.onnx"
        Dest = Join-Path $sileroDir "silero_vad.onnx"
    }
)

foreach ($d in $downloads) {
    if (Test-Path $d.Dest) {
        Write-Host "OK (już istnieje): $($d.Dest)"
        continue
    }
    Write-Host "Pobieram $($d.Name) -> $($d.Dest) ..."
    Invoke-WebRequest -Uri $d.Url -OutFile $d.Dest
    Write-Host "Gotowe: $($d.Dest)"
}

Write-Host ""
Write-Host "Sygnatura wejść/wyjść obu modeli: docs/superpowers/notes/smartturn-io.md"
