[CmdletBinding()]
param(
    [string[]]$Recipe = @(
        'res://AssetRecipes/connected-walkways-v1.json',
        'res://AssetRecipes/terrain-height-transitions-v1.json',
        'res://AssetRecipes/cave-walls-v1.json',
        'res://AssetRecipes/connected-structure-walls-v1.json'
    )
)

$repositoryPath = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryPath 'src\GoblinStronghold.Godot'
$candidates = @(
    $env:GODOT4,
    'L:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe',
    (Join-Path $repositoryPath 'artifacts\tools\godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe')
)
$godotPath = $candidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if (-not $godotPath) {
    throw 'Godot .NET 4.7.2 was not found. Set GODOT4 to the editor executable path.'
}

dotnet build (Join-Path $projectPath 'GoblinStronghold.Godot.csproj') --no-restore -c Debug
if ($LASTEXITCODE -ne 0) {
    throw 'The asset baker build failed.'
}

foreach ($recipePath in $Recipe) {
    $bakeProcess = Start-Process -FilePath $godotPath -ArgumentList @(
        '--headless',
        '--path',
        $projectPath,
        'res://AssetBake.tscn',
        '--',
        "--recipe=$recipePath"
    ) -Wait -PassThru -NoNewWindow
    if ($bakeProcess.ExitCode -ne 0) {
        throw "Asset baking failed for '$recipePath' with exit code $($bakeProcess.ExitCode)."
    }
}
