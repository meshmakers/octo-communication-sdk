namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.JsonPath;

/// <summary>
/// Parses JSONPath expressions into a structured <see cref="JsonPathExpression"/> for evaluation.
/// </summary>
public static class JsonPathParser
{
    /// <summary>
    /// Parses the given JSONPath string into a <see cref="JsonPathExpression"/>.
    /// </summary>
    /// <param name="path">The JSONPath expression. Must start with <c>$</c>.</param>
    /// <returns>The parsed expression.</returns>
    /// <exception cref="JsonPathException">Thrown when the expression is empty, missing the root, or otherwise malformed.</exception>
    public static JsonPathExpression Parse(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new JsonPathException("Path is empty", path, 0);
        }

        var segments = new List<PathSegment>();
        var pos = 0;

        if (path[pos] != '$')
        {
            throw new JsonPathException("Path must start with '$'", path, pos);
        }
        segments.Add(new RootSegment());
        pos++;

        while (pos < path.Length)
        {
            if (path[pos] == '.')
            {
                pos++;
                if (pos < path.Length && path[pos] == '.')
                {
                    pos++; // consume second '.'
                    segments.Add(new RecursiveDescentSegment());
                    // After ..  the next thing must be either a property identifier
                    // (no leading dot) or a '[' bracket selector. Anything else — end
                    // of string, another '.', or any other character — is malformed.
                    if (pos >= path.Length)
                    {
                        throw new JsonPathException("Trailing '..' with no property or bracket selector", path, pos);
                    }
                    if (IsIdentifierChar(path[pos]))
                    {
                        var s = pos;
                        while (pos < path.Length && IsIdentifierChar(path[pos])) pos++;
                        segments.Add(new PropertySegment(path.Substring(s, pos - s)));
                    }
                    else if (path[pos] != '[')
                    {
                        throw new JsonPathException("Trailing '..' with no property or bracket selector", path, pos);
                    }
                    // brackets ([*], [N], [?(...)]) handled by next loop iteration as normal
                    continue;
                }

                if (pos >= path.Length)
                {
                    throw new JsonPathException("Trailing '.' with no property", path, pos);
                }

                var start = pos;
                while (pos < path.Length && IsIdentifierChar(path[pos]))
                {
                    pos++;
                }
                if (pos == start)
                {
                    throw new JsonPathException("Expected property name after '.'", path, pos);
                }
                segments.Add(new PropertySegment(path.Substring(start, pos - start)));
            }
            else if (path[pos] == '[')
            {
                pos++; // consume '['
                if (pos >= path.Length)
                {
                    throw new JsonPathException("Unterminated '['", path, pos);
                }

                if (path[pos] == '*')
                {
                    pos++;
                    ExpectClosingBracket(path, ref pos);
                    segments.Add(new WildcardSegment());
                }
                else if (path[pos] == '-')
                {
                    throw new JsonPathNotSupportedException("negative array index", path, pos);
                }
                else if (char.IsDigit(path[pos]))
                {
                    var numStart = pos;
                    while (pos < path.Length && char.IsDigit(path[pos])) pos++;

                    // Detect slice: digits followed by ':'
                    if (pos < path.Length && path[pos] == ':')
                    {
                        throw new JsonPathNotSupportedException("array slice", path, pos);
                    }

                    var index = int.Parse(path.Substring(numStart, pos - numStart));
                    ExpectClosingBracket(path, ref pos);
                    segments.Add(new IndexSegment(index));
                }
                else if (path[pos] == '?')
                {
                    pos++; // consume '?'
                    if (pos >= path.Length || path[pos] != '(')
                    {
                        throw new JsonPathException("Expected '(' after '?'", path, pos);
                    }
                    pos++; // consume '('

                    // Consume '@' followed by relative property path
                    if (pos >= path.Length || path[pos] != '@')
                    {
                        throw new JsonPathException("Filter must start with '@'", path, pos);
                    }
                    pos++;

                    var props = new List<string>();
                    while (pos < path.Length && path[pos] == '.')
                    {
                        pos++;
                        var s = pos;
                        while (pos < path.Length && IsIdentifierChar(path[pos])) pos++;
                        if (pos == s)
                        {
                            throw new JsonPathException("Expected property in filter", path, pos);
                        }
                        props.Add(path.Substring(s, pos - s));
                    }

                    SkipWhitespace(path, ref pos);

                    // Detect operator. Only '==' supported.
                    if (pos + 1 >= path.Length)
                    {
                        throw new JsonPathException("Unterminated filter", path, pos);
                    }
                    // Reject regex first (it shares '=' with '==')
                    if (path[pos] == '=' && pos + 1 < path.Length && path[pos + 1] == '~')
                    {
                        throw new JsonPathNotSupportedException("filter regex '=~'", path, pos);
                    }
                    if (path[pos] == '!' && path[pos + 1] == '=')
                    {
                        throw new JsonPathNotSupportedException("filter operator '!='", path, pos);
                    }
                    if (path[pos] == '<' || path[pos] == '>')
                    {
                        throw new JsonPathNotSupportedException($"filter operator '{path[pos]}'", path, pos);
                    }
                    if (path[pos] == '=' && (pos + 1 >= path.Length || path[pos + 1] != '='))
                    {
                        throw new JsonPathNotSupportedException("single '='", path, pos);
                    }
                    if (path[pos] != '=' || path[pos + 1] != '=')
                    {
                        throw new JsonPathException("Expected '==' in filter", path, pos);
                    }
                    pos += 2;
                    SkipWhitespace(path, ref pos);

                    // Consume string literal — accept both ' and " (Newtonsoft parity).
                    if (pos >= path.Length || (path[pos] != '\'' && path[pos] != '"'))
                    {
                        throw new JsonPathNotSupportedException("non-string-literal filter rhs", path, pos);
                    }
                    var literal = ScanQuotedLiteral(path, ref pos);

                    SkipWhitespace(path, ref pos);

                    // Reject logical operators
                    if (pos < path.Length && (path[pos] == '&' || path[pos] == '|'))
                    {
                        throw new JsonPathNotSupportedException("filter logical operator", path, pos);
                    }

                    if (pos >= path.Length || path[pos] != ')')
                    {
                        throw new JsonPathException("Expected ')' to close filter", path, pos);
                    }
                    pos++; // consume ')'
                    ExpectClosingBracket(path, ref pos);
                    segments.Add(new FilterSegment(props, literal));
                }
                else if (path[pos] == '\'' || path[pos] == '"')
                {
                    var literal = ScanQuotedLiteral(path, ref pos);
                    ExpectClosingBracket(path, ref pos);
                    segments.Add(new PropertySegment(literal));
                }
                else
                {
                    throw new JsonPathException($"Unexpected character '{path[pos]}' inside '['", path, pos);
                }
            }
            else
            {
                throw new JsonPathException($"Unexpected character '{path[pos]}'", path, pos);
            }
        }

