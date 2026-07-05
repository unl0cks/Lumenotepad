# Lumenotepad — M1 Foundation & Glass Shell — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the Lumenotepad Avalonia project with the Lumen-family frosted-glass window shell — custom chrome, DWM acrylic, rounded corners, resize + snap, entrance animation — plus a portable settings service and a test harness.

**Architecture:** A single Windows-first Avalonia 12 / .NET 10 app (`src/Lumenotepad`). The window is chromeless (`WindowDecorations="None"`) with a custom title bar; Windows 11 DWM provides rounded corners and an acrylic system backdrop, and translucent XAML panels sit over it for the glass look. Three chrome helpers are ported verbatim from `E:\CLAUDE\Lumen` (they depend only on Avalonia + Win32). Testable logic (settings I/O) is TDD'd in an xUnit project; visual/UI work is verified by building and running.

**Tech Stack:** Avalonia 12.0.4, .NET 10, CommunityToolkit.Mvvm, central package management, xUnit. Windows 11 DWM (`dwmapi.dll`) + Win32 (`user32.dll`) via P/Invoke.

---

## Milestone roadmap (context)

M1 **Foundation & glass shell** (this plan) → M2 Organization & storage → M3 Rich-text editor engine → M4 Content types & dockable toolbar → M5 PDF view+annotate → M6 Themes matrix → M7 Preferences + advanced gate → M8 Font installer → M9 Icon & polish. Each later milestone gets its own plan.

## Reference sources (port from these exact paths)

- `E:\CLAUDE\Lumen\src\Lumen\Controls\Squircle.cs`
- `E:\CLAUDE\Lumen\src\Lumen\Controls\WindowResizeBorder.cs`
- `E:\CLAUDE\Lumen\src\Lumen\Platform\WinChrome.cs`
- `E:\CLAUDE\Lumen\src\Lumen\Themes\Theme.axaml` (palette + control themes to adapt)

## File structure (created in M1)

```
Lumenotepad.slnx
Directory.Packages.props
.gitignore
src/Lumenotepad/
  Lumenotepad.csproj         WinExe, net10.0, app.manifest, ApplicationIcon
  app.manifest               DPI awareness + Win11 supportedOS
  Program.cs                 entry point, BuildAvaloniaApp
  App.axaml / App.axaml.cs   FluentTheme + merged Theme.axaml; opens MainWindow
  Themes/Theme.axaml         tokens (palette, fonts) + title-bar control themes
  Platform/WinChrome.cs      ported (rounded corners, native move-drag, snap)
  Platform/DwmAcrylic.cs      NEW: DWM system backdrop + immersive dark mode
  Controls/Squircle.cs       ported (squircle clip)
  Controls/WindowResizeBorder.cs  ported (edge/corner resize grips)
  Views/MainWindow.axaml(.cs)     chromeless glass window; applies chrome on open
  Views/MainView.axaml(.cs)       title bar + body placeholder proving glass
  Services/AppSettings.cs         NEW: portable JSON settings in userdata/
tests/Lumenotepad.Tests/
  Lumenotepad.Tests.csproj   xUnit, references the app project
  AppSettingsTests.cs        round-trip test
```

---

## Task 1: Solution + project scaffold

**Files:**
- Create: `Lumenotepad.slnx`, `Directory.Packages.props`, `.gitignore`, `src/Lumenotepad/Lumenotepad.csproj`, `src/Lumenotepad/app.manifest`, `src/Lumenotepad/Program.cs`, `src/Lumenotepad/App.axaml`, `src/Lumenotepad/App.axaml.cs`

- [ ] **Step 1: Initialize git**

Run (from `E:\CLAUDE\Lumenotepad`):
```bash
git init
```

- [ ] **Step 2: Create `.gitignore`**

```gitignore
bin/
obj/
userdata/
*.user
.vs/
```

