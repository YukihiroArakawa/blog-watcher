using Microsoft.Extensions.Options;

namespace BlogWatcher.Tests;

public sealed class UnitTests
{
    [Fact]
    public void UrlNormalization_IsSafeAndPreservesQueryAndSlash()
    {
        Assert.Equal("https://example.com/path/?x=1", ArticleRules.NormalizeUrl("HTTPS://EXAMPLE.COM:443/path/?x=1#part"));
        Assert.NotEqual(ArticleRules.NormalizeUrl("https://example.com/path"), ArticleRules.NormalizeUrl("https://example.com/path/"));
    }

    [Fact]
    public void Matching_IsCaseInsensitiveOr()
    {
        var article = NewArticle("https://example.com", "A tidb weekly post");
        Assert.True(ArticleRules.Matches(article, ["absent", "TiDB WEEKLY"]));
        Assert.False(ArticleRules.Matches(article, ["postgres"]));
    }

    [Fact]
    public void DuplicateNormalizedUrls_AreCollapsed()
    {
        var result = ArticleRules.NormalizeAndDeduplicate([NewArticle("https://EXAMPLE.com:443/a#x"), NewArticle("https://example.com/a")]);
        Assert.Single(result);
    }

    [Fact]
    public void Notification_ContainsSubjectAndRequiredBody()
    {
        var formatted = NotificationFormatter.Format(new(new(2026, 8, 22), [new("Source", "Title", "https://example.com", null)]));
        Assert.Equal("[blog-watcher] 新着1件 - 2026-08-22", formatted.Subject);
        Assert.Contains("監視元: Source", formatted.Body);
        Assert.Contains("タイトル: Title", formatted.Body);
    }

    [Fact]
    public void EmptyNotification_HasNormalNoArticlesText() =>
        Assert.Contains("新着記事はありません", NotificationFormatter.Format(new(new(2026, 8, 22), [])).Body);

    [Fact]
    public void Configuration_RejectsDuplicateIdsAndEmptyKeywords()
    {
        var source = new SourceOptions { Id = "same", Name = "x", Type = "rss", Url = "https://example.com", Keywords = [] };
        var result = new WatcherOptionsValidator().Validate(null, new WatcherOptions { Sources = [source, source] });
        Assert.True(result.Failed);
    }

    private static Article NewArticle(string url, string text = "match") => new("source", "Source", "Title", url, null, text);
}
