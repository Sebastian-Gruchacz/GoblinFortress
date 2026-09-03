using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

public partial class WorldView3D : Node3D
{
    public enum CameraAngle
    {
        TopDown,
        Oblique,
    }

    public const float CellSize = 1.5f;
    public const float LevelHeight = 0.8f;
    private const int ChunkSize = 16;
    private readonly Dictionary<EntityId, MeshInstance3D> _actorMarkers = [];
    private SimulationEngine _engine = null!;
    private SimulationSnapshot _snapshot = null!;
    private Node3D _terrainRoot = null!;
    private Node3D _structureRoot = null!;
    private Node3D _actorRoot = null!;
    private Camera3D _camera = null!;
    private StandardMaterial3D _terrainMaterial = null!;
    private StandardMaterial3D _waterMaterial = null!;
    private StandardMaterial3D _structureMaterial = null!;
    private StandardMaterial3D _actorMaterial = null!;
    private StandardMaterial3D _selectedActorMaterial = null!;
    private Vector3 _cameraTarget;
    private ulong _renderedStructureSignature = ulong.MaxValue;
    private HashSet<EntityId> _selectedActorIds = [];
    private CameraAngle _cameraAngle = CameraAngle.TopDown;
    private int _cameraQuarterTurns;

    public int TerrainMeshCount => _terrainRoot?.GetChildCount() ?? 0;

    public int StructureMeshCount => _structureRoot?.GetChildCount() ?? 0;

    public CameraAngle CurrentCameraAngle => _cameraAngle;

    public int CameraQuarterTurns => _cameraQuarterTurns;

    public override void _Ready()
    {
        _terrainRoot = GetNode<Node3D>("TerrainChunks");
        _structureRoot = GetNode<Node3D>("Structures");
        _actorRoot = GetNode<Node3D>("Actors");
        _camera = GetNode<Camera3D>("Camera3D");
        CreateMaterials();
        CreateLighting();
    }

    public void SetWorld(SimulationEngine engine)
    {
        _engine = engine;
        _snapshot = engine.CreatePresentationSnapshot();
        _renderedStructureSignature = ulong.MaxValue;
        ClearChildren(_terrainRoot);
        ClearChildren(_structureRoot);
        ClearChildren(_actorRoot);
        _actorMarkers.Clear();
        BuildTerrainChunks();
        Refresh(_snapshot);
        CenterOn(engine.Map.GoblinSpawn);
    }

    public void Refresh(SimulationSnapshot snapshot)
    {
        _snapshot = snapshot;
        var structureSignature = ComputeStructureSignature(snapshot.WorldObjects);
        if (_renderedStructureSignature != structureSignature)
        {
            RebuildWorldGeometry();
            _renderedStructureSignature = structureSignature;
        }

        SynchronizeActors();
    }

    public void SetActive(bool active)
    {
        Visible = active;
        _camera.Current = active;
    }

    public void SetSelectedActors(IEnumerable<EntityId> actorIds)
    {
        _selectedActorIds = actorIds.ToHashSet();
        UpdateActorMaterials();
    }

    public void CenterOn(GridPosition position)
    {
        _cameraTarget = new Vector3(
            (position.X + 0.5f) * CellSize,
            GetSurfaceHeight(position.X, position.Y),
            (position.Y + 0.5f) * CellSize);
        ConstrainCamera();
    }

    public void Pan(Vector2 direction, double delta)
    {
        if (direction.IsZeroApprox())
        {
            return;
        }

        var right = new Vector3(_camera.GlobalBasis.X.X, 0f, _camera.GlobalBasis.X.Z).Normalized();
        var screenUp = new Vector3(_camera.GlobalBasis.Y.X, 0f, _camera.GlobalBasis.Y.Z).Normalized();
        var movement = (right * direction.X) - (screenUp * direction.Y);
        _cameraTarget += movement.Normalized() * (float)(_camera.Size * 0.72 * delta);
        ConstrainCamera();
    }

