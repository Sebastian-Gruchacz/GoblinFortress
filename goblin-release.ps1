.\tools\bake-assets.ps1

dotnet build .\GoblinStronghold.slnx -c Release --no-restore
dotnet test .\GoblinStronghold.slnx -c Release --no-build --no-restore

& 'L:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' `
    --headless `
    --path '.\src\GoblinStronghold.Godot' `
    --export-release 'Windows Desktop' `
    '..\..\artifacts\release\windows\GoblinStronghold.exe'