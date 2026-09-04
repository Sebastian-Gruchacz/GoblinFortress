using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient.UI.Actors;

public sealed partial class GoblinEquipmentPaperDoll : VBoxContainer
{
    private const float SlotWidth = 76f;
    private const float SlotHeight = 58f;
    private readonly Dictionary<EquipmentSlot, SlotView> _slots = [];
    private Func<EquipmentSlot, string> _describeSlot = null!;
    private Func<ResourceVariant, string> _describeItem = null!;
    private Func<ResourceVariant, Texture2D> _createIcon = null!;
    private SlotView _ammunition = null!;
    private SlotView _provisions = null!;
    private SlotView _water = null!;
    private Texture2D _ammunitionIcon = null!;
    private Texture2D _provisionsIcon = null!;
    private Texture2D _waterIcon = null!;
    private string _emptyText = string.Empty;
    private string _signature = string.Empty;
    private Label _beltLabel = null!;

    public void Configure(
        Func<EquipmentSlot, string> describeSlot,
        Func<ResourceVariant, string> describeItem,
        Func<ResourceVariant, Texture2D> createIcon,
        Texture2D ammunitionIcon,
        Texture2D provisionsIcon,
        Texture2D waterIcon,
        string beltText,
        string emptyText)
    {
        _describeSlot = describeSlot;
        _describeItem = describeItem;
        _createIcon = createIcon;
        _ammunitionIcon = ammunitionIcon;
        _provisionsIcon = provisionsIcon;
        _waterIcon = waterIcon;
        _emptyText = emptyText;
        _signature = string.Empty;
        if (_slots.Count == 0)
        {
            BuildLayout();
        }
        _beltLabel.Text = beltText;
    }

    public void Update(
        IReadOnlyList<EquippedItemSnapshot> equipment,
        int ammunition,
        string ammunitionTooltip,
        int provisions,
        string provisionsTooltip,
        int water,
        string waterTooltip)
    {
        var signature = string.Join(';', equipment.Select(item =>
            $"{(int)item.Slot}:{(int)item.Equipment}:{(int)item.Variant}")) +
            $"|{ammunition}:{provisions}:{water}";
        if (_signature == signature)
        {
            return;
        }
        _signature = signature;

        foreach (var (slot, view) in _slots)
        {
            var item = equipment.FirstOrDefault(candidate => candidate.Slot == slot);
            var occupied = item.Equipment != PersonalEquipment.None;
            var slotName = _describeSlot(slot);
            SetSlot(
                view,
                occupied ? _createIcon(item.Variant) : null,
                occupied ? $"{slotName}: {_describeItem(item.Variant)}" :
                    $"{slotName}: {_emptyText}");
        }
        SetSupplement(_ammunition, _ammunitionIcon, ammunition, ammunitionTooltip);
        SetSupplement(_provisions, _provisionsIcon, provisions, provisionsTooltip);
        SetSupplement(_water, _waterIcon, water, waterTooltip);
    }