- [ ] **Step 3: Create `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Avalonia" Version="12.0.4" />
    <PackageVersion Include="Avalonia.Desktop" Version="12.0.4" />
    <PackageVersion Include="Avalonia.Themes.Fluent" Version="12.0.4" />
    <PackageVersion Include="Avalonia.Fonts.Inter" Version="12.0.4" />
    <PackageVersion Include="Avalonia.Skia" Version="12.0.4" />
    <PackageVersion Include="AvaloniaUI.DiagnosticsSupport" Version="2.2.1" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageVersion Include="SkiaSharp" Version="3.119.4" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `Lumenotepad.slnx`**

```xml
<Solution>
  <Project Path="src/Lumenotepad/Lumenotepad.csproj" />
  <Project Path="tests/Lumenotepad.Tests/Lumenotepad.Tests.csproj" />
</Solution>
```

- [ ] **Step 5: Create `src/Lumenotepad/Lumenotepad.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <AvaloniaResource Include="Assets\**" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" />
    <PackageReference Include="Avalonia.Desktop" />
    <PackageReference Include="Avalonia.Themes.Fluent" />
    <PackageReference Include="Avalonia.Fonts.Inter" />
    <PackageReference Include="Avalonia.Skia" />
    <PackageReference Include="SkiaSharp" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="AvaloniaUI.DiagnosticsSupport">
      <IncludeAssets Condition="'$(Configuration)' != 'Debug'">None</IncludeAssets>
      <PrivateAssets Condition="'$(Configuration)' != 'Debug'">All</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create `src/Lumenotepad/app.manifest`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Lumenotepad.Desktop"/>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">permonitorv2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 7: Create `src/Lumenotepad/Program.cs`**

```csharp
using Avalonia;

namespace Lumenotepad;

sealed class Program
{
    [System.STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface },
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
```

- [ ] **Step 8: Create `src/Lumenotepad/App.axaml`**

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Lumenotepad.App"
             RequestedThemeVariant="Dark">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="/Themes/Theme.axaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
    <Application.Styles>
        <FluentTheme />
    </Application.Styles>
</Application>
```

- [ ] **Step 9: Create `src/Lumenotepad/App.axaml.cs`**

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lumenotepad.Views;

namespace Lumenotepad;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
```

