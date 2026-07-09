# Motion System — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or subagent-driven-development to implement task-by-task. Steps use `- [ ]` checkboxes.

**Goal:** Build the shared `Motion` foundation and apply it to the highest-impact transitions — home↔editor, opening a notebook, and switching sections/pages — so views cross-fade/rise in instead of popping.

**Architecture:** One code-behind tween engine (`Motion`, static) drives all transform + opacity motion per the spike (transforms MUST be code-behind; opacity may be declarative). MainView's existing drag/hover/selection tweens are refactored onto `Motion` so there is a single engine. View/content swaps use `Motion.RiseIn`/`FadeIn` triggered from the existing VM `PropertyChanged` hooks in `MainView.axaml.cs`.

**Tech Stack:** Avalonia 12, C#, xUnit. Reference: `docs/superpowers/specs/2026-07-09-motion-system-design.md`.

---

### Task 1: `Motion` foundation (engine + tokens + methods)

**Files:**
- Create: `src/Lumenotepad/Views/Motion.cs`
- Test: `tests/Lumenotepad.Tests/MotionTests.cs`

- [ ] **Step 1: Write failing tests for the pure math**

```csharp
using Avalonia;
using Lumenotepad.Views;
using Xunit;

public class MotionTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.875)]   // 1-(1-0.5)^3
    public void EaseOut_matches_cubic(double t, double expected)
        => Assert.Equal(expected, Motion.EaseOut(t), 3);

    [Fact]
    public void Lerp_interpolates_endpoints()
    {
        Assert.Equal(10, Motion.Lerp(10, 20, 0), 3);
        Assert.Equal(20, Motion.Lerp(10, 20, 1), 3);
        Assert.Equal(15, Motion.Lerp(10, 20, 0.5), 3);
    }

    [Fact]
    public void Steps_never_zero()  // a 1ms animation still runs at least one frame
        => Assert.True(Motion.Steps(1) >= 1);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Lumenotepad.Tests/Lumenotepad.Tests.csproj --filter MotionTests`
Expected: FAIL (Motion does not exist).

- [ ] **Step 3: Implement `Motion`**

```csharp
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Lumenotepad.Views;

/// <summary>The app's single motion engine. Transforms (scale/translate) MUST be driven here as
/// LOCAL values tweened per-frame — this build's RenderTransform Transitions/Animation are dead
/// (see the motion spec). Opacity is tweened in the same loop. One tween per element; a new tween
/// on the same element cancels the old.</summary>
public static class Motion
{
    public const int Fast = 120, Base = 190, Slow = 280;
    public const double Rise = 8;

    public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    public static double EaseIn(double t) => t * t * t;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static int Steps(int ms) => Math.Max(1, ms / 15);

    private static readonly Dictionary<Visual, DispatcherTimer> Tweens = new();

    public static void Stop(Visual v)
    {
        if (Tweens.TryGetValue(v, out var t)) { t.Stop(); Tweens.Remove(v); }
    }

    private static ITransform Make(double tx, double ty, double s)
    {
        var b = TransformOperations.CreateBuilder(2);
        b.AppendTranslate(tx, ty);
        b.AppendScale(s, s);
        return b.Build();
    }

    /// <summary>Tween translate+scale (and optionally opacity) from a start to a target. At rest at
    /// identity the RenderTransform is cleared. onDone always fires at the end.</summary>
    public static void Tween(Control c, double fx, double fy, double fs, double tx, double ty, double ts,
                             int ms, Func<double, double>? ease = null, double? fromOpacity = null,
                             double? toOpacity = null, Action? onDone = null)
    {
        Stop(c);
        c.Transitions = null;
        ease ??= EaseOut;
        int step = 0, steps = Steps(ms);
        void Frame(double e)
        {
            c.RenderTransform = Make(Lerp(fx, tx, e), Lerp(fy, ty, e), Lerp(fs, ts, e));
            if (fromOpacity is double o0 && toOpacity is double o1) c.Opacity = Lerp(o0, o1, e);
        }
        Frame(0);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            step++;
            Frame(ease(Math.Min(1.0, step / (double)steps)));
            if (step >= steps)
            {
                Stop(c);
                bool rest = Math.Abs(ts - 1) < 1e-3 && Math.Abs(tx) < 1e-3 && Math.Abs(ty) < 1e-3;
                if (rest) { c.ClearValue(Visual.RenderTransformProperty); c.ClearValue(Animatable.TransitionsProperty); }
                if (toOpacity is double t1) c.Opacity = t1;
                onDone?.Invoke();
            }
        };
        Tweens[c] = timer;
        timer.Start();
    }

    public static void FadeIn(Control c, int ms = Base)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, 0, 1, 0, 0, 1, ms, EaseOut, 0, 1); }

    public static void RiseIn(Control c, int ms = Base)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, Rise, 1, 0, 0, 1, ms, EaseOut, 0, 1); }

    public static void ScaleIn(Control c, double from = 0.96, int ms = Base)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, 0, from, 0, 0, 1, ms, EaseOut, 0, 1); }

    public static void FadeOut(Control c, int ms = Base, Action? onDone = null)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, 0, 1, 0, 0, 1, ms, EaseIn, c.Opacity, 0, onDone); }

    public static void CollapseOut(Control c, int ms = Base, Action? onDone = null)
    { c.RenderTransformOrigin = RelativePoint.Center; Tween(c, 0, 0, 1, 0, 0, 0.92, ms, EaseIn, c.Opacity, 0, onDone); }
}
```

- [ ] **Step 4: Run tests to verify pass**

