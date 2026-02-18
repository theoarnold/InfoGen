namespace InfoGen.Models;

public class WikipediaPage
{
    public string Title { get; set; } = "";
    public string Extract { get; set; } = "";
    public int PageId { get; set; }
    public string Url => $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(Title.Replace(' ', '_'))}";
}
