# PF2 — Follow-ups on the PF1 Round

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Owner re-tests of PF1 found: (1) borders still strong and now WARM-TINTED (translucent
white borders composite over the wallpaper-showing acrylic — lower alpha let MORE wallpaper bleed
through) → make them OPAQUE; (2) fonts list still short of the window bottom (the −200 header guess
undershoots) → anchor to the list's real Y; (3) Data & tools buttons off-style → LumenButton; (4)
both windows bigger by default; (5) Lumen+Full-theme right-click menus MUST truly blur.

**Verified context:**
- `DwmAcrylic.Apply(Window, Backdrop, bool dark)` (Platform/DwmAcrylic.cs) resolves the HWND via
  `TryGetPlatformHandle()` then `DwmExtendFrameIntoClientArea` + `DWMWA_SYSTEMBACKDROP_TYPE`.
  ContextMenu popups live in a `PopupRoot` (a `WindowBase`, NOT `Window`) — that's why PF1's gate
  no-oped. `PopupRoot` IS a `TopLevel`: `TopLevel.GetTopLevel(menu)` returns it, it has
  `TryGetPlatformHandle()` and a settable `TransparencyLevelHint`.
- `ThemeManager.Current.MenuBackground` (PF1): Lumen full-off `#F5171922`, full-on `#B814161C`,
  solid themes opaque. Alpha < 0xF0 ⇒ the translucent (glass-intent) variant — usable as the gate.
- `LumenButton` ControlTheme (Themes/Theme.axaml): accent gradient bg, AccentDeep border, radius 10,
  Padding 14,8, FontSize 13, press scale via code-behind-safe TransformOperationsTransition.
- MainWindow.axaml: `Width="1180" Height="720" MinWidth="720" MinHeight="460"`.
  PreferencesWindow: `Width="840" Height="620"`.
- Data & tools buttons (PreferencesWindow.axaml, DataPanel): `OpenDataBtn`, `BackupFolderBtn`,
  `BackupClearBtn`, `BackupNowBtn`, `ExportBtn`, `ImportBtn`, `ResetBtn`, `RelockBtn` — plain Buttons.
- Fonts list height (PF1): `PrefsScroll.SizeChanged → FontsList.Height = Max(240, vh − 200)`.
- Build gotchas: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before every build/test;
  `cd /e/CLAUDE/Lumenotepad` per Bash call; never launch the GUI. Suite: 175 green.

---

### Task 1: Opaque borders (kills the wallpaper tint)

**Files:** `src/Lumenotepad/Services/ThemePalettes.cs` (+ `ThemePalettesTests` only if literals pinned)

- [ ] Replace the PF1 translucent border values with OPAQUE family-tinted colors (Lumen untouched):

| Theme | frameBorder | solidPaperBorder | CanvasChipBorder |
|---|---|---|---|
| Dark | `#14FFFFFF` → `#FF292C34` | `#1AFFFFFF` → `#FF2C2F38` | dark: `#22FFFFFF` → `#FF383B44` |
| Light | `#10000000` → `#FFDFE3E9` | `#12000000` → `#FFE3E6EC` | light: `#12000000` → `#FFDCE0E7` |
| Pink | `#14B0526E` → `#FFF4D3DC` | `#1AC97D97` → `#FFF4DEE5` | (shared light value above) |
| Light blue | `#145F7BAE` → `#FFDCE4F1` | `#1A6E86B8` → `#FFE1E8F3` | (shared light value above) |

  CanvasChipBorder is a shared dark/light pair inside `Solid(...)` — use `#FF383B44` / `#FFDCE0E7`.
  The Full-theme-OFF glass-paper `PaperBorder` (currently `#1AFFFFFF` in the shared branch): replace
  with `dark ? "#FF3A3D46" : "#FFC9CED6"` (opaque neutral — the glass region shows wallpaper, a
  neutral opaque edge is the point). Add ONE comment at the first substitution: opaque borders —
  translucent ones composite over the wallpaper-showing acrylic and pick up its tint (owner report).
- [ ] Build + suite (update only literal-pinning test assertions, if any). Commit:
  `git commit -m "fix(pf2): opaque theme borders — translucent ones picked up wallpaper tint over glass"`

### Task 2: Fonts-list height anchored + Data & tools buttons + bigger windows

**Files:** `src/Lumenotepad/Views/PreferencesWindow.axaml(.cs)`, `src/Lumenotepad/Views/MainWindow.axaml`

- [ ] Fonts height: replace the `vh − 200` guess with the list's REAL offset. Extract a method and
  call it from BOTH the existing `PrefsScroll.SizeChanged` hook AND the end of `RefreshFontChoices()`
  (posted at Background priority so layout has run — the panel must be visible for TranslatePoint):

```csharp
    /// <summary>Size the fonts checklist to fill the viewport below its actual header (a fixed
    /// guess undershoots as the header wraps/grows) — still bounded, so it keeps virtualizing.</summary>
    private void SizeFontsList()
    {
        if (!FontsPanel.IsVisible) return;
        double top = FontsList.TranslatePoint(default, PrefsScroll)?.Y ?? 200;
        FontsList.Height = Math.Max(240, PrefsScroll.Bounds.Height - top - 22);
    }
```

  (`TranslatePoint` gives the list's top within the ScrollViewer INCLUDING scroll offset — with the
  fonts panel freshly shown the scroll is at 0; good enough, and clamped anyway.) Call sites:
  `PrefsScroll.SizeChanged += (_, _) => SizeFontsList();` (replacing the old lambda body) and in
  `RefreshFontChoices()` end: `Dispatcher.UIThread.Post(SizeFontsList, DispatcherPriority.Background);`