> Note: Steps 8–9 reference `/Themes/Theme.axaml` (Task 2) and `MainWindow` (Task 6); the project will not build until those exist. Build verification is at the end of Task 6.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "chore: scaffold Lumenotepad Avalonia project"
```

---

## Task 2: Design tokens — `Themes/Theme.axaml`

Adapt the palette + title-bar control themes from `E:\CLAUDE\Lumen\src\Lumen\Themes\Theme.axaml`. This subset covers M1 (title bar); later milestones port more control themes.

**Files:**
- Create: `src/Lumenotepad/Themes/Theme.axaml`

- [ ] **Step 1: Create `src/Lumenotepad/Themes/Theme.axaml`**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:controls="using:Lumenotepad.Controls">

    <!-- Palette (Lumen family tokens) -->
    <Color x:Key="AccentColor">#4DA6FF</Color>
    <SolidColorBrush x:Key="AccentBrush" Color="#4DA6FF"/>
    <SolidColorBrush x:Key="AccentHoverBrush" Color="#73BAFF"/>
    <SolidColorBrush x:Key="AccentSoftBrush" Color="#384DA6FF"/>
    <SolidColorBrush x:Key="TextPrimaryBrush" Color="#FFFFFFFF"/>
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="#CCFFFFFF"/>
    <SolidColorBrush x:Key="TextMutedBrush" Color="#80FFFFFF"/>
    <SolidColorBrush x:Key="ControlHoverBrush" Color="#22FFFFFF"/>
    <SolidColorBrush x:Key="ControlPressedBrush" Color="#38FFFFFF"/>
    <SolidColorBrush x:Key="CloseHoverBrush" Color="#E81123"/>
    <SolidColorBrush x:Key="GlassBorderBrush" Color="#33FFFFFF"/>
    <SolidColorBrush x:Key="PanelTintBrush" Color="#8C16171C"/>

    <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
    <FontFamily x:Key="UiFont">Segoe UI Variable Text, Segoe UI</FontFamily>

    <!-- Round icon button (title-bar app mark, generic actions) -->
    <ControlTheme x:Key="IconButton" TargetType="Button">
        <Setter Property="Focusable" Value="False"/>
        <Setter Property="FontFamily" Value="{StaticResource IconFont}"/>
        <Setter Property="FontSize" Value="16"/>
        <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Width" Value="34"/>
        <Setter Property="Height" Value="34"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <ControlTemplate>
                <Panel>
                    <Border x:Name="bg" Background="{StaticResource ControlHoverBrush}" controls:Squircle.Enabled="True" Opacity="0">
                        <Border.Transitions><Transitions><DoubleTransition Property="Opacity" Duration="0:0:0.15"/></Transitions></Border.Transitions>
                    </Border>
                    <ContentPresenter x:Name="cp" Content="{TemplateBinding Content}"
                                      HorizontalAlignment="Center" VerticalAlignment="Center"
                                      FontFamily="{TemplateBinding FontFamily}" FontSize="{TemplateBinding FontSize}"
                                      Foreground="{TemplateBinding Foreground}" RenderTransformOrigin="50%,50%">
                        <ContentPresenter.Transitions><Transitions><TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.16"/></Transitions></ContentPresenter.Transitions>
                    </ContentPresenter>
                </Panel>
            </ControlTemplate>
        </Setter>
        <Style Selector="^:pointerover /template/ Border#bg"><Setter Property="Opacity" Value="1"/></Style>
        <Style Selector="^:pointerover /template/ ContentPresenter#cp"><Setter Property="RenderTransform" Value="scale(1.13)"/></Style>
        <Style Selector="^:pressed /template/ ContentPresenter#cp"><Setter Property="RenderTransform" Value="scale(0.88)"/></Style>
        <Style Selector="^:disabled"><Setter Property="Opacity" Value="0.35"/></Style>
    </ControlTheme>

    <!-- Caption (window) buttons -->
    <ControlTheme x:Key="CaptionButton" TargetType="Button">
        <Setter Property="Focusable" Value="False"/>
        <Setter Property="FontFamily" Value="{StaticResource IconFont}"/>
        <Setter Property="FontSize" Value="10"/>
        <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}"/>
        <Setter Property="Width" Value="44"/>
        <Setter Property="Height" Value="32"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <ControlTemplate>
                <Border x:Name="bg" Background="Transparent" controls:Squircle.Enabled="True">
                    <Border.Transitions><Transitions><BrushTransition Property="Background" Duration="0:0:0.15"/></Transitions></Border.Transitions>
                    <ContentPresenter x:Name="cp" Content="{TemplateBinding Content}" FontFamily="{TemplateBinding FontFamily}"
                                      FontSize="{TemplateBinding FontSize}" Foreground="{TemplateBinding Foreground}"
                                      HorizontalAlignment="Center" VerticalAlignment="Center" RenderTransformOrigin="50%,50%">
                        <ContentPresenter.Transitions><Transitions><TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.13"/></Transitions></ContentPresenter.Transitions>
                    </ContentPresenter>
                </Border>
            </ControlTemplate>
        </Setter>
        <Style Selector="^:pointerover /template/ Border#bg"><Setter Property="Background" Value="{StaticResource ControlHoverBrush}"/></Style>
        <Style Selector="^:pointerover"><Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}"/></Style>
        <Style Selector="^:pointerover /template/ ContentPresenter#cp"><Setter Property="RenderTransform" Value="scale(1.12)"/></Style>
        <Style Selector="^:pressed /template/ ContentPresenter#cp"><Setter Property="RenderTransform" Value="scale(0.84)"/></Style>
    </ControlTheme>

    <ControlTheme x:Key="CloseCaptionButton" TargetType="Button" BasedOn="{StaticResource CaptionButton}">
        <Style Selector="^:pointerover /template/ Border#bg"><Setter Property="Background" Value="{StaticResource CloseHoverBrush}"/></Style>
        <Style Selector="^:pointerover"><Setter Property="Foreground" Value="White"/></Style>
    </ControlTheme>
</ResourceDictionary>
```

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "feat: add Lumenotepad design tokens and title-bar control themes"
```

---

## Task 3: Port Squircle + WindowResizeBorder

**Files:**
- Create: `src/Lumenotepad/Controls/Squircle.cs`, `src/Lumenotepad/Controls/WindowResizeBorder.cs`

- [ ] **Step 1: Copy `Squircle.cs`**

Copy `E:\CLAUDE\Lumen\src\Lumen\Controls\Squircle.cs` to `src/Lumenotepad/Controls/Squircle.cs` verbatim, then change the namespace line from `namespace Lumen.Controls;` to `namespace Lumenotepad.Controls;`. No other edits.

- [ ] **Step 2: Copy `WindowResizeBorder.cs`**

Copy `E:\CLAUDE\Lumen\src\Lumen\Controls\WindowResizeBorder.cs` to `src/Lumenotepad/Controls/WindowResizeBorder.cs` verbatim, then change `namespace Lumen.Controls;` to `namespace Lumenotepad.Controls;`. No other edits.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: port Squircle and WindowResizeBorder from Lumen"
```

