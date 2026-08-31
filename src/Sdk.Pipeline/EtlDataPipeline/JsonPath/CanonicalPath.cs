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

    /// <summary>
    /// Normalizes a user-supplied path (bare, leading-dot, rooted, or bracket-quoted spelling)
    /// into the canonical write form the overlay grammar accepts - "$" followed by ".name" and
    /// "[index]" segments - validating it on the way. Throws <see cref="JsonPathException"/>
    /// for malformed paths and <see cref="JsonPathNotSupportedException"/> for constructs a
    /// write path cannot address (wildcards, filters, recursive descent, property names the
    /// dotted grammar cannot express).
    /// </summary>
    public static string NormalizeWritePath(string path)
    {
        var expression = JsonPathParser.Parse(JsonNodePath.NormalizePathOrRelative(path));
        var canonical = "$";
        foreach (var seg in expression.Segments)
        {
            switch (seg)
            {
                case RootSegment:
                    continue;
                case PropertySegment p when IsWritablePropertyName(p.Name):
                    canonical += "." + p.Name;
                    break;
                case PropertySegment p:
                    throw new JsonPathNotSupportedException($"property name '{p.Name}' in write path", path, 0);
                case IndexSegment i:
                    canonical += "[" + i.Index + "]";
                    break;
                default:
                    throw new JsonPathNotSupportedException($"{seg.GetType().Name} in write path", path, 0);
            }
        }
        return canonical;
    }

    // Mirrors JsonPathParser.IsIdentifierChar: only names the dotted write grammar can
    // round-trip are allowed; anything else must stay bracket-quoted and is not writable.
    private static bool IsWritablePropertyName(string name)
    {
        if (name.Length == 0) return false;
        foreach (var ch in name)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-') return false;
        }
        return true;
    }
}
