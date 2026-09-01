# Test projects

Simulation tests are split into four independently runnable projects. Test source
files remain in `tests/GoblinStronghold.Simulation.Tests/`; each project owns an
explicit, non-overlapping list so moving a test between areas is a deliberate
project-file change.

- `GoblinStronghold.Simulation.Content.Tests` validates embedded definitions,
  catalogs, package composition, and localization resources.
- `GoblinStronghold.Simulation.World.Tests` covers map generation, topology,
  visibility, navigation, geology, and terrain construction.
- `GoblinStronghold.Simulation.Economy.Tests` covers construction workflows,
  crafting, storage, hauling, logistics, and work dispatch.
- `GoblinStronghold.Simulation.Scenarios.Tests` covers actors, needs, ecology,
  combat, raids, commands, persistence, and broad engine scenarios.

Run one area by passing its project to `dotnet test`. Run the complete suite with:

```powershell
dotnet test .\GoblinStronghold.slnx --no-restore
```

`tests/Directory.Build.props` owns the shared test framework versions, compiler
settings, and simulation project reference. Reusable scenario helpers live in
the non-test `GoblinStronghold.Simulation.TestSupport` library, referenced only
by projects that need them.