    private void BuildLayout()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 5);
        AddEquipmentRow(
            (EquipmentSlot.Head, DollGlyph.Head),
            (EquipmentSlot.Cloak, DollGlyph.Cloak));
        AddEquipmentRow(
            (EquipmentSlot.RingLeft, DollGlyph.Ring),
            (EquipmentSlot.Amulet, DollGlyph.Amulet),
            (EquipmentSlot.RingRight, DollGlyph.Ring));

        var combat = CreateRow();
        combat.AddChild(CreateEquipmentSlot(EquipmentSlot.RangedWeapon, DollGlyph.Bow));
        _ammunition = CreateSlot(DollGlyph.Ammunition);
        combat.AddChild(_ammunition.Root);
        combat.AddChild(CreateEquipmentSlot(EquipmentSlot.Torso, DollGlyph.Torso));
        combat.AddChild(CreateEquipmentSlot(EquipmentSlot.MeleeWeapon, DollGlyph.Sword));
        AddChild(combat);

        var belt = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 22),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        belt.AddThemeStyleboxOverride("panel", CreateBeltStyle());
        _beltLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        belt.AddChild(_beltLabel);
        AddChild(belt);

        AddEquipmentRow(
            (EquipmentSlot.FellingTool, DollGlyph.Axe),
            (EquipmentSlot.ConstructionTool, DollGlyph.Hammer),
            (EquipmentSlot.MiningTool, DollGlyph.Pickaxe),
            (EquipmentSlot.EarthmovingTool, DollGlyph.Shovel));
        var lowerBody = CreateRow();
        _provisions = CreateSlot(DollGlyph.Provisions);
        lowerBody.AddChild(_provisions.Root);
        lowerBody.AddChild(CreateEquipmentSlot(EquipmentSlot.Legs, DollGlyph.Legs));
        _water = CreateSlot(DollGlyph.Water);
        lowerBody.AddChild(_water.Root);
        AddChild(lowerBody);
        AddEquipmentRow((EquipmentSlot.Feet, DollGlyph.Feet));
    }

    private void AddEquipmentRow(params (EquipmentSlot Slot, DollGlyph Glyph)[] slots)
    {
        var row = CreateRow();
        foreach (var (slot, glyph) in slots)
        {
            row.AddChild(CreateEquipmentSlot(slot, glyph));
        }
        AddChild(row);
    }

    private static HBoxContainer CreateRow()
    {
        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 7);
        return row;
    }

    private Control CreateEquipmentSlot(EquipmentSlot slot, DollGlyph glyph)
    {
        var view = CreateSlot(glyph);
        _slots.Add(slot, view);
        return view.Root;
    }

    private static SlotView CreateSlot(DollGlyph glyphKind)
    {
        var root = new PanelContainer
        {
            CustomMinimumSize = new Vector2(SlotWidth, SlotHeight),
        };
        root.AddThemeStyleboxOverride("panel", CreateSlotStyle());
        var layer = new Control
        {
            CustomMinimumSize = new Vector2(SlotWidth - 8f, SlotHeight - 8f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var glyph = new EquipmentSlotGlyph
        {
            Glyph = glyphKind,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var icon = new TextureRect
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 6f,
            OffsetTop = 3f,
            OffsetRight = -6f,
            OffsetBottom = -3f,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var quantity = new Label
        {
            AnchorLeft = 0.55f,
            AnchorTop = 0.58f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        quantity.AddThemeFontSizeOverride("font_size", 11);
        layer.AddChild(glyph);
        layer.AddChild(icon);
        layer.AddChild(quantity);
        root.AddChild(layer);
        return new SlotView(root, glyph, icon, quantity);
    }

    private static void SetSlot(SlotView view, Texture2D? texture, string tooltip)
    {
        view.Icon.Texture = texture;
        view.Glyph.SelfModulate = new Color(1f, 1f, 1f, texture is null ? 1f : 0.18f);
        view.Quantity.Text = string.Empty;
        view.Root.TooltipText = tooltip;
    }

    private static void SetSupplement(SlotView view, Texture2D icon, int quantity, string tooltip)
    {
        view.Icon.Texture = quantity > 0 ? icon : null;
        view.Glyph.SelfModulate = new Color(1f, 1f, 1f, quantity > 0 ? 0.18f : 1f);
        view.Quantity.Text = quantity.ToString("N0");
        view.Root.TooltipText = tooltip;
    }

    private static StyleBoxFlat CreateSlotStyle() => new()
    {
        BgColor = new Color("111b2ee8"),
        BorderColor = new Color("48698ca8"),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 4f,
        ContentMarginTop = 4f,
        ContentMarginRight = 4f,
        ContentMarginBottom = 4f,
    };

    private static StyleBoxFlat CreateBeltStyle() => new()
    {
        BgColor = new Color("182942e8"),
        BorderColor = new Color("6598c9c8"),
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3,
    };

    private sealed record SlotView(
        PanelContainer Root,
        EquipmentSlotGlyph Glyph,
        TextureRect Icon,
        Label Quantity);
}

internal enum DollGlyph : byte
{
    Head, Cloak, Ring, Amulet, Bow, Ammunition, Torso, Sword,
    Axe, Hammer, Pickaxe, Shovel, Provisions, Legs, Water, Feet,
}

internal sealed partial class EquipmentSlotGlyph : Control
{
    private static readonly Color Ink = new("76a9d5a8");
    public DollGlyph Glyph { get; init; }

    public override void _Draw()
    {
        var center = Size / 2f;
        var scale = MathF.Min(Size.X, Size.Y) / 48f;
        Vector2 P(float x, float y) => center + new Vector2(x, y) * scale;
        void Line(float x1, float y1, float x2, float y2, float width = 2f) =>
            DrawLine(P(x1, y1), P(x2, y2), Ink, width * scale, true);
        void Circle(float x, float y, float radius, float width = 2f) =>
            DrawCircle(P(x, y), radius * scale, Ink, false, width * scale, true);

        switch (Glyph)
        {
            case DollGlyph.Head:
                Circle(0, -2, 11); Line(-8, 9, 8, 9); break;
            case DollGlyph.Cloak:
                DrawColoredPolygon([P(0, -14), P(-15, 16), P(15, 16)], Ink); break;
            case DollGlyph.Ring:
                Circle(0, 2, 9, 3); Line(-5, -8, 0, -14); Line(0, -14, 5, -8); break;
            case DollGlyph.Amulet:
                Line(-10, -14, 0, 3); Line(10, -14, 0, 3); Circle(0, 9, 6); break;
            case DollGlyph.Bow:
                DrawArc(P(-2, 0), 16f * scale, -1.2f, 1.2f, 18, Ink, 2f * scale, true);
                Line(4, -15, 4, 15, 1.5f); break;
            case DollGlyph.Ammunition:
                Line(-10, 13, 8, -13); Line(-2, 13, 16, -13);
                Line(5, -10, 8, -13); Line(8, -13, 8, -8); break;
            case DollGlyph.Torso:
                DrawPolyline([P(-12, -12), P(-18, -4), P(-11, 2), P(-8, -2),
                    P(-8, 15), P(8, 15), P(8, -2), P(11, 2), P(18, -4), P(12, -12)],
                    Ink, 2f * scale, true); break;
            case DollGlyph.Sword:
                Line(-11, 13, 10, -12, 3); Line(-12, 6, -4, 14); break;
            case DollGlyph.Axe:
                Line(-9, 15, 7, -14, 3);
                DrawColoredPolygon([P(4, -15), P(15, -13), P(13, -3), P(2, -5)], Ink); break;
            case DollGlyph.Hammer:
                Line(-8, 15, 5, -10, 3);
                DrawRect(new Rect2(P(-4, -15), new Vector2(19, 8) * scale), Ink); break;
            case DollGlyph.Pickaxe:
                Line(0, 15, 0, -10, 3);
                DrawArc(P(0, -7), 14f * scale, MathF.PI, MathF.Tau, 18, Ink, 3f * scale, true);
                break;
            case DollGlyph.Shovel:
                Line(0, 15, 0, -8, 3);
                DrawPolyline([P(-8, -8), P(0, -16), P(8, -8), P(6, 2), P(0, 7),
                    P(-6, 2), P(-8, -8)], Ink, 2.5f * scale, true);
                break;
            case DollGlyph.Provisions:
                Circle(0, 2, 11); Line(-3, -9, 4, -15); break;
            case DollGlyph.Legs:
                Line(-7, -15, -7, 12, 5); Line(7, -15, 7, 12, 5); Line(-8, -15, 8, -15, 3);
                break;
            case DollGlyph.Water:
                DrawColoredPolygon([P(0, -16), P(-11, 5), P(-8, 13), P(0, 16),
                    P(8, 13), P(11, 5)], Ink); break;
            case DollGlyph.Feet:
                Line(-8, -13, -8, 8, 5); Line(8, -13, 8, 8, 5);
                Line(-9, 9, -17, 13, 5); Line(9, 9, 17, 13, 5); break;
        }
    }
}
