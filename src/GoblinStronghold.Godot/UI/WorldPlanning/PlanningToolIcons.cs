using Godot;

namespace GoblinStronghold.GodotClient.UI.WorldPlanning;

internal static class PlanningToolIcons
{
    public static Texture2D CreateBasicConstructionIcon(Texture2D construction) =>
        CreateFramedIcon(construction, CreateSvgIcon("""
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <rect x="3" y="3" width="58" height="58" rx="10" fill="#344d2c" stroke="#78934f" stroke-width="3"/>
              <path d="M9 49 Q20 42 31 48 T55 47 V59 H9 Z" fill="#49663a" opacity="0.9"/>
            </svg>
            """, "basic construction background"));

    public static Texture2D CreateAdvancedConstructionIcon(Texture2D construction) =>
        CreateBadgedIcon(construction, CreateSvgIcon("""
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
              <circle cx="16" cy="16" r="14" fill="#493522" stroke="#f2d889" stroke-width="2"/>
              <path d="M16 5 L19 12 L27 12 L21 17 L23 25 L16 21 L9 25 L11 17 L5 12 L13 12 Z"
                    fill="#f5db72" stroke="#5b3f1c" stroke-width="1.5" stroke-linejoin="round"/>
              <circle cx="16" cy="16" r="3" fill="#fff1ad"/>
            </svg>
            """, "advanced construction badge"));

