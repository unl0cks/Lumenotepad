using System;
using System.Reflection;

namespace Lumenotepad.Services;

/// <summary>The running build's version, read from the assembly (set by &lt;Version&gt; in the csproj).</summary>
public static class AppVersion
{
    /// <summary>e.g. "1.2.0". Never empty — falls back to "0.0.0" if the attribute is somehow missing.</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var asm = typeof(AppVersion).Assembly;
        // InformationalVersion is the exact string from <Version>; AssemblyVersion is padded to four
        // parts ("1.2.0.0"), which would never compare equal to a manifest saying "1.2.0".
        string? s = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // The SDK appends "+<commit sha>" when the build is in a git repo.
        if (s is { Length: > 0 }) return s.Split('+')[0];
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>Compare two dotted version strings. Returns &gt;0 when <paramref name="a"/> is newer.
    /// Unparseable parts count as 0, so a malformed manifest can never look like an upgrade.</summary>
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
