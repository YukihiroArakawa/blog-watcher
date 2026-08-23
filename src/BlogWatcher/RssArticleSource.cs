using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace BlogWatcher;

public sealed class RssArticleSource(HttpClient client, ILogger<RssArticleSource> logger) : IArticleSource
{
    public string Type => "rss";
    public async Task<IReadOnlyList<Article>> FetchAsync(SourceOptions source, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(source.Url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var items = document.Descendants().Where(x => x.Name.LocalName is "item" or "entry").ToList();
        if (items.Count == 0) throw new InvalidDataException($"Feed '{source.Id}' contains no articles.");
        var articles = new List<Article>();
        foreach (var item in items)
        {
            string? Value(params string[] names) => item.Elements().FirstOrDefault(x => names.Contains(x.Name.LocalName, StringComparer.OrdinalIgnoreCase))?.Value?.Trim();
            var title = Value("title");
            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogWarning("Excluded article without a title from {SourceId}", source.Id);
                continue;
            }
            var links = item.Elements().Where(x => x.Name.LocalName == "link").ToList();
            var url = links.FirstOrDefault(x => x.Attribute("rel")?.Value is null or "alternate")?.Attribute("href")?.Value ?? Value("link") ?? "";
            var description = Value("description", "summary") ?? "";
            var content = Value("encoded", "content") ?? "";
            var dateText = Value("pubDate", "published", "updated");
            DateTimeOffset? published = DateTimeOffset.TryParse(dateText, out var parsed) ? parsed : null;
            articles.Add(new(source.Id, source.Name, title, url, published, string.Join('\n', title, description, content)));
        }
        if (articles.Count == 0) throw new InvalidDataException($"Feed '{source.Id}' contains no valid articles.");
        logger.LogInformation("Fetched source {SourceId}: {FetchedCount} articles", source.Id, articles.Count);
        return articles;
    }
}
