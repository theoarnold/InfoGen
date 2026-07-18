using System.Text.RegularExpressions;

namespace InfoGen.Services;

/// <summary>Splits model-generated body text into paragraphs.</summary>
public static class ParagraphSplitter
{
    private static readonly Regex BlankLine = new(@"\n[ \t]*\n");
    private static readonly Regex Whitespace = new(@"\s+");

    /// <summary>
    /// Splits a block of text into paragraphs on blank lines.
    /// Tolerates literal "\n" escape sequences: the model is asked for real newlines, but some model
    /// versions emit the two-character sequence backslash-n instead, which would otherwise survive
    /// into the rendered article as a visible "\n\n".
    /// </summary>
    public static List<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        // Order matters: collapse the escaped forms to real newlines before normalising line endings.
        var normalized = text
            .Replace("\\r\\n", "\n")
            .Replace("\\n", "\n")
            .Replace("\\r", "\n")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        return BlankLine.Split(normalized)
            .Select(p => Whitespace.Replace(p, " ").Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }
}
