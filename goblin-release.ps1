[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$godotProjectPath = Join-Path $projectRoot 'src\GoblinStronghold.Godot'
$releaseDirectory = Join-Path $projectRoot 'artifacts\release\windows'
$releaseExecutable = Join-Path $releaseDirectory 'GoblinStronghold.exe'
$godotPath = 'L:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'

Push-Location $projectRoot
try {
    & '.\tools\bake-assets.ps1'
    if ($LASTEXITCODE -ne 0) {
        throw 'Asset baking failed.'
    }

    dotnet build '.\GoblinStronghold.slnx' -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'Release build failed.'
    }

    dotnet test '.\GoblinStronghold.slnx' -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'Release tests failed.'
    }

    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

    & $godotPath `
        --headless `
        --path $godotProjectPath `
        --export-release 'Windows Desktop' `
        $releaseExecutable
    if ($LASTEXITCODE -ne 0) {
        throw 'Godot release export failed.'
    }

    $releasePack = Join-Path $releaseDirectory 'GoblinStronghold.pck'
    $managedRuntime = Join-Path $releaseDirectory 'data_GoblinStronghold.Godot_windows_x86_64'
    if (-not (Test-Path -LiteralPath $releaseExecutable) -or
        -not (Test-Path -LiteralPath $releasePack) -or
        -not (Test-Path -LiteralPath $managedRuntime -PathType Container)) {
        throw 'Godot export did not produce a complete Windows package.'
    }
}
finally {
    Pop-Location
}