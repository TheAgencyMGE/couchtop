using System.Text;

namespace Couchtop.Core.Discovery;

/// <summary>A node of Valve's KeyValues (VDF/ACF) text format.</summary>
public sealed class VdfNode
{
    public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetString(string key) => Values.TryGetValue(key, out var v) ? v as string : null;
    public VdfNode? GetNode(string key) => Values.TryGetValue(key, out var v) ? v as VdfNode : null;
}

public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        var pos = 0;
        var root = new VdfNode();
        ParseBody(text, ref pos, root, isRoot: true);
        return root;
    }

    private static void ParseBody(string text, ref int pos, VdfNode node, bool isRoot)
    {
        while (true)
        {
            var token = NextToken(text, ref pos, out var kind);
            if (kind == TokenKind.End)
            {
                if (!isRoot) throw new FormatException("Unexpected end of VDF data (missing '}').");
                return;
            }
            if (kind == TokenKind.Close)
            {
                if (isRoot) throw new FormatException("Unexpected '}' in VDF data.");
                return;
            }
            if (kind == TokenKind.Open) throw new FormatException("Unexpected '{' where a key was expected.");

            var key = token!;
            var valueToken = NextToken(text, ref pos, out var valueKind);
            switch (valueKind)
            {
                case TokenKind.String:
                    node.Values[key] = valueToken!;
                    SkipConditional(text, ref pos);
                    break;
                case TokenKind.Open:
                    var child = new VdfNode();
                    ParseBody(text, ref pos, child, isRoot: false);
                    node.Values[key] = child;
                    break;
                default:
                    throw new FormatException($"Key '{key}' has no value.");
            }
        }
    }

    private enum TokenKind { String, Open, Close, End }

    private static void SkipConditional(string text, ref int pos)
    {
        var save = pos;
        SkipWhitespaceAndComments(text, ref pos);
        if (pos < text.Length && text[pos] == '[')
        {
            var end = text.IndexOf(']', pos);
            pos = end < 0 ? text.Length : end + 1;
        }
        else
        {
            pos = save;
        }
    }

    private static void SkipWhitespaceAndComments(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            if (char.IsWhiteSpace(text[pos]) || text[pos] == '﻿')
            {
                pos++;
            }
            else if (text[pos] == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
            }
            else
            {
                break;
            }
        }
    }

    private static string? NextToken(string text, ref int pos, out TokenKind kind)
    {
        SkipWhitespaceAndComments(text, ref pos);
        if (pos >= text.Length)
        {
            kind = TokenKind.End;
            return null;
        }

        var c = text[pos];
        if (c == '{') { pos++; kind = TokenKind.Open; return null; }
        if (c == '}') { pos++; kind = TokenKind.Close; return null; }

        var sb = new StringBuilder();
        kind = TokenKind.String;
        if (c == '"')
        {
            pos++;
            while (pos < text.Length && text[pos] != '"')
            {
                if (text[pos] == '\\' && pos + 1 < text.Length)
                {
                    pos++;
                    sb.Append(text[pos] switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', var other => other });
                }
                else
                {
                    sb.Append(text[pos]);
                }
                pos++;
            }
            if (pos >= text.Length) throw new FormatException("Unterminated string in VDF data.");
            pos++;
            return sb.ToString();
        }

        while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not ('{' or '}' or '"'))
            sb.Append(text[pos++]);
        return sb.ToString();
    }
}
