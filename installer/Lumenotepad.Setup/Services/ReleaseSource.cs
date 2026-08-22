using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Lumenotepad.Setup.Services;

public static class ReleaseSource
{
    public const string ManifestUrl = "https://github.com/unl0cks/Lumenotepad/releases/latest/download/latest.json";
    public const string PlatformKey = "win-x64";

    public sealed record Build(string Url, string Sha256, long Size);

    public sealed record Release(string Version, Build Client);

    public static Release? ParseManifest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (Find(root, "version")?.GetString() is not { Length: > 0 } version) return null;
            if (Find(root, "builds") is not { ValueKind: JsonValueKind.Object } builds) return null;
            if (Find(builds, PlatformKey) is not { ValueKind: JsonValueKind.Object } win) return null;

            string? url = Find(win, "url")?.GetString();
            string? sha = Find(win, "sha256")?.GetString();
            long size = Find(win, "size") is { ValueKind: JsonValueKind.Number } s && s.TryGetInt64(out long parsed)
                ? parsed : 0;

            if (url is not { Length: > 0 }) return null;
            if (sha is not { Length: 64 } || !sha.All(Uri.IsHexDigit)) return null;

            return new Release(NormaliseVersion(version), new Build(url, sha.ToLowerInvariant(), size));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Find(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }

    public static string NormaliseVersion(string version)
    {
        var trimmed = version.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? trimmed[1..] : trimmed;
    }

    public static bool IsNewer(string candidate, string installed)
    {
        var left = Components(candidate);
        var right = Components(installed);
        if (left.Length == 0) return false;
        if (right.Length == 0) return true;

        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            long a = i < left.Length ? left[i] : 0;
            long b = i < right.Length ? right[i] : 0;
            if (a != b) return a > b;
        }
        return false;
    }

    private static long[] Components(string version)
    {
        var normalised = NormaliseVersion(version);
        int dash = normalised.IndexOf('-');
        if (dash >= 0) normalised = normalised[..dash];

        var parts = new System.Collections.Generic.List<long>();
        foreach (var piece in normalised.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(piece, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) break;
            parts.Add(value);
        }
        return parts.ToArray();
    }

    public static bool HashMatches(string expected, string actual) =>
        string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}
