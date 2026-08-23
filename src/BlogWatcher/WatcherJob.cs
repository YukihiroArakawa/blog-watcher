using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlogWatcher;

public sealed class WatcherJob(
    IOptions<WatcherOptions> options,
    IEnumerable<IArticleSource> articleSources,
    IStateStore state,
    IArticleProcessor processor,
    IEmailSender email,
    ILogger<WatcherJob> logger)
{
    public async Task RunAsync(bool dryRun, CancellationToken cancellationToken)
    {
        var sourcesByType = articleSources.ToDictionary(x => x.Type, StringComparer.OrdinalIgnoreCase);
        var fetched = new List<(SourceOptions Source, IReadOnlyList<Article> Articles)>();

        // Fetch every source before any side effect so an incomplete daily result is never committed.
        foreach (var source in options.Value.Sources)
            fetched.Add((source, await sourcesByType[source.Type].FetchAsync(source, cancellationToken)));

        var newArticles = new List<Article>();
        foreach (var (source, rawArticles) in fetched)
        {
            var excluded = 0;
            var validArticles = ArticleRules.NormalizeAndDeduplicate(rawArticles, (article, exception) =>
            {
                excluded++;
                logger.LogWarning(exception, "Excluded invalid article URL from {SourceId}; title={Title}", source.Id, article.Title);
            });
            if (rawArticles.Count > 0 && validArticles.Count == 0)
                throw new InvalidDataException($"All articles in source '{source.Id}' have invalid URLs.");
            var normalized = validArticles.Where(x => ArticleRules.Matches(x, source.Keywords)).ToList();

            var initialized = await state.IsInitializedAsync(source.Id, cancellationToken);
            var seen = 0;
            foreach (var article in normalized)
            {
                var key = ArticleRules.SeenKey(article.Url);
                if (await state.IsSeenAsync(key, cancellationToken)) { seen++; continue; }
                if (!initialized)
                {
                    if (!dryRun) await state.MarkSeenAsync(key, article, cancellationToken);
                }
                else newArticles.Add(article);
            }
            if (!initialized && !dryRun) await state.MarkInitializedAsync(source.Id, cancellationToken);
            logger.LogInformation("Processed source {SourceId}: FetchedCount={FetchedCount}, MatchedCount={MatchedCount}, SeenCount={SeenCount}, NewCount={NewCount}, ExcludedCount={ExcludedCount}, Initialized={Initialized}",
                source.Id, rawArticles.Count, normalized.Count, seen, initialized ? normalized.Count - seen : 0, excluded, initialized);
        }

        var processed = await processor.ProcessAsync(newArticles, cancellationToken);
        var jst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var notification = new Notification(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, jst).DateTime),
            processed.Select(x => new NotificationArticle(x.SourceName, x.Title, x.Url, x.PublishedAt)).ToList());
        var formatted = NotificationFormatter.Format(notification);
        if (dryRun)
        {
            logger.LogInformation("Dry run: NewCount={NewCount}; no email or KV writes were performed. Subject={Subject}", processed.Count, formatted.Subject);
            return;
        }
        await email.SendAsync(formatted.Subject, formatted.Body, cancellationToken);
        foreach (var article in processed) await state.MarkSeenAsync(ArticleRules.SeenKey(article.Url), article, cancellationToken);
    }
}
