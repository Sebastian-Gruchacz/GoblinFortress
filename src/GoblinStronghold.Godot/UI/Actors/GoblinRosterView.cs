using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient.UI.Actors;

public sealed class GoblinRosterView
{
    private static readonly Color HealthColor = new("c95656");
    private static readonly Color StaminaColor = new("62b86b");
    private static readonly Color ManaColor = new("557bd1");
    private static readonly Color NutritionColor = new("c99b55");
    private static readonly Color HydrationColor = new("55a8d1");
    private readonly VBoxContainer _rows;
    private readonly Action<EntityId, bool> _activateActor;
    private readonly Func<ActorSnapshot, string> _describeJob;
    private readonly Func<ResourceVariant, string> _describeItem;
    private readonly Func<ResourceVariant, Texture2D> _createIcon;
    private readonly Func<string, string> _text;
    private readonly Dictionary<EntityId, GoblinRow> _actorRows = [];

    public GoblinRosterView(
        VBoxContainer rows,
        Action<EntityId, bool> activateActor,
        Func<ActorSnapshot, string> describeJob,
        Func<ResourceVariant, string> describeItem,
        Func<ResourceVariant, Texture2D> createIcon,
        Func<string, string> text)
    {
        _rows = rows;
        _activateActor = activateActor;
        _describeJob = describeJob;
        _describeItem = describeItem;
        _createIcon = createIcon;
        _text = text;
    }