    public void PanScreenDelta(Vector2 screenDelta, Vector2 viewportSize)
    {
        if (viewportSize.Y <= 0f)
        {
            return;
        }

        var worldPerPixel = _camera.Size / viewportSize.Y;
        var right = new Vector3(_camera.GlobalBasis.X.X, 0f, _camera.GlobalBasis.X.Z).Normalized();
        var upScreen = new Vector3(_camera.GlobalBasis.Y.X, 0f, _camera.GlobalBasis.Y.Z).Normalized();
        _cameraTarget -= ((right * screenDelta.X) + (upScreen * screenDelta.Y)) * worldPerPixel;
        ConstrainCamera();
    }

    public void ChangeZoom(float factor)
    {
        _camera.Size = Math.Clamp(_camera.Size / factor, 10f, 110f);
        ConstrainCamera();
    }

    public void ToggleCameraAngle()
    {
        _cameraAngle = _cameraAngle == CameraAngle.TopDown
            ? CameraAngle.Oblique
            : CameraAngle.TopDown;
        ConstrainCamera();
    }

    public void RotateCamera(int quarterTurns)
    {
        _cameraQuarterTurns = ((_cameraQuarterTurns + quarterTurns) % 4 + 4) % 4;
        ConstrainCamera();
    }

    public void ConstrainCamera()
    {
        if (_engine is null)
        {
            return;
        }

        var extentX = _engine.Map.Width * CellSize;
        var extentZ = _engine.Map.Height * CellSize;
        _cameraTarget.X = Math.Clamp(_cameraTarget.X, CellSize * 0.5f, extentX - CellSize * 0.5f);
        _cameraTarget.Z = Math.Clamp(_cameraTarget.Z, CellSize * 0.5f, extentZ - CellSize * 0.5f);
        _cameraTarget.Y = GetSurfaceHeightAtWorld(_cameraTarget.X, _cameraTarget.Z);
        var distanceScale = Math.Clamp(_camera.Size / 42f, 0.75f, 1.8f);
        if (_cameraAngle == CameraAngle.TopDown)
        {
            _camera.Position = _cameraTarget + (Vector3.Up * 54f * distanceScale);
            _camera.LookAt(_cameraTarget, GetTopDownScreenUp());
            return;
        }

        var horizontalOffset = new Vector3(28f, 0f, 28f)
            .Rotated(Vector3.Up, _cameraQuarterTurns * Mathf.Pi * 0.5f);
        _camera.Position = _cameraTarget +
            ((horizontalOffset + (Vector3.Up * 40f)) * distanceScale);
        _camera.LookAt(_cameraTarget, Vector3.Up);
    }

    public (Vector2 Center, Vector2 Size) GetNormalizedCameraView(Vector2 viewportSize)
    {
        var worldSize = new Vector2(_engine.Map.Width * CellSize, _engine.Map.Height * CellSize);
        var aspect = viewportSize.Y <= 0f ? 1f : viewportSize.X / viewportSize.Y;
        var visible = new Vector2(_camera.Size * aspect, _camera.Size);
        return (
            new Vector2(_cameraTarget.X / worldSize.X, _cameraTarget.Z / worldSize.Y),
            new Vector2(
                Math.Clamp(visible.X / worldSize.X, 0f, 1f),
                Math.Clamp(visible.Y / worldSize.Y, 0f, 1f)));
    }

    public GridPosition ScreenToCell(Vector2 screenPosition)
    {
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        var maximumDistance = 180f;
        const float step = 0.18f;
        for (var distance = 0f; distance <= maximumDistance; distance += step)
        {
            var point = origin + (direction * distance);
            var x = Mathf.FloorToInt(point.X / CellSize);
            var y = Mathf.FloorToInt(point.Z / CellSize);
            var position = new GridPosition(x, y);
            if (!_engine.Map.IsWithin(position))
            {
                continue;
            }

            if (point.Y <= GetSurfaceHeight(x, y) + 0.08f)
            {
                return position;
            }
        }

        return new GridPosition(-1, -1);
    }

