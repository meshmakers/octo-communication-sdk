namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

internal static class CanonicalPath
{
    public static bool IsAncestor(string ancestor, string descendant)
    {
        if (ancestor == "$") return true;
        if (!descendant.StartsWith(ancestor, StringComparison.Ordinal)) return false;
        if (descendant.Length == ancestor.Length) return true;
        var next = descendant[ancestor.Length];
        return next == '.' || next == '[';
    }

    public static IReadOnlyList<string> GetSegments(string path)
    {
        if (path == "$") return Array.Empty<string>();
        var segments = new List<string>();
        var i = 1; // skip '$'
        while (i < path.Length)
        {
            var start = i;
            if (path[i] == '.')
            {
                i++;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                segments.Add(path.Substring(start, i - start));
            }
            else if (path[i] == '[')
            {
                while (i < path.Length && path[i] != ']') i++;
                if (i < path.Length) i++; // consume ']'
                segments.Add(path.Substring(start, i - start));
            }
            else
            {
                throw new ArgumentException($"Malformed canonical path: '{path}'");
            }
        }
        return segments;
    }

    public static string? GetParent(string path)
    {
        if (path == "$") return null;
        var segments = GetSegments(path);
        if (segments.Count == 0) return null;
        var parent = "$" + string.Concat(segments.Take(segments.Count - 1));
        return parent;
    }
}
