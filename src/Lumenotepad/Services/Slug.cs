using System.Collections.Generic;
using System.Text;

namespace Lumenotepad.Services;

/// <summary>Turns a display name into a stable, filesystem-safe folder name.</summary>
public static class Slug
{
    /// <summary>Lowercase, alphanumerics kept, runs of anything else collapsed to single dashes,
    /// trimmed. Empty / symbol-only input falls back to "notebook".</summary>
    public static string Make(string name)
    {
        var sb = new StringBuilder();
        bool lastDash = false;
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); lastDash = false; }
            else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
        }
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "notebook" : s;
    }

    /// <summary>A slug not already present in <paramref name="existing"/>, appending -2, -3, … on collision.</summary>
    public static string Unique(string name, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing);
        var baseSlug = Make(name);
        if (!set.Contains(baseSlug)) return baseSlug;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!set.Contains(candidate)) return candidate;
        }
    }
}
