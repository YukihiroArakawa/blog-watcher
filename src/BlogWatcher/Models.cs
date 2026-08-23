namespace BlogWatcher;

public sealed record Article(string SourceId, string SourceName, string Title, string Url,
    DateTimeOffset? PublishedAt, string SearchableText);

public sealed record NotificationArticle(string SourceName, string Title, string Url, DateTimeOffset? PublishedAt);
public sealed record Notification(DateOnly Date, IReadOnlyList<NotificationArticle> Articles);

public sealed class SourceOptions
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Url { get; init; } = "";
    public string[] Keywords { get; init; } = [];
}

public sealed class WatcherOptions
{
    public List<SourceOptions> Sources { get; init; } = [];
}

public sealed class ExternalOptions
{
    public string CloudflareAccountId { get; init; } = "";
    public string CloudflareNamespaceId { get; init; } = "";
    public string CloudflareApiToken { get; init; } = "";
    public string GmailAddress { get; init; } = "";
    public string GmailAppPassword { get; init; } = "";
}