Run: `dotnet test tests/Lumenotepad.Tests/Lumenotepad.Tests.csproj --filter MotionTests`
Expected: PASS (3 tests / 5 cases).

- [ ] **Step 5: Commit**

```bash
git add src/Lumenotepad/Views/Motion.cs tests/Lumenotepad.Tests/MotionTests.cs
git commit -m "feat(m6.9): Motion engine — shared tween (opacity+transform), tokens, enter/exit helpers"
```

---

### Task 2: Refactor MainView drag/hover/selection onto `Motion`

Removes the duplicate engine so there is one. Behaviour must be unchanged.

**Files:**
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`

- [ ] **Step 1:** Delete the local `Tween`, `Make`, `ScaleNow`, `StopTween`, and `_tweens` field from `MainView.axaml.cs`. Add `private static double ScaleNow(Visual b) => b.RenderTransform?.Value is { } m && m.M11 > 0 ? m.M11 : 1;` back as a local helper (still used).
- [ ] **Step 2:** Replace every `Tween(x, fx,fy,fs, tx,ty,ts, ms, onDone: cb)` call with `Motion.Tween(x, fx,fy,fs, tx,ty,ts, ms, onDone: cb)`. Replace `StopTween(x)` with `Motion.Stop(x)`. (Call sites: `SetHoverCard`, `SetHoverChip`, `ScaleSelect`, `OnRearrangeMoved` reflow, `PrimeGrabbed` removed already, `OnRearrangeReleased`/ghost path uses `_ghostTween` — leave the ghost's own timer as-is.)
- [ ] **Step 3: Build + tests**

Run: `dotnet build src/Lumenotepad/Lumenotepad.csproj -c Debug -v q` then `dotnet test`
Expected: 0 errors; 67 tests pass.

- [ ] **Step 4: Verify unchanged in the real app**

Kill + relaunch; confirm hover scale, selection scale, and drag still work exactly as before.

- [ ] **Step 5: Commit**

```bash
git add src/Lumenotepad/Views/MainView.axaml.cs
git commit -m "refactor(m6.9): MainView uses the shared Motion engine (single tween)"
```

---

### Task 3: Home ↔ editor + open-notebook transition

When `IsHomeVisible` flips, the newly-shown surface rises/fades in (Slow) instead of popping.

**Files:**
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs` (the `OnVmPropertyChanged` handler for `IsHomeVisible`)

- [ ] **Step 1:** In `OnVmPropertyChanged`, when `e.PropertyName == nameof(MainViewModel.IsHomeVisible)`, after the existing logic, post (Background) `Motion.RiseIn(target, Motion.Slow)` where `target` is `HomeHost` if `Vm.IsHomeVisible` else `BodyDock`. Guard nulls. Keep the existing rearrange-exit + card-re-realize logic.

```csharp
else if (e.PropertyName == nameof(MainViewModel.IsHomeVisible))
{
    if (_rearranging) SetRearranging(false);
    if (Vm is { IsHomeVisible: true } vm) { HomeCards.ItemsSource = null; HomeCards.ItemsSource = vm.Notebooks; }
    var surface = (Vm?.IsHomeVisible ?? true) ? (Control)HomeHost : BodyDock;
    Dispatcher.UIThread.Post(() => Motion.RiseIn(surface, Motion.Slow), DispatcherPriority.Background);
}
```
(Consolidate the two prior `IsHomeVisible` branches into this one.)

- [ ] **Step 2: Build + launch.** Open a notebook, hit Home — the editor and gallery should fade+rise in, not pop.
- [ ] **Step 3: Commit**

```bash
git commit -am "feat(m6.9): cross-fade/rise home<->editor + opening a notebook"
```

---

### Task 4: Section-switch content transition

Switching the selected section repopulates the pages list + page content; rise it in.

**Files:**
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs`

- [ ] **Step 1:** In `OnVmPropertyChanged`, add a branch: when `e.PropertyName == nameof(MainViewModel.SelectedSection)`, post (Background) `Motion.RiseIn(PagesList, Motion.Base)`. (Selection re-assert already runs; this only adds the animation.)
- [ ] **Step 2: Build + launch.** Switch sections — the pages list rises in.
- [ ] **Step 3: Commit** `git commit -am "feat(m6.9): rise-in pages list on section switch"`

---

### Task 5: Page-switch content transition

Selecting a page swaps the canvas via `SyncEditorDocument`; rise the page box in so blank→content stops popping.

**Files:**
- Modify: `src/Lumenotepad/Views/MainView.axaml.cs` (`SyncEditorDocument`)

- [ ] **Step 1:** At the end of `SyncEditorDocument`, when a document was set, `Motion.RiseIn(PageDock, Motion.Base)` (the page title + canvas host). Guard: only when `PageCanvas.Document is not null`.
- [ ] **Step 2: Build + launch.** Switch pages — the page content rises in instead of popping.
- [ ] **Step 3: Commit** `git commit -am "feat(m6.9): rise-in page content on page switch"`

---

## Self-Review

- **Spec coverage (Phase 1):** foundation (Task 1) ✓; home↔editor + open notebook (Task 3) ✓; section switch (Task 4) ✓; page switch (Task 5) ✓; single engine (Task 2) ✓.
- **Placeholders:** none — Motion.cs is complete; each view task shows the exact hook + call.
- **Type consistency:** `Motion.Tween` signature is used identically in Task 2's refactor; `RiseIn/FadeIn/Slow/Base` names match across tasks.
- **Deferred to later phases (not gaps):** panels, add/delete/rename, buttons, dialogs, theme — Phases 2–4.
- **Note:** view transitions (Tasks 3–5) are verified by build + launch + observe, not unit tests (visual; headless clock unreliable).
