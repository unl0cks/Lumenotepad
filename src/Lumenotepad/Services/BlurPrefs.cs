namespace Lumenotepad.Services;

/// <summary>Per-surface glass-blur strength preferences (0–100), pushed from settings by the VM.
/// Windows does NOT expose a continuous blur radius — DWM offers fixed levels — so a percentage
/// maps onto the three REAL tiers the OS can do: 0 = completely clear (transparent, no blur at
/// all), up to 50 = the soft gaussian blur-behind, above = the full frosted acrylic. Consumers:
/// ThemeManager (window chrome) and MenuFx (popup menus/flyouts).</summary>
public static class BlurPrefs
{
    public enum Tier { Clear, Soft, Strong }

    public static int MainPct = 100;      // the app window itself
    public static int WindowsPct = 100;   // secondary windows (preferences, wizard, font browser…)
    public static int MenusPct = 100;     // context menus + toolbar flyouts

    public static Tier TierOf(int pct) => pct <= 0 ? Tier.Clear : pct <= 50 ? Tier.Soft : Tier.Strong;

    /// <summary>Plain-language tier name for the preferences value label.</summary>
    public static string TierName(int pct) => TierOf(pct) switch
    {
        Tier.Clear => "Clear",
        Tier.Soft => "Soft blur",
        _ => "Frosted",
    };
}
