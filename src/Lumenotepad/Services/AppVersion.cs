using System;
using System.Reflection;

namespace Lumenotepad.Services;

public static class AppVersion
{

    public static string Current { get; } = Read();

    private static string Read()
    {
        var asm = typeof(AppVersion).Assembly;

        string? s = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (s is { Length: > 0 }) return s.Split('+')[0];
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public static int Compare(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int va = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
            int vb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }

    public static bool IsNewerThanCurrent(string candidate) => Compare(candidate, Current) > 0;
}
