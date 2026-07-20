using System.Text;
using System.Text.RegularExpressions;

namespace InfoGen.Services;

/// <summary>Renders paragraph text with [[Title]] replaced by anchor tags using ReferenceLinks.</summary>
public static class ReferenceLinkRenderer
{
    /// <summary>Returns HTML string for the paragraph with [[Title]] turned into &lt;a href="/wiki/Slug"&gt;Title&lt;/a&gt;. Safe to wrap in MarkupString.</summary>
    public static string ToHtml(string paragraph, List<ReferenceLink>? referenceLinks)
    {
        if (string.IsNullOrEmpty(paragraph))
            return "";

        // Deliberately no early-out for an empty link list: the text may still contain [[Title]]
        // markers the model invented, and those must be stripped rather than rendered as brackets.
        referenceLinks ??= [];

        var sb = new StringBuilder();
        int i = 0;
        while (i < paragraph.Length)
        {
            int open = paragraph.IndexOf("[[", i, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(System.Net.WebUtility.HtmlEncode(paragraph[i..]));
                break;
            }
            sb.Append(System.Net.WebUtility.HtmlEncode(paragraph[i..open]));
            int close = paragraph.IndexOf("]]", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                sb.Append(System.Net.WebUtility.HtmlEncode(paragraph[open..]));
                break;
            }
            var rawTitle = paragraph[(open + 2)..close].Trim();
            var normalizedTitle = NormalizeTitle(rawTitle);
            var link = referenceLinks.FirstOrDefault(r => string.Equals(NormalizeTitle(r.Title), normalizedTitle, StringComparison.OrdinalIgnoreCase));
            if (link != null)
            {
                var href = System.Net.WebUtility.HtmlEncode("/wiki/" + link.Slug);
                var displayTitle = System.Net.WebUtility.HtmlEncode(link.Title);
                sb.Append("<a href=\"").Append(href).Append("\">").Append(displayTitle).Append("</a>");
            }
            else
                // Unknown title (model invented or misspelled one): show the text without the
                // brackets rather than leaking literal "[[...]]" into the article.
                sb.Append(System.Net.WebUtility.HtmlEncode(rawTitle));
            i = close + 2;
        }
        return sb.ToString();
    }

    /// <summary>Returns every [[Title]] referenced in the text, in order, de-duplicated case-insensitively.
    /// Used after generation to work out which existing articles the model actually linked, so only those
    /// get stored (the catalogue sent to the model is far too large to persist).</summary>
    public static List<string> ExtractTitles(string? paragraph)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(paragraph)) return found;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < paragraph.Length)
        {
            int open = paragraph.IndexOf("[[", i, StringComparison.Ordinal);
            if (open < 0) break;
            int close = paragraph.IndexOf("]]", open + 2, StringComparison.Ordinal);
            if (close < 0) break;

            var title = NormalizeTitle(paragraph[(open + 2)..close]);
            if (title.Length > 0 && seen.Add(title))
                found.Add(title);
            i = close + 2;
        }
        return found;
    }

    /// <summary>Returns the text with [[Title]] markers reduced to plain "Title". For places that store
    /// or display the prose as text rather than HTML - <see cref="ToHtml"/> is the equivalent for
    /// rendering, but it HTML-encodes, which is wrong for a value going into the database.</summary>
    public static string StripMarkers(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf("[[", i, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(text[i..]);
                break;
            }
            sb.Append(text[i..open]);
            int close = text.IndexOf("]]", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                sb.Append(text[open..]);
                break;
            }
            sb.Append(text[(open + 2)..close].Trim());
            i = close + 2;
        }
        return sb.ToString();
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        var collapsed = Regex.Replace(title.Trim(), @"\s+", " ");
        return collapsed;
    }
}
