using System.Collections.Generic;
using System.Text;

namespace Lumenotepad.Services;

public static class Slug
{

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
