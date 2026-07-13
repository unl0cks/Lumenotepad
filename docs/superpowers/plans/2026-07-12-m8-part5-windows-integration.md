# M8 Part 5 — Windows Integration (Tray + Global Hotkey)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close/minimize Lumenotepad to a system-tray icon (Open/Exit menu) and summon it from anywhere
with a global Ctrl+Alt+N hotkey.

**Architecture:** Three new bool settings (`CloseToTray`/`MinimizeToTray`/`SummonHotkey`) follow the
established AppSettings→VM guard-save pattern. All the OS glue lives in a new `MainWindow` partial
(`MainWindow.Integration.cs`): an Avalonia `TrayIcon` with a runtime-generated icon, hide/restore
plumbing hung off the existing `OnClosing`/`OnPropertyChanged(WindowState)` overrides, and the global
hotkey via `RegisterHotKey` + Avalonia's `Win32Properties.AddWndProcHookCallback` to catch `WM_HOTKEY`.

**Tech Stack:** Avalonia 12.0.4 (`TrayIcon`, `NativeMenu`, `WindowIcon`, `RenderTargetBitmap`,
`Win32Properties.AddWndProcHookCallback`), Win32 P/Invoke (`RegisterHotKey`/`UnregisterHotKey`), .NET 10, xUnit.

**Verified facts (do not re-derive — all confirmed against the built assemblies):**
- `Avalonia.Win32.Win32Properties.AddWndProcHookCallback(TopLevel tl, Win32Properties.CustomWndProcHookCallback cb)`
  compiles; the delegate is `IntPtr (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)`.
- `Window.TryGetPlatformHandle()?.Handle` gives the HWND (public API).
- `TrayIcon { Icon = WindowIcon, ToolTipText, IsVisible, Menu = NativeMenu }`, `TrayIcon.Clicked` event,
  `TrayIcon.Dispose()`; `NativeMenu.Add(NativeMenuItem)`, `NativeMenuItem(string)` + `.Click` event.
- `RenderTargetBitmap.Render(Visual)` then `.Save(Stream)` works (the drag-ghost uses `rtb.Render`);
  `new WindowIcon(Stream)` builds the icon. `Services.ThemeManager.Current.Accent` is the accent hex.
- `MainWindow` already overrides `OnClosing` (with the `_closingAnimated` re-entrancy guard),
  `OnOpened`, and `OnPropertyChanged` (handles `WindowStateProperty`), and hooks the VM via
  `OnThemePropertyChanged`. It is `public partial class MainWindow : Window`.
- The app's `App.axaml.cs` uses the default `ShutdownMode` (OnLastWindowClose). `Window.Hide()` keeps
  the window in the lifetime's window list, so hiding to tray does NOT shut the app down; only a real
  `Close()` (or `desktop.Shutdown()`) exits. The tray "Exit" uses `desktop.Shutdown()`.
- Build gotcha: `taskkill //F //IM Lumenotepad.exe 2>/dev/null; true` before every build/test.
  `cd /e/CLAUDE/Lumenotepad` in every Bash call. Never launch the GUI from a subagent. Tray + hotkey
  behavior is pointer/OS-level → owner-verified (only the settings are unit-tested).

---

### Task 1: Tray/hotkey settings + VM (TDD)

**Files:**
- Modify: `src/Lumenotepad/Services/AppSettings.cs`
- Modify: `src/Lumenotepad/ViewModels/MainViewModel.cs`
- Test: `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `tests/Lumenotepad.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `tests/Lumenotepad.Tests/AppSettingsTests.cs` (inside the class):

```csharp
    [Fact]
    public void TraySettings_defaultsFalse_andRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            Assert.False(new AppSettings().CloseToTray);
            Assert.False(new AppSettings().MinimizeToTray);
            Assert.False(new AppSettings().SummonHotkey);

            var s = new AppSettings { CloseToTray = true, MinimizeToTray = true, SummonHotkey = true };
            s.Save(dir);
            var loaded = AppSettings.Load(dir);

            Assert.True(loaded.CloseToTray);
            Assert.True(loaded.MinimizeToTray);
            Assert.True(loaded.SummonHotkey);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
```