---

## Task 4: Port WinChrome

**Files:**
- Create: `src/Lumenotepad/Platform/WinChrome.cs`

- [ ] **Step 1: Copy `WinChrome.cs`**

Copy `E:\CLAUDE\Lumen\src\Lumen\Platform\WinChrome.cs` to `src/Lumenotepad/Platform/WinChrome.cs` verbatim, then change `namespace Lumen.Platform;` to `namespace Lumenotepad.Platform;`. No other edits. (It exposes `RoundCorners(Window,bool)`, `EnableSnap(Window)`, `BeginNativeMoveDrag(Window)`, and the static `CornerStyle` field.)

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "feat: port WinChrome (rounded corners, native move-drag, snap)"
```

---

## Task 5: DWM acrylic backdrop helper

**Files:**
- Create: `src/Lumenotepad/Platform/DwmAcrylic.cs`

- [ ] **Step 1: Create `src/Lumenotepad/Platform/DwmAcrylic.cs`**

```csharp
using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Lumenotepad.Platform;

/// <summary>Applies the Windows 11 DWM system backdrop (Mica / Acrylic) behind a chromeless window,
/// plus immersive dark mode. No-ops on non-Windows or pre-Win11 builds. Pair with a transparent
/// window Background and a tint overlay in XAML for the frosted-glass look.</summary>
public static class DwmAcrylic
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>DWM_SYSTEMBACKDROP_TYPE values.</summary>
    public enum Backdrop { None = 1, Mica = 2, Acrylic = 3, Tabbed = 4 }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window window, Backdrop backdrop = Backdrop.Acrylic, bool dark = true)
    {
        if (!OperatingSystem.IsWindows()) return;
        var h = window.TryGetPlatformHandle();
        if (h is null || h.Handle == IntPtr.Zero) return;
        try
        {
            int d = dark ? 1 : 0;
            DwmSetWindowAttribute(h.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));
            int b = (int)backdrop;
            DwmSetWindowAttribute(h.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref b, sizeof(int));
        }
        catch { /* pre-Win11: no system-backdrop API */ }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "feat: add DwmAcrylic system-backdrop helper"
```

---

## Task 6: Glass window shell (MainWindow + MainView)

**Files:**
- Create: `src/Lumenotepad/Views/MainWindow.axaml`, `src/Lumenotepad/Views/MainWindow.axaml.cs`, `src/Lumenotepad/Views/MainView.axaml`, `src/Lumenotepad/Views/MainView.axaml.cs`

- [ ] **Step 1: Create `src/Lumenotepad/Views/MainWindow.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="using:Lumenotepad.Views"
        xmlns:controls="using:Lumenotepad.Controls"
        x:Class="Lumenotepad.Views.MainWindow"
        Title="Lumenotepad"
        Width="1180" Height="720" MinWidth="720" MinHeight="460"
        WindowStartupLocation="CenterScreen"
        Background="Transparent"
        TransparencyLevelHint="Mica,AcrylicBlur,Transparent"
        FontFamily="{StaticResource UiFont}"
        Foreground="{StaticResource TextPrimaryBrush}"
        WindowDecorations="None">
    <Grid>
        <views:MainView x:Name="Host" Opacity="0" RenderTransformOrigin="50%,50%" RenderTransform="scale(0.985)">
            <views:MainView.Transitions>
                <Transitions>
                    <DoubleTransition Property="Opacity" Duration="0:0:0.22" Easing="CubicEaseOut"/>
                    <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.26" Easing="CubicEaseOut"/>
                </Transitions>
            </views:MainView.Transitions>
        </views:MainView>
        <controls:WindowResizeBorder/>
    </Grid>