    public static Texture2D CreateWoodenBoxIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M7 20 H57 V56 H7 Z" fill="#80542f" stroke="#2d1c12" stroke-width="4"/>
          <path d="M10 30 H54 M10 45 H54 M20 22 V54 M44 22 V54" stroke="#c08a4d" stroke-width="3"/>
          <path d="M5 17 H59 V24 H5 Z" fill="#a56f3b" stroke="#2d1c12" stroke-width="3"/>
        </svg>
        """, "wooden box");

    public static Texture2D CreateWoodenChestIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M8 26 Q8 10 24 8 H40 Q56 10 56 26 V55 H8 Z" fill="#78502e" stroke="#2b1b12" stroke-width="4"/>
          <path d="M10 27 H54 M18 10 V54 M46 10 V54" fill="none" stroke="#c08a4d" stroke-width="3"/>
          <rect x="27" y="25" width="10" height="15" rx="2" fill="#c49a45" stroke="#3b2d16" stroke-width="2"/>
          <circle cx="32" cy="31" r="2" fill="#302417"/>
        </svg>
        """, "wooden chest");

    public static Texture2D CreateBulkBinIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M8 16 H56 L51 57 H13 Z" fill="#6f4728" stroke="#2c1b12" stroke-width="4" stroke-linejoin="round"/>
          <path d="M10 18 Q32 30 54 18" fill="#211813" stroke="#c08a4d" stroke-width="3"/>
          <circle cx="22" cy="21" r="7" fill="#777b78" stroke="#323534" stroke-width="2"/>
          <circle cx="34" cy="20" r="8" fill="#96978e" stroke="#323534" stroke-width="2"/>
          <circle cx="45" cy="22" r="6" fill="#646966" stroke="#323534" stroke-width="2"/>
          <path d="M20 31 V52 M32 29 V54 M44 31 V52" stroke="#b47b40" stroke-width="3"/>
        </svg>
        """, "bulk bin");

    public static Texture2D CreateStorageAreaIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M7 8 H57 V56 H7 Z" fill="#263225" fill-opacity="0.65" stroke="#e1c66f" stroke-width="3" stroke-dasharray="7 5"/>
          <path d="M7 18 V8 H17 M47 8 H57 V18 M57 46 V56 H47 M17 56 H7 V46" fill="none" stroke="#f0df9b" stroke-width="5"/>
          <path d="M18 31 H46 V49 H18 Z" fill="#79502f" stroke="#2b1b12" stroke-width="3"/>
          <path d="M20 38 H44 M27 32 V48 M38 32 V48" stroke="#c08a4d" stroke-width="2"/>
        </svg>
        """, "storage area");

    public static Texture2D CreateDryingRackIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M10 57 L19 12 L28 57 M36 57 L45 12 L54 57 M17 18 H47" fill="none" stroke="#6f4728" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/>
          <path d="M25 18 V29 M38 18 V31" stroke="#c49a62" stroke-width="2"/>
          <path d="M18 33 Q25 25 32 33 Q25 41 18 33 Z M31 35 Q39 27 47 35 Q39 44 31 35 Z" fill="#b67a4c" stroke="#492c1d" stroke-width="2"/>
          <path d="M18 33 L13 29 V37 Z M47 35 L52 31 V39 Z" fill="#8f5b3b" stroke="#492c1d" stroke-width="2"/>
        </svg>
        """, "drying rack");

    public static Texture2D CreateCookingFireIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M13 52 L51 52 M17 58 L47 45 M17 45 L47 58" stroke="#704526" stroke-width="6" stroke-linecap="round"/>
          <path d="M32 49 C16 42 22 27 34 18 C32 29 47 31 41 43 C39 48 35 50 32 49 Z" fill="#f06b1d" stroke="#7d2b0b" stroke-width="3"/>
          <path d="M32 45 C26 40 29 34 35 29 C34 36 40 37 37 43 C36 45 34 46 32 45 Z" fill="#ffe05b"/>
          <path d="M18 13 H46 L43 29 Q32 35 21 29 Z" fill="#3d4241" stroke="#171918" stroke-width="3"/>
          <path d="M16 11 H48" stroke="#a9aaa1" stroke-width="4" stroke-linecap="round"/>
        </svg>
        """, "cooking fire");

    public static Texture2D CreateWoodenWalkwayIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <rect x="4" y="4" width="56" height="56" rx="7" fill="#273d3c" stroke="#182827" stroke-width="3"/>
          <path d="M4 11 H20 L17 25 H4 Z M44 11 H60 V25 H47 Z M4 43 H12 L9 57 H4 Z M52 43 H60 V57 H55 Z" fill="#48643a" stroke="#263920" stroke-width="2"/>
          <path d="M20 4 H44 L55 60 H9 Z" fill="#76502e" stroke="#2d1c12" stroke-width="3"/>
          <path d="M18 14 H46 M16 26 H48 M13 40 H51 M10 54 H54" stroke="#c28b4b" stroke-width="4"/>
          <path d="M24 5 L18 59 M40 5 L46 59" stroke="#3f291a" stroke-width="3"/>
        </svg>
        """, "wooden walkway");

    public static Texture2D CreateBasaltWalkwayIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <rect x="4" y="4" width="56" height="56" rx="7" fill="#273d3c" stroke="#182827" stroke-width="3"/>
          <path d="M4 11 H20 L17 25 H4 Z M44 11 H60 V25 H47 Z M4 43 H12 L9 57 H4 Z M52 43 H60 V57 H55 Z" fill="#48643a" stroke="#263920" stroke-width="2"/>
          <path d="M20 4 H44 L55 60 H9 Z" fill="#60646a" stroke="#25282b" stroke-width="3"/>
          <path d="M18 15 H46 M15 30 H49 M12 45 H52 M26 5 L22 60 M38 5 L43 60" stroke="#9b9d9c" stroke-width="2"/>
          <path d="M18 15 L24 30 M40 15 L34 30 M22 30 L17 45 M42 30 L48 45" stroke="#3d4144" stroke-width="2"/>
        </svg>
        """, "basalt walkway");

    public static Texture2D CreateRampIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <rect x="4" y="4" width="56" height="56" rx="7" fill="#3f382d" stroke="#241f19" stroke-width="3"/>
          <path d="M22 4 H42 L58 60 H6 Z" fill="#6f4d31" stroke="#2d1c12" stroke-width="3"/>
          <path d="M21 13 H43 L46 23 H18 Z M17 27 H47 L50 38 H14 Z M13 42 H51 L55 55 H9 Z" fill="#a8753f" stroke="#3d2819" stroke-width="2"/>
          <path d="M25 5 L18 59 M39 5 L46 59" stroke="#d09a52" stroke-width="3"/>
          <path d="M32 52 V18 M25 25 L32 17 L39 25" fill="none" stroke="#f0dc91" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        """, "ramp");

    public static Texture2D CreatePathIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <rect x="4" y="4" width="56" height="56" rx="7" fill="#49663a" stroke="#263920" stroke-width="3"/>
          <path d="M14 60 C9 43 31 42 24 29 C19 19 37 16 43 4 H58 C49 20 35 24 40 35 C46 49 29 52 31 60 Z" fill="#aa8757" stroke="#5e472f" stroke-width="3"/>
          <path d="M22 49 L32 46 M27 35 L37 32 M31 20 L42 17" stroke="#d4b67a" stroke-width="3" stroke-linecap="round"/>
        </svg>
        """, "path");

    public static Texture2D CreateRoadIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M18 4 H46 L58 60 H6 Z" fill="#747570" stroke="#292b2a" stroke-width="3"/>
          <path d="M16 18 H48 M13 34 H51 M9 51 H55 M27 5 L24 60 M39 5 L43 60" stroke="#b3aa94" stroke-width="2"/>
          <path d="M18 18 L24 34 M39 18 L34 34 M24 34 L18 51 M43 34 L48 51" stroke="#4c4d49" stroke-width="2"/>
        </svg>
        """, "road");

    public static Texture2D CreateRaiseTerrainIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M7 42 L27 32 L48 41 L28 52 Z" fill="#8b6a45" stroke="#35271b" stroke-width="3"/>
          <path d="M7 42 V52 L28 61 V52 M48 41 V51 L28 61" fill="#674b32" stroke="#35271b" stroke-width="3"/>
          <path d="M38 37 V15 H31 L43 3 L55 15 H48 V37" fill="#e8d37f" stroke="#4a3a20" stroke-width="3" stroke-linejoin="round"/>
        </svg>
        """, "raise terrain");

    public static Texture2D CreateLevelTerrainIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M5 43 L23 34 L40 42 L22 52 Z M33 28 L48 20 L60 27 L45 35 Z" fill="#8b6a45" stroke="#35271b" stroke-width="3"/>
          <path d="M5 43 V53 L22 61 V52 L40 42 V52 L22 61 M33 28 V39 L45 46 V35 L60 27 V38 L45 46" fill="#674b32" stroke="#35271b" stroke-width="3"/>
          <path d="M8 14 H56" stroke="#e8d37f" stroke-width="5" stroke-linecap="round"/>
          <circle cx="32" cy="14" r="7" fill="#334c4b" stroke="#e8d37f" stroke-width="3"/>
          <path d="M28 14 H36" stroke="#a9e1da" stroke-width="3" stroke-linecap="round"/>
        </svg>
        """, "level terrain");

    public static Texture2D CreateHuntDesignationIcon(Texture2D sling) =>
        CreateBadgedIcon(sling, CreatePawBadgeIcon());

    public static Texture2D CreateHuntAreaIcon(Texture2D sling) =>
        CreateBadgedIcon(sling, CreateTargetBadgeIcon());

    public static Texture2D CreatePatrolIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M12 51 C18 30 45 48 51 23" fill="none" stroke="#e4cf78" stroke-width="4" stroke-dasharray="5 5" stroke-linecap="round"/>
          <circle cx="12" cy="51" r="7" fill="#53763d" stroke="#24351e" stroke-width="3"/>
          <circle cx="51" cy="23" r="7" fill="#a44935" stroke="#3e1d18" stroke-width="3"/>
          <path d="M15 45 V9 M15 10 L38 16 L15 25 Z" fill="#c35a3b" stroke="#3e2119" stroke-width="3" stroke-linejoin="round"/>
        </svg>
        """, "patrol");

    public static Texture2D CreateScoutIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <path d="M5 34 Q32 8 59 34 Q32 58 5 34 Z" fill="#d7c27d" stroke="#45361e" stroke-width="4"/>
          <circle cx="32" cy="34" r="12" fill="#567f68" stroke="#263d32" stroke-width="3"/>
          <circle cx="32" cy="34" r="5" fill="#151b18"/>
          <path d="M12 54 L23 43 M52 54 L41 43" stroke="#6f4728" stroke-width="5" stroke-linecap="round"/>
          <path d="M9 56 H55" stroke="#3d2b1e" stroke-width="3"/>
        </svg>
        """, "scout");

    private static Texture2D CreatePawBadgeIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
          <circle cx="16" cy="16" r="14" fill="#4b3928" stroke="#f2d889" stroke-width="2"/>
          <ellipse cx="16" cy="20" rx="7" ry="6" fill="#f7edc0"/>
          <circle cx="8" cy="13" r="3" fill="#f7edc0"/><circle cx="14" cy="9" r="3" fill="#f7edc0"/>
          <circle cx="20" cy="9" r="3" fill="#f7edc0"/><circle cx="25" cy="14" r="3" fill="#f7edc0"/>
        </svg>
        """, "hunt designation badge");

    private static Texture2D CreateTargetBadgeIcon() => CreateSvgIcon("""
        <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
          <circle cx="16" cy="16" r="14" fill="#5a2925" stroke="#f2d889" stroke-width="2"/>
          <circle cx="16" cy="16" r="8" fill="none" stroke="#f7edc0" stroke-width="3"/>
          <path d="M16 4 V11 M16 21 V28 M4 16 H11 M21 16 H28" stroke="#f7edc0" stroke-width="3"/>
        </svg>
        """, "hunt area badge");

    private static Texture2D CreateBadgedIcon(Texture2D foundation, Texture2D badge)
    {
        var image = foundation.GetImage();
        image.Resize(64, 64, Image.Interpolation.Lanczos);
        var badgeImage = badge.GetImage();
        badgeImage.Resize(27, 27, Image.Interpolation.Lanczos);
        image.BlendRect(badgeImage, new Rect2I(0, 0, 27, 27), new Vector2I(36, 36));
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D CreateFramedIcon(Texture2D foreground, Texture2D background)
    {
        var image = background.GetImage();
        image.Resize(64, 64, Image.Interpolation.Lanczos);
        var foregroundImage = foreground.GetImage();
        foregroundImage.Resize(64, 64, Image.Interpolation.Lanczos);
        image.BlendRect(foregroundImage, new Rect2I(0, 0, 64, 64), Vector2I.Zero);
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D CreateSvgIcon(string svg, string name)
    {
        var image = new Image();
        if (image.LoadSvgFromString(svg) != Error.Ok)
        {
            throw new InvalidOperationException($"Cannot create the {name} icon.");
        }
        return ImageTexture.CreateFromImage(image);
    }
}