Append to `tests/Lumenotepad.Tests/MainViewModelTests.cs` (inside the class):

```csharp
    [Fact]
    public void ResetSettingsToDefaults_restoresTrayPrefs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lnp-vm-" + Path.GetRandomFileName());
        try
        {
            var vm = new MainViewModel(new WorkspaceStore(dir), dir);
            vm.CloseToTray = true;
            vm.MinimizeToTray = true;
            vm.SummonHotkey = true;

            vm.ResetSettingsToDefaults();

            Assert.False(vm.CloseToTray);
            Assert.False(vm.MinimizeToTray);
            Assert.False(vm.SummonHotkey);
            var persisted = AppSettings.Load(dir);
            Assert.False(persisted.CloseToTray);
            Assert.False(persisted.MinimizeToTray);
            Assert.False(persisted.SummonHotkey);
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run — verify fail**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test --filter "TraySettings_defaultsFalse_andRoundTrip|ResetSettingsToDefaults_restoresTrayPrefs" 2>&1 | tail -12`
Expected: compile errors (`CloseToTray` etc. don't exist).

- [ ] **Step 3: Implement**

In `src/Lumenotepad/Services/AppSettings.cs`, after the `LastBackupUtc` line (added in Part 4), add:

```csharp
    public bool CloseToTray { get; set; }                   // closing hides to the tray instead of quitting
    public bool MinimizeToTray { get; set; }                // minimizing hides to the tray
    public bool SummonHotkey { get; set; }                  // global Ctrl+Alt+N brings the window forward
```

In `src/Lumenotepad/ViewModels/MainViewModel.cs`:

1. After the `_backupKeep` field (Part 4), add:

```csharp
    [ObservableProperty] private bool _closeToTray;        // prefs: close hides to tray
    [ObservableProperty] private bool _minimizeToTray;     // prefs: minimize hides to tray
    [ObservableProperty] private bool _summonHotkey;       // prefs: global Ctrl+Alt+N
```

2. In the ctor settings-load block, after `BackupKeep = _settings.BackupKeep;`, add:

```csharp
            CloseToTray = _settings.CloseToTray;
            MinimizeToTray = _settings.MinimizeToTray;
            SummonHotkey = _settings.SummonHotkey;
```

3. After `OnBackupKeepChanged` (Part 4), add the three save hooks:

```csharp
    partial void OnCloseToTrayChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.CloseToTray = value;
        _settings.Save(_settingsDir);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.MinimizeToTray = value;
        _settings.Save(_settingsDir);
    }

    partial void OnSummonHotkeyChanged(bool value)
    {
        if (_settings is null || _settingsDir is null) return;
        _settings.SummonHotkey = value;
        _settings.Save(_settingsDir);
    }
```

4. In `ResetSettingsToDefaults`, after `BackupFolder = d.BackupFolder; BackupEveryDays = d.BackupEveryDays; BackupKeep = d.BackupKeep;` (Part 4), add:

```csharp
        CloseToTray = d.CloseToTray; MinimizeToTray = d.MinimizeToTray; SummonHotkey = d.SummonHotkey;
```

- [ ] **Step 4: Run — verify green (full suite)**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet test 2>&1 | tail -5`
Expected: 0 failures; the total rises by 2 (137 → 139).

- [ ] **Step 5: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): tray + summon-hotkey settings/VM"
```

---

### Task 2: Tray icon + hide/restore in MainWindow

**Files:**
- Create: `src/Lumenotepad/Views/MainWindow.Integration.cs`
- Modify: `src/Lumenotepad/Views/MainWindow.axaml.cs`

No new unit tests (OS/pointer behavior — owner-verified); the suite must stay green.

- [ ] **Step 1: Create the integration partial (tray only for this task; hotkey members land in Task 3)**

Create `src/Lumenotepad/Views/MainWindow.Integration.cs`:

```csharp
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lumenotepad.ViewModels;

namespace Lumenotepad.Views;

/// <summary>System-tray integration: a generated tray icon with Open/Exit, plus close-to-tray and
/// minimize-to-tray hide/restore. (The global summon hotkey is added in a second step.)</summary>
public partial class MainWindow
{
    private TrayIcon? _tray;
    private bool _exiting;      // set by the tray Exit path so OnClosing lets the real close through

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Create the tray icon on demand (whenever a tray feature is on).</summary>
    private void EnsureTray()
    {
        if (_tray is not null) return;
        var open = new NativeMenuItem("Open Lumenotepad");
        open.Click += (_, _) => RestoreFromTray();
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        var menu = new NativeMenu();
        menu.Add(open);
        menu.Add(exit);
        _tray = new TrayIcon
        {
            Icon = BuildTrayIcon(), ToolTipText = "Lumenotepad", IsVisible = true, Menu = menu,
        };
        _tray.Clicked += (_, _) => RestoreFromTray();     // left-click opens
    }

    private void DisposeTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    /// <summary>Show the tray icon while either tray feature is on; remove it otherwise.</summary>
    private void SyncTrayEnabled()
    {
        if (Vm is { } vm && (vm.CloseToTray || vm.MinimizeToTray)) EnsureTray();
        else DisposeTray();
    }

    /// <summary>Hide the window into the tray (making sure the icon exists first). The close path
    /// animates the shrink; the minimize path is already visually gone, so it just hides.</summary>
    private void HideToTray(bool animate)
    {
        EnsureTray();
        if (animate) Motion.CollapseOut(Host, 150, Hide);
        else Hide();
    }

    /// <summary>Bring the window back from the tray: show, un-minimize, focus, and re-scale it in.</summary>
    public void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Motion.ScaleIn(Host, 0.97, 220);
        ReassertChrome();
    }

    /// <summary>Real quit (tray Exit): flush, drop the icon, and shut the app down. `_exiting` makes
    /// OnClosing skip both the close-to-tray hide and the close animation.</summary>
    private void ExitApp()
    {
        _exiting = true;
        Vm?.FlushDirtyDocs();
        DisposeTray();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
        else Close();
    }

    /// <summary>A simple placeholder tray icon (accent rounded square + "L") rendered to a bitmap —
    /// stands in until the real app icon ships. Uses the proven RenderTargetBitmap.Render path.</summary>
    private static WindowIcon BuildTrayIcon()
    {
        var accent = Color.Parse(Services.ThemeManager.Current.Accent);
        var visual = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(accent),
            Child = new TextBlock
            {
                Text = "L", FontSize = 40, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        visual.Measure(new Size(64, 64));
        visual.Arrange(new Rect(0, 0, 64, 64));
        var rtb = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        rtb.Render(visual);
        using var ms = new MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }
}
```

- [ ] **Step 2: Wire the hooks into MainWindow.axaml.cs**

In `src/Lumenotepad/Views/MainWindow.axaml.cs`:

(a) `OnClosing` currently is:

```csharp
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as ViewModels.MainViewModel)?.FlushDirtyDocs();   // never lose the last keystrokes
        if (_closingAnimated) return;                                  // second pass: let the close through
        e.Cancel = true;
        _closingAnimated = true;
        Motion.CollapseOut(Host, 150, Close);                          // quick fade + shrink, then close
    }
```

Replace it with:

```csharp
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as ViewModels.MainViewModel)?.FlushDirtyDocs();   // never lose the last keystrokes
        if (_exiting) return;                                          // tray Exit: close immediately
        if (Vm is { CloseToTray: true })                               // close hides to the tray instead
        {
            e.Cancel = true;
            HideToTray(animate: true);
            return;
        }
        if (_closingAnimated) return;                                  // second pass: let the close through
        e.Cancel = true;
        _closingAnimated = true;
        Motion.CollapseOut(Host, 150, Close);                          // quick fade + shrink, then close
    }
```

(b) Add an `OnClosed` override (right after `OnClosing`) so the tray icon + hotkey are cleaned up on a
real close:

```csharp
    protected override void OnClosed(EventArgs e)
    {
        DisposeTray();
        UnregisterSummon();      // no-op until Task 3 registers it
        base.OnClosed(e);
    }
```

(c) In `OnOpened`, after the existing `Motion.ScaleIn(Host, 0.97, 220);` line, add:

```csharp
        SyncTrayEnabled();
        SyncHotkey();            // no-op until Task 3; safe to call now
```

(d) In `OnPropertyChanged`, the `WindowStateProperty` branch computes `var state = change.GetNewValue<WindowState>();`.
Immediately AFTER that line, add the minimize-to-tray shortcut:

```csharp
        if (state == WindowState.Minimized && Vm is { MinimizeToTray: true })
        {
            HideToTray(animate: false);
            return;               // hidden — skip the maximize-margin / chrome bookkeeping
        }
```

(e) In `OnThemePropertyChanged`, after the existing `AlwaysOnTop` handling block, add:

```csharp
        if (e.PropertyName is nameof(ViewModels.MainViewModel.CloseToTray)
            or nameof(ViewModels.MainViewModel.MinimizeToTray))
            SyncTrayEnabled();
        if (e.PropertyName == nameof(ViewModels.MainViewModel.SummonHotkey))
            SyncHotkey();         // no-op until Task 3
```

NOTE: `SyncHotkey()` and `UnregisterSummon()` are referenced here but DEFINED in Task 3. To keep this
task building on its own, add temporary no-op stubs to `MainWindow.Integration.cs` now and REPLACE them
with the real implementations in Task 3:

```csharp
    private void SyncHotkey() { }
    private void UnregisterSummon() { }
```

- [ ] **Step 3: Build + suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds (0 errors), 139/139 pass.

- [ ] **Step 4: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): system-tray icon + close/minimize-to-tray hide & restore"
```

---

### Task 3: Global summon hotkey (Ctrl+Alt+N)

**Files:**
- Modify: `src/Lumenotepad/Views/MainWindow.Integration.cs`

- [ ] **Step 1: Replace the stubs with the real hotkey implementation**

In `src/Lumenotepad/Views/MainWindow.Integration.cs`, add these usings at the top:

```csharp
using System;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Avalonia.Win32;
```

Then REMOVE the two stub methods `private void SyncHotkey() { }` and
`private void UnregisterSummon() { }` and add the full implementation (e.g. at the end of the class):

```csharp
    // ---- global summon hotkey: Ctrl+Alt+N brings the window forward from anywhere ----

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const uint VK_N = 0x4E;
    private const uint WM_HOTKEY = 0x0312;
    private const int SummonHotkeyId = 0x4C4E;      // arbitrary, app-unique

    private bool _hotkeyRegistered;
    private Win32Properties.CustomWndProcHookCallback? _wndHook;

    /// <summary>Install the WndProc hook once (kept for the window's life); it only reacts to our
    /// hotkey id, so it is inert until <see cref="RegisterHotKey"/> has run.</summary>
    private void InstallWndHook()
    {
        if (_wndHook is not null) return;
        _wndHook = (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == SummonHotkeyId)
            {
                handled = true;
                Dispatcher.UIThread.Post(RestoreFromTray);
            }
            return IntPtr.Zero;
        };
        Win32Properties.AddWndProcHookCallback(this, _wndHook);
    }

    /// <summary>Register or unregister the global hotkey to match the pref.</summary>
    private void SyncHotkey()
    {
        if (Vm is { SummonHotkey: true }) RegisterSummon();
        else UnregisterSummon();
    }

    private void RegisterSummon()
    {
        if (_hotkeyRegistered) return;
        if (TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;   // handle not ready yet
        InstallWndHook();
        _hotkeyRegistered = RegisterHotKey(hwnd, SummonHotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_N);
    }

    private void UnregisterSummon()
    {
        if (!_hotkeyRegistered) return;
        if (TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
            UnregisterHotKey(hwnd, SummonHotkeyId);
        _hotkeyRegistered = false;
    }
```

(`SyncHotkey` is already called from `OnOpened` — where the platform handle exists — and from
`OnThemePropertyChanged` when the pref flips; `UnregisterSummon` from `OnClosed`. No other wiring.)

- [ ] **Step 2: Build + suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds (0 errors), 139/139 pass.

- [ ] **Step 3: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): global Ctrl+Alt+N summon hotkey via RegisterHotKey + WndProc hook"
```

---

### Task 4: Preferences — SYSTEM section (General)

**Files:**
- Modify: `src/Lumenotepad/Views/PreferencesWindow.axaml` (GeneralPanel)

- [ ] **Step 1: Add the toggles**

In `src/Lumenotepad/Views/PreferencesWindow.axaml`, inside `GeneralPanel`, AFTER the STARTUP section
(after the "Always on top" Grid — the one whose ToggleSwitch binds `AlwaysOnTop`) and BEFORE the
`<TextBlock Classes="section" Text="SAVING"/>` line, insert:

```xml
                        <TextBlock Classes="section" Text="SYSTEM TRAY"/>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Close to tray"/>
                                <TextBlock Classes="hint" Text="Closing the window hides it to the system tray instead of quitting."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding CloseToTray, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Minimize to tray"/>
                                <TextBlock Classes="hint" Text="Minimizing hides to the tray instead of the taskbar."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding MinimizeToTray, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,Auto">
                            <StackPanel Spacing="2" VerticalAlignment="Center">
                                <TextBlock Classes="label" Text="Global shortcut (Ctrl+Alt+N)"/>
                                <TextBlock Classes="hint" Text="Bring Lumenotepad to the front from anywhere, even from the tray."/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="1" IsChecked="{Binding SummonHotkey, Mode=TwoWay}" VerticalAlignment="Center"/>
                        </Grid>
```

(All three are plain TwoWay bool bindings — no code-behind sync is needed, matching the existing
`AlwaysOnTop`/`ShowHomeStats` toggles. The MainWindow reacts to the VM changes via
`OnThemePropertyChanged`.)

- [ ] **Step 2: Build + suite green**

Run: `cd /e/CLAUDE/Lumenotepad && taskkill //F //IM Lumenotepad.exe 2>/dev/null; dotnet build 2>&1 | tail -3 && dotnet test 2>&1 | tail -3`
Expected: build succeeds, 139/139 pass.

- [ ] **Step 3: Commit**

```bash
cd /e/CLAUDE/Lumenotepad && git add -A && git commit -m "feat(m8): System tray prefs — close/minimize to tray + global shortcut toggles"
```

---

### Task 5: Final integration review + relaunch + checklist

- [ ] Dispatch a final integration reviewer (opus) over the Part 5 diff (the 4 feature commits after the
  plan commit), against this plan + the M8 spec Part 5. Focus on the seams: the `OnClosing` branch order
  (`_exiting` → CloseToTray → animated close), that hiding to tray doesn't shut the app down, that the
  tray icon is disposed on real exit AND that `desktop.Shutdown()` from tray Exit exits cleanly (no
  cancelled-close deadlock), the WndProc hook + hotkey register/unregister lifecycle (handle readiness,
  double-register guard, cleanup on close), and that `SyncTrayEnabled`/`SyncHotkey` fire on the right VM
  changes. Confirm `RestoreFromTray` works from BOTH a hidden (closed-to-tray) and a minimized state.
- [ ] Fix anything Important+ inline; re-run the suite.
- [ ] Rebuild + relaunch the app for the owner.
- [ ] Update memory (`lumenotepad.md`) with the Part 5 entry.
- [ ] Hand the owner the verification checklist:
  1. Prefs → General → SYSTEM TRAY → turn on **Close to tray**. A tray icon appears. Click the window's
     ✕ → the window vanishes to the tray (not quit). Left-click the tray icon (or right-click → Open) →
     it scales back in. Right-click → **Exit** → the app really quits (icon gone).
  2. Turn on **Minimize to tray** → minimize the window → it hides to the tray; restore via the icon.
  3. Turn on **Global shortcut** → click another app to give it focus → press **Ctrl+Alt+N** → Lumenotepad
     jumps to the front (works whether it was in the tray, minimized, or just behind another window).
     Toggle the shortcut off → Ctrl+Alt+N no longer summons it.
  4. With all three off, closing quits normally and there's no tray icon.
  5. Reset settings turns all three back off.
