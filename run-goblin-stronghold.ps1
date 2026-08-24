[CmdletBinding()]
param()

$projectPath = Join-Path $PSScriptRoot 'src\GoblinStronghold.Godot'
$candidates = @(
    $env:GODOT4,
    'L:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe',
    (Join-Path $PSScriptRoot 'artifacts\tools\godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe')
)

$godotPath = $candidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if (-not $godotPath) {
    throw 'Godot .NET 4.7.2 was not found. Set GODOT4 to the editor executable path.'
}

dotnet build (Join-Path $PSScriptRoot 'GoblinStronghold.slnx') --no-restore -c Debug
if ($LASTEXITCODE -ne 0) {
    throw 'The game build failed.'
}

& $godotPath --path $projectPath