    public void Update(
        IReadOnlyList<ActorSnapshot> actors,
        int maximumFatigue,
        int maximumHunger,
        int maximumThirst)
    {
        var livingIds = actors.Select(actor => actor.Id).ToHashSet();
        foreach (var removedId in _actorRows.Keys.Where(id => !livingIds.Contains(id)).ToArray())
        {
            var removed = _actorRows[removedId];
            _actorRows.Remove(removedId);
            removed.Root.QueueFree();
        }

        var ordered = actors.OrderBy(actor => actor.Id).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var actor = ordered[index];
            if (!_actorRows.TryGetValue(actor.Id, out var row))
            {
                row = CreateRow(actor.Id);
                _actorRows.Add(actor.Id, row);
                _rows.AddChild(row.Root);
            }
            _rows.MoveChild(row.Root, index);
            UpdateRow(row, actor, maximumFatigue, maximumHunger, maximumThirst);
        }
    }

    private GoblinRow CreateRow(EntityId actorId)
    {
        var root = new HBoxContainer { CustomMinimumSize = new Vector2(0, 46) };
        root.AddThemeConstantOverride("separation", 6);
        var name = new LinkButton
        {
            CustomMinimumSize = new Vector2(110, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            Underline = LinkButton.UnderlineMode.Always,
        };
        var openDetails = false;
        name.GuiInput += inputEvent =>
        {
            if (inputEvent is InputEventMouseButton
                {
                    ButtonIndex: MouseButton.Left,
                    Pressed: true,
                } mouse)
            {
                openDetails = mouse.CtrlPressed;
            }
            else if (inputEvent is InputEventKey { Pressed: true } key)
            {
                openDetails = key.CtrlPressed;
            }
        };
        name.Pressed += () =>
        {
            _activateActor(actorId, openDetails);
            openDetails = false;
        };
        var age = new Label
        {
            CustomMinimumSize = new Vector2(42, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tools = CreateIconStrip(78);
        var weapons = CreateIconStrip(54);
        var vitals = CreateBarColumn(88);
        var health = CreateVitalBar(HealthColor);
        var stamina = CreateVitalBar(StaminaColor);
        var mana = CreateVitalBar(ManaColor);
        vitals.AddChild(health);
        vitals.AddChild(stamina);
        vitals.AddChild(mana);
        var needs = CreateBarColumn(64);
        var nutrition = CreateVitalBar(NutritionColor, 64);
        var hydration = CreateVitalBar(HydrationColor, 64);
        needs.AddChild(nutrition);
        needs.AddChild(hydration);
        var job = new Label
        {
            CustomMinimumSize = new Vector2(30, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        root.AddChild(name);
        root.AddChild(age);
        root.AddChild(tools);
        root.AddChild(weapons);
        root.AddChild(vitals);
        root.AddChild(needs);
        root.AddChild(job);
        return new GoblinRow(
            root, name, age, tools, weapons,
            health, stamina, mana, nutrition, hydration, job);
    }

    private void UpdateRow(
        GoblinRow row,
        ActorSnapshot actor,
        int maximumFatigue,
        int maximumHunger,
        int maximumThirst)
    {
        row.Name.Text = actor.Name;
        row.Name.TooltipText = string.Format(_text("select-tooltip"), actor.Name);
        row.Age.Text = string.Format(_text("age"), actor.AgeDays);
        row.Age.TooltipText = string.Format(_text("age-tooltip"), actor.AgeDays);
        row.Job.Text = DescribeJobSymbol(actor.Job.Kind);
        row.Job.TooltipText = _describeJob(actor);
        SetVital(row.Health, actor.Health, actor.EffectiveMaximumHealth,
            string.Format(_text("health"), actor.Health, actor.EffectiveMaximumHealth));
        var stamina = Math.Max(0, maximumFatigue - actor.Fatigue);
        SetVital(row.Stamina, stamina, maximumFatigue,
            string.Format(_text("stamina"), stamina, maximumFatigue));
        SetVital(row.Mana, actor.Mana, actor.MaximumMana,
            string.Format(_text("mana"), actor.Mana, actor.MaximumMana));
        var nutrition = Math.Max(0, maximumHunger - actor.Hunger);
        SetVital(row.Nutrition, nutrition, maximumHunger,
            string.Format(_text("nutrition"), nutrition, maximumHunger));
        var hydration = Math.Max(0, maximumThirst - actor.Thirst);
        SetVital(row.Hydration, hydration, maximumThirst,
            string.Format(_text("hydration"), hydration, maximumThirst));

        var equipmentSignature = ((int)actor.Equipment).ToString();
        if (row.EquipmentSignature == equipmentSignature)
        {
            return;
        }
        row.EquipmentSignature = equipmentSignature;
        PopulateEquipmentStrip(
            row.Tools,
            actor.Loadout.Items.Where(item => item.Slot is EquipmentSlot.ConstructionTool or
                EquipmentSlot.MiningTool or EquipmentSlot.FellingTool or
                EquipmentSlot.EarthmovingTool),
            _text("tools-empty"));
        PopulateEquipmentStrip(
            row.Weapons,
            actor.Loadout.Items.Where(item => item.Slot is EquipmentSlot.RangedWeapon or
                EquipmentSlot.MeleeWeapon),
            _text("weapons-empty"));
    }

    private void PopulateEquipmentStrip(
        HBoxContainer strip,
        IEnumerable<EquippedItemSnapshot> equipment,
        string emptyTooltip)
    {
        foreach (var child in strip.GetChildren())
        {
            strip.RemoveChild(child);
            child.QueueFree();
        }
        var items = equipment.ToArray();
        strip.TooltipText = items.Length == 0
            ? emptyTooltip
            : string.Join(", ", items.Select(item => _describeItem(item.Variant)));
        foreach (var item in items)
        {
            strip.AddChild(new TextureRect
            {
                CustomMinimumSize = new Vector2(24, 24),
                Texture = _createIcon(item.Variant),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TooltipText = _describeItem(item.Variant),
            });
        }
    }

    private static HBoxContainer CreateIconStrip(float width)
    {
        var strip = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(width, 0),
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        strip.AddThemeConstantOverride("separation", 2);
        return strip;
    }

    private static VBoxContainer CreateBarColumn(float width)
    {
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(width, 0),
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        column.AddThemeConstantOverride("separation", 2);
        return column;
    }

    private static ProgressBar CreateVitalBar(Color color, float width = 88)
    {
        var bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(width, 5),
            ShowPercentage = false,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat
        {
            BgColor = new Color("18202ad0"),
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
        });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
        });
        return bar;
    }

    private static void SetVital(ProgressBar bar, int value, int maximum, string tooltip)
    {
        bar.MaxValue = Math.Max(1, maximum);
        bar.Value = Math.Clamp(value, 0, Math.Max(1, maximum));
        bar.TooltipText = tooltip;
    }

    private static string DescribeJobSymbol(ActorJobKind kind) => kind switch
    {
        ActorJobKind.Forage => "⌕",
        ActorJobKind.Haul => "⇢",
        ActorJobKind.Rest => "Zz",
        ActorJobKind.Eat => "●",
        ActorJobKind.Explore => "⌖",
        ActorJobKind.Move => "→",
        ActorJobKind.Resupply => "↺",
        ActorJobKind.ClearVegetation => "×",
        ActorJobKind.SupplyConstruction => "⇥",
        ActorJobKind.BuildConstruction or ActorJobKind.DismantleConstruction => "⚒",
        ActorJobKind.Collapsed => "!",
        ActorJobKind.FellTree => "♣",
        ActorJobKind.QuarryBoulder => "◆",
        ActorJobKind.MineRock => "⛏",
        ActorJobKind.CarveRamp => "⇅",
        ActorJobKind.TendBud => "♧",
        ActorJobKind.HuntAnimal => "⚔",
        ActorJobKind.SupplyCrafting => "⇥",
        ActorJobKind.Craft => "⚒",
        ActorJobKind.ClearConstructionSite => "↗",
        ActorJobKind.CleanBlood => "✦",
        ActorJobKind.LootRaid => "▣",
        ActorJobKind.RecoverRaidCorpse => "†",
        ActorJobKind.ConsumeRaidCorpse => "☠",
        _ => "·",
    };

    private sealed class GoblinRow(
        HBoxContainer root,
        LinkButton name,
        Label age,
        HBoxContainer tools,
        HBoxContainer weapons,
        ProgressBar health,
        ProgressBar stamina,
        ProgressBar mana,
        ProgressBar nutrition,
        ProgressBar hydration,
        Label job)
    {
        public HBoxContainer Root { get; } = root;
        public LinkButton Name { get; } = name;
        public Label Age { get; } = age;
        public HBoxContainer Tools { get; } = tools;
        public HBoxContainer Weapons { get; } = weapons;
        public ProgressBar Health { get; } = health;
        public ProgressBar Stamina { get; } = stamina;
        public ProgressBar Mana { get; } = mana;
        public ProgressBar Nutrition { get; } = nutrition;
        public ProgressBar Hydration { get; } = hydration;
        public Label Job { get; } = job;
        public string EquipmentSignature { get; set; } = string.Empty;
    }
}