</Window>
```

- [ ] **Step 2: Create `src/Lumenotepad/Views/MainWindow.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            WinChrome.EnableSnap(this);
            WinChrome.RoundCorners(this, true);
            DwmAcrylic.Apply(this, DwmAcrylic.Backdrop.Acrylic, dark: true);
            Host.Opacity = 1;
            Host.RenderTransform = null;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 3: Create `src/Lumenotepad/Views/MainView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Lumenotepad.Views.MainView"
             xmlns:controls="using:Lumenotepad.Controls">
    <Grid RowDefinitions="38,*">

        <!-- Title bar -->
        <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" x:Name="TitleBar" Background="#01FFFFFF">
            <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="11,0,0,0" Spacing="9">
                <Border Width="22" Height="22" CornerRadius="7" Background="{StaticResource AccentBrush}">
                    <TextBlock Text="&#xE82F;" FontFamily="{StaticResource IconFont}" FontSize="13"
                               Foreground="#04213F" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <TextBlock Text="Lumenotepad" FontSize="13.5" FontWeight="SemiBold" VerticalAlignment="Center"/>
            </StackPanel>

            <StackPanel Grid.Column="2" Orientation="Horizontal">
                <Button x:Name="MinBtn" Theme="{StaticResource CaptionButton}" Content="&#xE921;"/>
                <Button x:Name="MaxBtn" Theme="{StaticResource CaptionButton}" Content="&#xE922;"/>
                <Button x:Name="CloseBtn" Theme="{StaticResource CloseCaptionButton}" Content="&#xE8BB;"/>
            </StackPanel>
        </Grid>

        <!-- Body placeholder: a translucent glass panel proving the acrylic shows through -->
        <Border Grid.Row="1" Margin="14" CornerRadius="14"
                Background="#0FFFFFFF" BorderBrush="{StaticResource GlassBorderBrush}" BorderThickness="1">
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="8">
                <TextBlock Text="Lumenotepad" FontSize="26" FontWeight="SemiBold" HorizontalAlignment="Center"/>
                <TextBlock Text="Foundation shell — frosted glass working" FontSize="13"
                           Foreground="{StaticResource TextMutedBrush}" HorizontalAlignment="Center"/>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 4: Create `src/Lumenotepad/Views/MainView.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Lumenotepad.Platform;

namespace Lumenotepad.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();

        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                TopLevel.GetTopLevel(this) is Window w && !WinChrome.BeginNativeMoveDrag(w))
                w.BeginMoveDrag(e);
        };

        MinBtn.Click += (_, _) => { if (Window is { } w) w.WindowState = WindowState.Minimized; };
        MaxBtn.Click += (_, _) => { if (Window is { } w) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
        CloseBtn.Click += (_, _) => Window?.Close();
    }

    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: build succeeds with no errors.

- [ ] **Step 6: Run and verify (visual — user confirms on their machine)**

Run: `dotnet run --project src/Lumenotepad`
Expected: a centered chromeless window that (a) fades + scales in on launch, (b) shows the Windows 11 acrylic backdrop through the translucent body panel, (c) has rounded corners, (d) drags from the title bar and Aero-snaps, (e) resizes from every edge/corner, (f) minimize / maximize-restore / close buttons work with hover feedback.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: Lumen-family glass window shell (chrome, acrylic, resize, snap)"
```

---

## Task 7: Portable settings service (TDD spine)

Establishes the test harness and the portable `userdata/` convention. `AppSettings` has no Avalonia dependency, so it is unit-testable directly.

**Files:**
- Create: `tests/Lumenotepad.Tests/Lumenotepad.Tests.csproj`, `tests/Lumenotepad.Tests/AppSettingsTests.cs`, `src/Lumenotepad/Services/AppSettings.cs`

- [ ] **Step 1: Create `tests/Lumenotepad.Tests/Lumenotepad.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Lumenotepad\Lumenotepad.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing test — `tests/Lumenotepad.Tests/AppSettingsTests.cs`**

