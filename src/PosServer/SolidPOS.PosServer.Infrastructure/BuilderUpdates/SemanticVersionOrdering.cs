namespace SolidPOS.PosServer.Infrastructure.BuilderUpdates;

internal static class SemanticVersionOrdering
{
    public static bool IsStrictlyNewer(string candidate, string current)
    {
        return TryParse(candidate, out ParsedVersion? candidateVersion)
            && TryParse(current, out ParsedVersion? currentVersion)
            && Compare(candidateVersion!, currentVersion!) > 0;
    }

    private static bool TryParse(string value, out ParsedVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Trim();
        int plus = normalized.IndexOf('+');
        if (plus >= 0) normalized = normalized[..plus];
        string[] split = normalized.Split('-', 2, StringSplitOptions.None);
        string[] coreParts = split[0].Split('.', StringSplitOptions.None);
        if (coreParts.Length == 0) return false;
        var core = new List<int>();
        foreach (string part in coreParts)
        {
            if (!int.TryParse(part, out int number) || number < 0) return false;
            core.Add(number);
        }
        string[] prerelease = split.Length == 2
            ? split[1].Split('.', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        if (split.Length == 2 && prerelease.Length == 0) return false;
        version = new ParsedVersion(core.ToArray(), prerelease);
        return true;
    }

    private static int Compare(ParsedVersion left, ParsedVersion right)
    {
        int max = Math.Max(left.Core.Length, right.Core.Length);
        for (int i = 0; i < max; i++)
        {
            int l = i < left.Core.Length ? left.Core[i] : 0;
            int r = i < right.Core.Length ? right.Core[i] : 0;
            int comparison = l.CompareTo(r);
            if (comparison != 0) return comparison;
        }

        bool leftStable = left.Prerelease.Length == 0;
        bool rightStable = right.Prerelease.Length == 0;
        if (leftStable != rightStable) return leftStable ? 1 : -1;
        if (leftStable) return 0;

        int count = Math.Max(left.Prerelease.Length, right.Prerelease.Length);
        for (int i = 0; i < count; i++)
        {
            if (i >= left.Prerelease.Length) return -1;
            if (i >= right.Prerelease.Length) return 1;
            string l = left.Prerelease[i];
            string r = right.Prerelease[i];
            bool ln = long.TryParse(l, out long lv);
            bool rn = long.TryParse(r, out long rv);
            int comparison;
            if (ln && rn) comparison = lv.CompareTo(rv);
            else if (ln != rn) comparison = ln ? -1 : 1;
            else comparison = string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private sealed record ParsedVersion(int[] Core, string[] Prerelease);
}
