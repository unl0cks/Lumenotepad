using System;
using System.IO;
using Microsoft.Win32;

namespace Lumenotepad.Platform;

/// <summary>The "Start with Windows" toggle. The REGISTRY is the source of truth (no settings
/// field) so the switch always shows reality even if the user edited it elsewhere. The Run value
/// points at the exe beside our assemblies — Environment.ProcessPath is dotnet.exe under
/// `dotnet App.dll` and must not be used.</summary>
public static class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumenotepad";

    private static string ExePath => Path.Combine(AppContext.BaseDirectory, "Lumenotepad.exe");

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(ValueName, $"\"{ExePath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* non-elevated registry writes can still fail under policy — fail quiet */ }
    }
}