```csharp
using System.IO;
using Lumenotepad.Services;
using Xunit;

namespace Lumenotepad.Tests;

public class AppSettingsTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-test-" + Path.GetRandomFileName());
        try
        {
            var s = new AppSettings { Theme = "Lumen", FullTheme = true, AccentColor = "#4DA6FF", BlurStrength = 0.7 };
            s.Save(dir);

            var loaded = AppSettings.Load(dir);

            Assert.Equal("Lumen", loaded.Theme);
            Assert.True(loaded.FullTheme);
            Assert.Equal("#4DA6FF", loaded.AccentColor);
            Assert.Equal(0.7, loaded.BlurStrength, 3);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumenotepad-missing-" + Path.GetRandomFileName());
        var loaded = AppSettings.Load(dir);
        Assert.Equal("Lumen", loaded.Theme);
        Assert.False(loaded.FullTheme);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test`
Expected: FAIL — `AppSettings` does not exist / does not compile.

- [ ] **Step 4: Implement `src/Lumenotepad/Services/AppSettings.cs`**

```csharp
using System.IO;
using System.Text.Json;

namespace Lumenotepad.Services;

/// <summary>Portable app settings persisted as JSON in the beside-the-exe userdata folder.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Lumen";          // "Light" | "Dark" | "Lumen"
    public bool FullTheme { get; set; }                     // canvas matches frame material when true
    public string AccentColor { get; set; } = "#4DA6FF";
    public double BlurStrength { get; set; } = 0.6;         // 0..1

    private const string FileName = "settings.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(string userDataDir)
    {
        Directory.CreateDirectory(userDataDir);
        File.WriteAllText(Path.Combine(userDataDir, FileName), JsonSerializer.Serialize(this, Options));
    }

    public static AppSettings Load(string userDataDir)
    {
        var path = Path.Combine(userDataDir, FileName);
        if (!File.Exists(path)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    /// <summary>The portable userdata folder beside the running executable.</summary>
    public static string DefaultDir =>
        Path.Combine(Path.GetDirectoryName(System.Environment.ProcessPath) ?? ".", "userdata");
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test`
Expected: PASS — both tests green.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: portable AppSettings service with round-trip tests"
```

---

## Self-review (completed by plan author)

- **Spec coverage (M1 scope):** family stack + tokens (§2, §11) → Tasks 1–2; chrome + glass + rounded corners + resize/snap (§2, §11) → Tasks 3–6; portable `userdata/` settings foundation (§4) → Task 7. The theme *matrix* (§7), organization (§3), editor (§5), content types, PDF, prefs, fonts, and icon are intentionally deferred to M2–M9 and noted in the roadmap. `AppSettings` already carries `Theme` / `FullTheme` / `AccentColor` / `BlurStrength` so M6/M7 build on it.
- **Placeholder scan:** no TBD/TODO; every code step has complete file content, ports name an exact source path + the single edit, and every run step states its expected result.
- **Type consistency:** `WinChrome.RoundCorners/EnableSnap/BeginNativeMoveDrag`, `DwmAcrylic.Apply/Backdrop`, `Squircle.Enabled`, `AppSettings.Save(dir)/Load(dir)/DefaultDir` are used consistently across tasks and match the reference sources. `MainView` is named `Host` in `MainWindow.axaml` and the code-behind animates that element.
- **Known verify-at-runtime risk:** the exact combination of `TransparencyLevelHint` + `DwmAcrylic.Apply` that yields the best acrylic is confirmed by Task 6 Step 6 (visual). If the backdrop doesn't show, the fix is local to `MainWindow` (transparency hint) / `DwmAcrylic` (backdrop enum) and does not affect other tasks.
