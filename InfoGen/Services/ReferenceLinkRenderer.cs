using System.Text;
using System.Text.RegularExpressions;

namespace InfoGen.Services;

/// <summary>Renders paragraph text with [[Title]] replaced by anchor tags using ReferenceLinks.</summary>
public static class ReferenceLinkRenderer
{
    // This needs looking at properly
    /// <summary>Returns HTML string for the paragraph with [[Title]] turned into &lt;a href="/wiki/Slug"&gt;Title&lt;/a&gt;. Safe to wrap in MarkupString.</summary>
    public static string ToHtml(string paragraph, List<ReferenceLink>? referenceLinks)
    {
        if (string.IsNullOrEmpty(paragraph))
            return "";
        if (referenceLinks == null || referenceLinks.Count == 0)
            return System.Net.WebUtility.HtmlEncode(paragraph);

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
                sb.Append(System.Net.WebUtility.HtmlEncode(paragraph[open..(close + 2)]));
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