- [ ] Data & tools buttons: give all 8 (`OpenDataBtn`, `BackupFolderBtn`, `BackupClearBtn`,
  `BackupNowBtn`, `ExportBtn`, `ImportBtn`, `ResetBtn`, `RelockBtn`) the home-page button style:
  `Theme="{StaticResource LumenButton}" FontSize="12" Padding="12,5"` (smaller padding/font so they
  sit well inline; the theme brings the accent gradient, rounding, and press animation).
- [ ] Window sizes: MainWindow `Width="1320" Height="820"` (Min unchanged); PreferencesWindow
  `Width="920" Height="680"` (Min unchanged).
- [ ] Build + suite. Commit:
  `git commit -m "feat(pf2): fonts list truly fills, LumenButton data-tools buttons, larger default windows"`

### Task 3: Real blur on Lumen full-theme context menus

**Files:** `src/Lumenotepad/Platform/DwmAcrylic.cs`, `src/Lumenotepad/Views/MenuFx.cs` (new),
`src/Lumenotepad/Views/MainView.axaml.cs`, `src/Lumenotepad/Editor/NoteCanvas.cs`,
`src/Lumenotepad/Services/ThemePalettes.cs`

- [ ] `DwmAcrylic`: extract the HWND body into a public overload
  `public static void Apply(IntPtr hwnd, Backdrop backdrop = Backdrop.Acrylic, bool dark = true)`;
  the `Window` overload delegates to it (byte-identical behavior for existing callers).
- [ ] New `src/Lumenotepad/Views/MenuFx.cs`:

```csharp
using System;
using Avalonia.Controls;
using Lumenotepad.Services;

namespace Lumenotepad.Views;

/// <summary>Shared context-menu opening effects: the rise-in animation, and — when the active
/// theme's menu background is the translucent glass variant (alpha below 0xF0, i.e. Lumen with
/// Full theme on) — a real DWM acrylic backdrop on the menu's own popup window.</summary>
public static class MenuFx
{
    public static void Attach(ContextMenu menu)
    {
        menu.Opened += (_, _) =>
        {
            Motion.RiseIn(menu, Motion.Fast);
            TryBlur(menu);
        };
    }

    private static void TryBlur(ContextMenu menu)
    {
        try
        {
            var bg = ThemeManager.Current.MenuBackground;                 // "#AARRGGBB"
            if (bg.Length != 9 || Convert.ToInt32(bg.Substring(1, 2), 16) >= 0xF0) return;
            if (TopLevel.GetTopLevel(menu) is not { } tl) return;
            tl.TransparencyLevelHint = new[]
            {
                Avalonia.Controls.WindowTransparencyLevel.AcrylicBlur,
                Avalonia.Controls.WindowTransparencyLevel.Transparent,
            };
            if (tl.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero)
                Platform.DwmAcrylic.Apply(h, Platform.DwmAcrylic.Backdrop.Acrylic, dark: true);
        }
        catch { /* popups that reject the backdrop keep the translucent fallback */ }
    }
}
```

  (If `TransparencyLevelHint`'s type differs — it's `IReadOnlyList<WindowTransparencyLevel>` — adapt
  the collection expression only. If it's not settable on this TopLevel, drop that line and keep the
  DwmAcrylic call; report what you found.)
- [ ] Lumen full-ON `MenuBackground`: `#B814161C` → `#7014161C` in ThemePalettes (more translucent so
  the blur actually shows; full-off stays `#F5171922`).
- [ ] Replace the PF1 inline `menu.Opened += … RiseIn …` (+ acrylic gate) in `MainView.OpenMenu` with
  `MenuFx.Attach(menu);` (OpenMenu can go back to `static` if the Vm gate was its only instance need —
  check). Same swap for the grip menu in `NoteCanvas.cs` (`MenuFx.Attach(menu);` before `menu.Open`).
- [ ] Build + suite. Commit:
  `git commit -m "feat(pf2): real acrylic blur on Lumen full-theme context menus (popup HWND backdrop)"`

### Task 4: Final review + relaunch + checklist

- [ ] Single opus review over `git diff <pf2-plan-commit>..HEAD`: opaque values correct at every site
  (no translucent border left for the 4 themes; Lumen untouched), fonts sizing on panel-show +
  resize + font-count change, LumenButton on all 8 buttons w/o layout breakage, MenuFx gate logic
  (alpha parse, solid themes unaffected, no crash on non-Windows path), DwmAcrylic overload
  equivalence. Fix Important+ inline; suite green.
- [ ] Rebuild + relaunch; memory update; owner checklist:
  1. Dark/Light/Pink/Light blue: borders now a steady neutral tone — no wallpaper tint, softer.
  2. Fonts list reaches the window bottom (resize the window too).
  3. Data & tools buttons look like the home-page buttons.
  4. Both windows open larger.
  5. Lumen + Full theme: right-click menu shows REAL blur behind it; Full theme off: dark opaque;
     other themes: opaque frame-colored menus.
