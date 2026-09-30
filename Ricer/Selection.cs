using System.Text.RegularExpressions;

namespace Ricer;

/// <summary>
/// Caelestia-style package selection: tokens split on [,\s]+;
/// "..." = all, ^N/^name = exclude, ranges A-B / A- / -B, app names allowed.
/// Ported verbatim from the PowerShell Expand-Selection.
/// </summary>
public static class Selection
{
    public static List<string> Expand(List<string> apps, List<string> selectors)
    {
        int n = apps.Count;

        bool all = false;
        var pos = new List<int>();
        var excl = new List<int>();
        var tokens = new List<string>();
        foreach (var s in selectors)
            tokens.AddRange(s.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

        foreach (var tk in tokens)
        {
            if (tk == "...")
            {
                all = true;
                continue;
            }

            bool caret = tk.StartsWith('^');
            string body = caret ? tk[1..] : tk;
            var range = new List<int>();

            int nameHit = apps.FindIndex(a => string.Equals(a, body, StringComparison.OrdinalIgnoreCase));
            if (nameHit >= 0)
            {
                range.Add(nameHit + 1);
            }
            else if (Regex.Match(body, @"^(\d+)-(\d+)$") is { Success: true } m)
            {
                int a = int.Parse(m.Groups[1].Value);
                int b = int.Parse(m.Groups[2].Value);
                if (a > b) (a, b) = (b, a);
                a = Math.Max(a, 1);
                b = Math.Min(b, n);
                range = a <= b ? Enumerable.Range(a, b - a + 1).ToList() : [];
            }
            else if (Regex.IsMatch(body, @"^\d+-$"))
            {
                int a = Math.Max(1, int.Parse(body[..^1]));
                range = a <= n ? Enumerable.Range(a, n - a + 1).ToList() : [];
            }
            else if (Regex.IsMatch(body, @"^-\d+$"))
            {
                int b = Math.Min(n, int.Parse(body[1..]));
                range = b >= 1 ? Enumerable.Range(1, b).ToList() : [];
            }
            else if (Regex.IsMatch(body, @"^\d+$"))
            {
                int i0 = int.Parse(body);
                if (i0 < 1 || i0 > n)
                {
                    Ricer.Warn($"index {i0} out of range (1..{n}), skipping");
                    continue;
                }
                range.Add(i0);
            }
            else
            {
                throw new InvalidOperationException(
                    $"bad selector '{tk}' - use numbers, 1-4, 2-, -4, ... for all, or ^4 to exclude");
            }

            var dest = caret ? excl : pos;
            foreach (int i in range)
                if (!dest.Contains(i))
                    dest.Add(i);
        }

        var sel = new List<int>();
        if (all) sel = Enumerable.Range(1, n).ToList();
        else if (pos.Count > 0) sel = [.. pos];
        else if (excl.Count > 0) sel = Enumerable.Range(1, n).ToList();

        foreach (int x in excl) sel.Remove(x);

        var names = new List<string>();
        foreach (int i in sel)
        {
            if (i < 1 || i > n)
            {
                Ricer.Warn($"skipping out-of-range index {i} (1..{n})");
                continue;
            }
            names.Add(apps[i - 1]);
        }
        return names;
    }
}