        return new JsonPathExpression(segments);
    }

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';

    private static void ExpectClosingBracket(string path, ref int pos)
    {
        if (pos >= path.Length || path[pos] != ']')
        {
            throw new JsonPathException("Expected ']'", path, pos);
        }
        pos++;
    }

    private static void SkipWhitespace(string path, ref int pos)
    {
        while (pos < path.Length && (path[pos] == ' ' || path[pos] == '\t')) pos++;
    }

    /// <summary>
    /// Scans a quoted string literal at <paramref name="pos"/>, consuming the
    /// opening quote (either <c>'</c> or <c>"</c>), the literal body, and the
    /// matching closing quote. Returns the literal body. Callers are
    /// responsible for verifying that <paramref name="pos"/> points at a valid
    /// opening quote before invoking; on entry it must, on exit it points
    /// just past the closing quote.
    /// </summary>
    private static string ScanQuotedLiteral(string path, ref int pos)
    {
        var quote = path[pos];
        pos++; // consume opening quote
        var litStart = pos;
        while (pos < path.Length && path[pos] != quote) pos++;
        if (pos >= path.Length)
        {
            throw new JsonPathException("Unterminated string literal", path, pos);
        }
        var literal = path.Substring(litStart, pos - litStart);
        pos++; // consume closing quote
        return literal;
    }
}
