using UnityEngine;

namespace ReelsMods
{
    /// <summary>
    /// Palette + sizing for the REELS MODS panel.
    /// Kept in one place so the theme row on the MAIN tab can retint the
    /// whole menu without rebuilding any geometry.
    /// </summary>
    public static class Theme
    {
        public const string Title = "R E E L S   M O D S";

        public static readonly Color Accent      = new Color(0.780f, 0.608f, 1.000f); // #C79BFF
        public static readonly Color AccentDim   = new Color(0.480f, 0.360f, 0.680f);
        public static readonly Color PanelBg     = new Color(0.055f, 0.050f, 0.075f, 0.96f);
        public static readonly Color RowBg       = new Color(0.110f, 0.100f, 0.150f, 1.00f);
        public static readonly Color RowBgHover  = new Color(0.165f, 0.150f, 0.230f, 1.00f);
        public static readonly Color TabBg       = new Color(0.090f, 0.082f, 0.125f, 1.00f);
        public static readonly Color TextMain    = new Color(0.940f, 0.930f, 0.970f);
        public static readonly Color TextMuted   = new Color(0.600f, 0.580f, 0.660f);
        public static readonly Color ToggleOff   = new Color(0.240f, 0.230f, 0.290f);
        public static readonly Color ToastBg     = new Color(0.140f, 0.120f, 0.220f, 0.97f);

        // Canvas units. localScale turns this into metres.
        public const float CanvasW   = 1000f;
        public const float CanvasH   = 1300f;
        public const float CanvasScale = 0.0008f;   // -> 0.80m x 1.04m

        public const float TitleY    = 570f;
        public const float TitleH    = 100f;
        public const float TabY      = 470f;
        public const float TabH      = 80f;
        public const float RowTop    = 410f;
        public const float RowH      = 100f;
        public const float RowGap    = 18f;
        public const int   RowsPerPage = 6;
        public const float FooterY   = -370f;
        public const float FooterH   = 90f;
        public const float ToastY    = -560f;
        public const float ToastH    = 90f;

        // One shared font instance for every label on the menu. Per-label font
        // instances are what churned the dynamic font texture in RIFTIX.
        private static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null)
                        _font = Font.CreateDynamicFontFromOSFont("Arial", 24);
                }
                return _font;
            }
        }
    }

    public static class Tabs
    {
        public const string Main   = "MAIN";
        public const string Reels  = "REELS";
        public const string Pages  = "PAGES";
        public static readonly string[] All = { Main, Reels, Pages };
    }

    /// <summary>One mod row: label on the left, ON/OFF pill on the right.</summary>
    public class ModDef
    {
        public string id;
        public string label;
        public bool on;
        public string[] pages;   // which tab ids this row belongs to

        public ModDef(string id, string label, bool on, params string[] pages)
        {
            this.id = id;
            this.label = label;
            this.on = on;
            this.pages = pages;
        }
    }

    public static class Mods
    {
        public static readonly ModDef[] All =
        {
            // REELS tab - the actual game mods
            new ModDef("give_coins",    "Give Coins",        true,  Tabs.Reels),
            new ModDef("give_bundles",  "Give Bundles",      true,  Tabs.Reels),
            new ModDef("unlock_rods",   "Unlock Every Rod",  true,  Tabs.Reels),
            new ModDef("instant_reel",  "Instant Reel",      false, Tabs.Reels),
            new ModDef("auto_catch",    "Auto Catch",        false, Tabs.Reels),
            new ModDef("monster_lure",  "Monster Lure",      true,  Tabs.Reels),
            new ModDef("zero_cooldown", "Zero Lure Cooldown",false, Tabs.Reels),
            new ModDef("god_fish",      "Never Lose A Fish", false, Tabs.Reels),

            // MAIN tab - menu settings
            new ModDef("set_hand",      "Use Right Hand",    false, Tabs.Main),
            new ModDef("set_notif",     "Notifications",     true,  Tabs.Main),
            new ModDef("set_laser",     "Pointer Laser",     true,  Tabs.Main),
            new ModDef("set_pop",       "Button Pop",        true,  Tabs.Main),
            new ModDef("set_big",       "Big Panel",         false, Tabs.Main),
            new ModDef("close_menu",    "Close Menu",        false, Tabs.Main),

            // PAGES tab - page nav + filler rows for future mods
            new ModDef("page_next",     "Next Page >>",      false, Tabs.Pages),
            new ModDef("page_prev",     "<< Previous Page",  false, Tabs.Pages),
            new ModDef("save_loadout",  "Save Loadout",      true,  Tabs.Pages),
            new ModDef("reset_loadout", "Reset Loadout",     false, Tabs.Pages),
            new ModDef("about",         "About REELS MODS",  false, Tabs.Pages),
            new ModDef("stub",          "Reserved Slot",     false, Tabs.Pages),
        };
    }
}