    private void CreateMaterials()
    {
        _terrainMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.94f,
        };
        _waterMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.18f,
            Metallic = 0.08f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _structureMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.86f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _actorMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("69b94f"),
            Roughness = 0.78f,
        };
        _selectedActorMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("d7ff68"),
            EmissionEnabled = true,
            Emission = new Color("91bd3e"),
            EmissionEnergyMultiplier = 0.65f,
            Roughness = 0.78f,
        };
    }

    private void CreateLighting()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("111a18"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("8da28f"),
            AmbientLightEnergy = 0.55f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-58f, -32f, 0f),
            LightColor = new Color("fff1c7"),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        AddChild(sun);
    }

    private void BuildTerrainChunks()
    {
        for (var chunkY = 0; chunkY < _engine.Map.Height; chunkY += ChunkSize)
        {
            for (var chunkX = 0; chunkX < _engine.Map.Width; chunkX += ChunkSize)
            {
                BuildTerrainChunk(chunkX, chunkY);
            }
        }
    }

    private void BuildTerrainChunk(int startX, int startY)
    {
        var terrain = new SurfaceTool();
        terrain.Begin(Mesh.PrimitiveType.Triangles);
        terrain.SetMaterial(_terrainMaterial);
        var water = new SurfaceTool();
        water.Begin(Mesh.PrimitiveType.Triangles);
        water.SetMaterial(_waterMaterial);
        var endX = Math.Min(startX + ChunkSize, _engine.Map.Width);
        var endY = Math.Min(startY + ChunkSize, _engine.Map.Height);
        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                AddTerrainCell(terrain, water, x, y);
            }
        }

        terrain.GenerateNormals();
        var terrainMesh = terrain.Commit();
        if (terrainMesh is not null)
        {
            var instance = new MeshInstance3D { Mesh = terrainMesh, Name = $"Ground_{startX}_{startY}" };
            _terrainRoot.AddChild(instance);
        }

        water.GenerateNormals();
        var waterMesh = water.Commit();
        if (waterMesh is not null && waterMesh.GetSurfaceCount() > 0)
        {
            var instance = new MeshInstance3D { Mesh = waterMesh, Name = $"Water_{startX}_{startY}" };
            _terrainRoot.AddChild(instance);
        }
    }

    private void AddTerrainCell(SurfaceTool terrain, SurfaceTool water, int x, int y)
    {
        var position = new GridPosition(x, y);
        var cell = _engine.Map.GetCell(position);
        var heights = GetGroundCornerHeights(x, y, cell);
        var x0 = x * CellSize;
        var x1 = x0 + CellSize;
        var z0 = y * CellSize;
        var z1 = z0 + CellSize;
        var vertices = new[]
        {
            new Vector3(x0, heights[0], z0),
            new Vector3(x1, heights[1], z0),
            new Vector3(x1, heights[2], z1),
            new Vector3(x0, heights[3], z1),
        };
        AddQuad(terrain, vertices[0], vertices[3], vertices[2], vertices[1], TerrainColor(cell));
        AddTerrainSides(terrain, x, y, vertices, cell);

        if (cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater)
        {
            var waterY = (cell.SurfaceLevel * LevelHeight) + 0.06f;
            var color = cell.Terrain == TerrainKind.DeepWater
                ? new Color(0.025f, 0.22f, 0.34f, 0.78f)
                : new Color(0.12f, 0.48f, 0.52f, 0.54f);
            AddQuad(
                water,
                new Vector3(x0, waterY, z0),
                new Vector3(x0, waterY, z1),
                new Vector3(x1, waterY, z1),
                new Vector3(x1, waterY, z0),
                color);
        }
    }

    private void AddTerrainSides(
        SurfaceTool surface,
        int x,
        int y,
        IReadOnlyList<Vector3> vertices,
        MapCell cell)
    {
        AddTerrainSide(surface, x, y, TerrainRampDirection.North, vertices[0], vertices[1], cell);
        AddTerrainSide(surface, x, y, TerrainRampDirection.East, vertices[1], vertices[2], cell);
        AddTerrainSide(surface, x, y, TerrainRampDirection.South, vertices[2], vertices[3], cell);
        AddTerrainSide(surface, x, y, TerrainRampDirection.West, vertices[3], vertices[0], cell);
    }

    private void AddTerrainSide(
        SurfaceTool surface,
        int x,
        int y,
        TerrainRampDirection direction,
        Vector3 upperA,
        Vector3 upperB,
        MapCell cell)
    {
        var neighborPosition = direction switch
        {
            TerrainRampDirection.North => new GridPosition(x, y - 1),
            TerrainRampDirection.East => new GridPosition(x + 1, y),
            TerrainRampDirection.South => new GridPosition(x, y + 1),
            TerrainRampDirection.West => new GridPosition(x - 1, y),
            _ => default,
        };
        float lowerA;
        float lowerB;
        if (_engine.Map.IsWithin(neighborPosition))
        {
            var neighborHeights = GetGroundCornerHeights(
                neighborPosition.X,
                neighborPosition.Y,
                _engine.Map.GetCell(neighborPosition));
            (lowerA, lowerB) = direction switch
            {
                TerrainRampDirection.North => (neighborHeights[3], neighborHeights[2]),
                TerrainRampDirection.East => (neighborHeights[0], neighborHeights[3]),
                TerrainRampDirection.South => (neighborHeights[1], neighborHeights[0]),
                TerrainRampDirection.West => (neighborHeights[2], neighborHeights[1]),
                _ => (upperA.Y, upperB.Y),
            };
        }
        else
        {
            lowerA = lowerB = (_engine.Map.MinimumTerrainLevel - 1) * LevelHeight;
        }

        if (upperA.Y <= lowerA + 0.01f && upperB.Y <= lowerB + 0.01f)
        {
            return;
        }

        var bottomA = new Vector3(upperA.X, Math.Min(upperA.Y, lowerA), upperA.Z);
        var bottomB = new Vector3(upperB.X, Math.Min(upperB.Y, lowerB), upperB.Z);
        var sideColor = TerrainColor(cell).Darkened(0.34f);
        AddQuad(surface, upperA, upperB, bottomB, bottomA, sideColor);
    }

    private float[] GetGroundCornerHeights(int x, int y, MapCell cell)
    {
        var level = cell.Terrain == TerrainKind.DeepWater ? cell.FloorLevel : cell.SurfaceLevel;
        var baseHeight = level * LevelHeight;
        var heights = new[] { baseHeight, baseHeight, baseHeight, baseHeight };
        if (cell.Terrain == TerrainKind.DeepWater ||
            !_engine.World.IsTerrainRampIntact(new GridPosition(x, y, cell.SurfaceLevel)))
        {
            return heights;
        }

        switch (cell.RampDirection)
        {
            case TerrainRampDirection.North:
                heights[0] += LevelHeight;
                heights[1] += LevelHeight;
                break;
            case TerrainRampDirection.East:
                heights[1] += LevelHeight;
                heights[2] += LevelHeight;
                break;
            case TerrainRampDirection.South:
                heights[2] += LevelHeight;
                heights[3] += LevelHeight;
                break;
            case TerrainRampDirection.West:
                heights[3] += LevelHeight;
                heights[0] += LevelHeight;
                break;
        }

        return heights;
    }

    private void RebuildWorldGeometry()
    {
        ClearChildren(_structureRoot);
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(_structureMaterial);
        foreach (var worldObject in _snapshot.WorldObjects)
        {
            foreach (var (position, part) in worldObject.GetAbsoluteParts())
            {
                if (position.X < 0 || position.X >= _engine.Map.Width ||
                    position.Y < 0 || position.Y >= _engine.Map.Height ||
                    part.Kind == WorldObjectPartKind.Roof)
                {
                    continue;
                }

                AddWorldObjectPart(surface, worldObject, position, part.Kind);
            }
        }

        surface.GenerateNormals();
        var mesh = surface.Commit();
        if (mesh is not null && mesh.GetSurfaceCount() > 0)
        {
            _structureRoot.AddChild(new MeshInstance3D { Mesh = mesh, Name = "BatchedStructures" });
        }
    }

    private void AddWorldObjectPart(
        SurfaceTool surface,
        WorldObjectSnapshot worldObject,
        GridPosition position,
        WorldObjectPartKind partKind)
    {
        var terrainY = GetSurfaceHeight(position.X, position.Y);
        var layerY = terrainY + (position.Z * LevelHeight);
        var center = new Vector3(
            (position.X + 0.5f) * CellSize,
            layerY,
            (position.Y + 0.5f) * CellSize);
        var color = StructureColor(worldObject, partKind);
        switch (partKind)
        {
            case WorldObjectPartKind.Floor:
            case WorldObjectPartKind.Walkway:
            case WorldObjectPartKind.WatchtowerPlatform:
                AddBox(surface, center + Vector3.Up * 0.06f,
                    new Vector3(CellSize * 0.9f, 0.12f, CellSize * 0.9f), color);
                break;
            case WorldObjectPartKind.ConstructedRamp:
                AddConstructedRamp(surface, center, worldObject.Orientation, color);
                break;
            case WorldObjectPartKind.Wall:
                AddBox(surface, center + Vector3.Up * (LevelHeight * 0.46f),
                    new Vector3(CellSize * 0.84f, LevelHeight * 0.92f, CellSize * 0.84f), color);
                break;
            case WorldObjectPartKind.Door:
                AddBox(surface, center + Vector3.Up * (LevelHeight * 0.34f),
                    new Vector3(CellSize * 0.42f, LevelHeight * 0.68f, CellSize * 0.22f), color);
                break;
            case WorldObjectPartKind.ClosedDoorLeaf:
                AddBox(surface, center + Vector3.Up * (LevelHeight * 0.34f),
                    new Vector3(CellSize * 0.72f, LevelHeight * 0.68f, CellSize * 0.12f), color);
                break;
            case WorldObjectPartKind.OpenDoorLeaf:
            case WorldObjectPartKind.AutomaticallyOpenedDoorLeaf:
                AddBox(surface, center + new Vector3(-CellSize * 0.32f, LevelHeight * 0.34f, 0f),
                    new Vector3(CellSize * 0.12f, LevelHeight * 0.68f, CellSize * 0.72f), color);
                break;
            case WorldObjectPartKind.WellRim:
            case WorldObjectPartKind.WellShaft:
                AddBox(surface, center + Vector3.Up * 0.25f,
                    new Vector3(CellSize * 0.74f, 0.5f, CellSize * 0.74f), color);
                break;
            case WorldObjectPartKind.TreeTrunk:
            case WorldObjectPartKind.WatchtowerSupport:
                AddBox(surface, center + Vector3.Up * (LevelHeight * 0.48f),
                    new Vector3(0.34f, LevelHeight * 0.96f, 0.34f), color);
                break;
            case WorldObjectPartKind.Ladder:
                AddBox(surface, center + Vector3.Up * (LevelHeight * 0.42f),
                    new Vector3(CellSize * 0.62f, LevelHeight * 0.84f, 0.12f), color);
                break;
            case WorldObjectPartKind.TreeStump:
                AddBox(surface, center + Vector3.Up * 0.13f,
                    new Vector3(0.46f, 0.26f, 0.46f), color);
                break;
            case WorldObjectPartKind.FelledTreeRemains:
                AddBox(surface, center + new Vector3(0f, 0.12f, 0f),
                    new Vector3(CellSize * 0.9f, 0.24f, 0.3f), color);
                break;
            case WorldObjectPartKind.TreeCrown:
                AddCrossedCard(surface, center + Vector3.Up * (LevelHeight * 0.45f),
                    new Vector2(CellSize * 1.55f, LevelHeight * 1.7f), color);
                break;
            case WorldObjectPartKind.PrimitiveWorkshop:
            case WorldObjectPartKind.FittedWorkshop:
                AddBox(surface, center + Vector3.Up * 0.2f,
                    new Vector3(CellSize * 0.82f, 0.4f, CellSize * 0.58f), color);
                break;
            case WorldObjectPartKind.Bloomery:
            case WorldObjectPartKind.SmeltingFurnace:
            case WorldObjectPartKind.CrucibleFurnace:
            case WorldObjectPartKind.CookingFire:
                AddBox(surface, center + Vector3.Up * (LevelHeight * 0.32f),
                    new Vector3(CellSize * 0.78f, LevelHeight * 0.64f, CellSize * 0.78f), color);
                break;
        }
    }

    private static void AddConstructedRamp(
        SurfaceTool surface,
        Vector3 center,
        CardinalOrientation orientation,
        Color color)
    {
        var uphill = orientation switch
        {
            CardinalOrientation.North => Vector3.Forward,
            CardinalOrientation.East => Vector3.Right,
            CardinalOrientation.South => Vector3.Back,
            CardinalOrientation.West => Vector3.Left,
            _ => Vector3.Forward,
        };
        var runsAlongX = orientation is CardinalOrientation.East or CardinalOrientation.West;
        const int stepCount = 4;
        for (var index = 0; index < stepCount; index++)
        {
            var top = LevelHeight * (index + 1) / stepCount;
            var offset = ((index + 0.5f) / stepCount - 0.5f) * CellSize;
            AddBox(
                surface,
                center + (uphill * offset) + (Vector3.Up * (top * 0.5f)),
                new Vector3(
                    runsAlongX ? CellSize / stepCount : CellSize * 0.9f,
                    top,
                    runsAlongX ? CellSize * 0.9f : CellSize / stepCount),
                color);
        }
    }

    private void SynchronizeActors()
    {
        var active = _snapshot.Actors.Select(actor => actor.Id).ToHashSet();
        foreach (var actorId in _actorMarkers.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            _actorMarkers[actorId].QueueFree();
            _actorMarkers.Remove(actorId);
        }

        foreach (var actor in _snapshot.Actors)
        {
            if (!_actorMarkers.TryGetValue(actor.Id, out var marker))
            {
                marker = new MeshInstance3D
                {
                    Mesh = new CapsuleMesh { Radius = 0.22f, Height = 0.68f },
                    Name = $"Goblin_{actor.Id.Value}",
                };
                _actorRoot.AddChild(marker);
                _actorMarkers.Add(actor.Id, marker);
            }

            marker.Position = new Vector3(
                (actor.Position.X + 0.5f) * CellSize,
                GetSurfaceHeight(actor.Position.X, actor.Position.Y) + 0.38f,
                (actor.Position.Y + 0.5f) * CellSize);
        }

        UpdateActorMaterials();
    }

    private void UpdateActorMaterials()
    {
        foreach (var (actorId, marker) in _actorMarkers)
        {
            marker.MaterialOverride = _selectedActorIds.Contains(actorId)
                ? _selectedActorMaterial
                : _actorMaterial;
        }
    }

    private static ulong ComputeStructureSignature(IReadOnlyList<WorldObjectSnapshot> worldObjects)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var signature = offset;
        foreach (var worldObject in worldObjects)
        {
            signature = (signature ^ worldObject.Id.Value) * prime;
            signature = (signature ^ (byte)worldObject.Kind) * prime;
            signature = (signature ^ unchecked((uint)worldObject.Anchor.X)) * prime;
            signature = (signature ^ unchecked((uint)worldObject.Anchor.Y)) * prime;
            signature = (signature ^ unchecked((uint)worldObject.Anchor.Z)) * prime;
            signature = (signature ^ (uint)worldObject.Parts.Count) * prime;
        }

        return signature;
    }

    private float GetSurfaceHeight(int x, int y)
    {
        var position = new GridPosition(x, y);
        if (!_engine.Map.IsWithin(position))
        {
            return 0f;
        }

        var cell = _engine.Map.GetCell(position);
        if (cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater)
        {
            return (cell.SurfaceLevel * LevelHeight) + 0.06f;
        }

        var corners = GetGroundCornerHeights(x, y, cell);
        return (float)corners.Average();
    }

    private float GetSurfaceHeightAtWorld(float worldX, float worldZ)
    {
        var x = Math.Clamp(Mathf.FloorToInt(worldX / CellSize), 0, _engine.Map.Width - 1);
        var y = Math.Clamp(Mathf.FloorToInt(worldZ / CellSize), 0, _engine.Map.Height - 1);
        return GetSurfaceHeight(x, y);
    }

    private Vector3 GetTopDownScreenUp() => _cameraQuarterTurns switch
    {
        0 => Vector3.Forward,
        1 => Vector3.Right,
        2 => Vector3.Back,
        _ => Vector3.Left,
    };

    private static Color TerrainColor(MapCell cell) => cell.Terrain switch
    {
        TerrainKind.SolidGround => new Color(0.23f + (cell.Fertility / 900f), 0.38f + (cell.Fertility / 650f), 0.18f, 1f),
        TerrainKind.Mud => new Color(0.25f, 0.205f, 0.12f, 1f).Lerp(new Color(0.17f, 0.25f, 0.16f), cell.Fertility / 255f),
        TerrainKind.ShallowWater => new Color(0.18f, 0.28f, 0.21f, 1f),
        TerrainKind.DeepWater => new Color(0.055f, 0.12f, 0.16f, 1f),
        _ => Colors.Magenta,
    };

    private Color StructureColor(WorldObjectSnapshot worldObject, WorldObjectPartKind partKind) =>
        partKind switch
        {
            WorldObjectPartKind.TreeTrunk or WorldObjectPartKind.TreeStump or
                WorldObjectPartKind.FelledTreeRemains => TreePartSprites.GetWoodColor(
                    WoodMaterialPolicy.VariantFor(
                        _engine.WorldSeed,
                        _engine.Map.Width,
                        worldObject.Anchor)),
            _ when worldObject.MaterialVariant != ResourceVariant.None =>
                MaterialPaletteColors.For(worldObject.MaterialVariant).Midtone,
            WorldObjectPartKind.TreeCrown => TreeCrownSprites.GetCrownColor(
                WoodMaterialPolicy.VariantFor(
                    _engine.WorldSeed,
                    _engine.Map.Width,
                    worldObject.Anchor)),
            WorldObjectPartKind.Walkway => new Color("ad7b41"),
            WorldObjectPartKind.WellRim or WorldObjectPartKind.WellShaft => new Color("777b70"),
            WorldObjectPartKind.Door => new Color("6f4324"),
            WorldObjectPartKind.ClosedDoorLeaf or WorldObjectPartKind.OpenDoorLeaf or
                WorldObjectPartKind.AutomaticallyOpenedDoorLeaf => new Color("89562e"),
            WorldObjectPartKind.Bloomery => new Color("766753"),
            WorldObjectPartKind.SmeltingFurnace => new Color("59606a"),
            WorldObjectPartKind.CrucibleFurnace => new Color("49434f"),
            _ when worldObject.Owner == WorldObjectOwner.GoblinTribe => new Color("78633c"),
            _ => new Color("a48052"),
        };

    private static void AddBox(SurfaceTool surface, Vector3 center, Vector3 size, Color color)
    {
        var half = size * 0.5f;
        var p000 = center + new Vector3(-half.X, -half.Y, -half.Z);
        var p001 = center + new Vector3(-half.X, -half.Y, half.Z);
        var p010 = center + new Vector3(-half.X, half.Y, -half.Z);
        var p011 = center + new Vector3(-half.X, half.Y, half.Z);
        var p100 = center + new Vector3(half.X, -half.Y, -half.Z);
        var p101 = center + new Vector3(half.X, -half.Y, half.Z);
        var p110 = center + new Vector3(half.X, half.Y, -half.Z);
        var p111 = center + new Vector3(half.X, half.Y, half.Z);
        AddQuad(surface, p010, p011, p111, p110, color);
        AddQuad(surface, p000, p100, p101, p001, color.Darkened(0.22f));
        AddQuad(surface, p000, p010, p110, p100, color.Darkened(0.12f));
        AddQuad(surface, p001, p101, p111, p011, color.Darkened(0.08f));
        AddQuad(surface, p000, p001, p011, p010, color.Darkened(0.18f));
        AddQuad(surface, p100, p110, p111, p101, color.Darkened(0.14f));
    }

    private static void AddCrossedCard(
        SurfaceTool surface,
        Vector3 center,
        Vector2 size,
        Color color)
    {
        var halfWidth = size.X * 0.5f;
        var halfHeight = size.Y * 0.5f;
        AddQuad(surface,
            center + new Vector3(-halfWidth, -halfHeight, 0f),
            center + new Vector3(-halfWidth, halfHeight, 0f),
            center + new Vector3(halfWidth, halfHeight, 0f),
            center + new Vector3(halfWidth, -halfHeight, 0f), color);
        AddQuad(surface,
            center + new Vector3(0f, -halfHeight, -halfWidth),
            center + new Vector3(0f, -halfHeight, halfWidth),
            center + new Vector3(0f, halfHeight, halfWidth),
            center + new Vector3(0f, halfHeight, -halfWidth), color.Darkened(0.08f));
    }

    private static void AddQuad(
        SurfaceTool surface,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector3 fourth,
        Color color)
    {
        AddVertex(surface, first, color);
        AddVertex(surface, second, color);
        AddVertex(surface, third, color);
        AddVertex(surface, first, color);
        AddVertex(surface, third, color);
        AddVertex(surface, fourth, color);
    }

    private static void AddVertex(SurfaceTool surface, Vector3 vertex, Color color)
    {
        surface.SetColor(color);
        surface.AddVertex(vertex);
    }

    private static void ClearChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
