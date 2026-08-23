using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BlogWatcher.Tests;

public sealed class IntegrationTests
{
    [Fact]
    public async Task FixedRss_IsMappedToCommonArticleModel()
    {
        const string rss = """
            <rss version="2.0"><channel><item>
              <title>TiDB release</title><link>https://example.com/post</link>
              <description>summary</description><content:encoded xmlns:content="http://purl.org/rss/1.0/modules/content/">full body</content:encoded>
              <pubDate>Sat, 22 Aug 2026 00:00:00 GMT</pubDate>
            </item></channel></rss>
            """;
        using var client = new HttpClient(new FixedResponseHandler(rss));
        var source = new RssArticleSource(client, NullLogger<RssArticleSource>.Instance);
        var articles = await source.FetchAsync(new() { Id = "source", Name = "Source", Url = "https://example.com/feed" }, TestContext.Current.CancellationToken);
        var article = Assert.Single(articles);
        Assert.Equal("source", article.SourceId);
        Assert.Contains("summary", article.SearchableText);
        Assert.Contains("full body", article.SearchableText);
        Assert.NotNull(article.PublishedAt);
    }

    [Fact]
    public async Task FirstRun_SavesMatchesThenSendsNormalEmptyEmail()
    {
        var fixture = Fixture();
        await fixture.Job.RunAsync(false, TestContext.Current.CancellationToken);
        Assert.Single(fixture.State.Seen);
        Assert.Contains("source", fixture.State.Initialized);
        Assert.Single(fixture.Email.Messages);
        Assert.Contains("新着記事はありません", fixture.Email.Messages[0].Body);
    }

    [Fact]
    public async Task SubsequentRun_SendsOnlyUnreadTogether_ThenMarksSeen()
    {
        var fixture = Fixture(initialized: true, articles: [Article("/old"), Article("/new1"), Article("/new2")]);
        fixture.State.Seen.Add(ArticleRules.SeenKey("https://example.com/old"));
        await fixture.Job.RunAsync(false, TestContext.Current.CancellationToken);
        Assert.Single(fixture.Email.Messages);
        Assert.Contains("新着2件", fixture.Email.Messages[0].Subject);
        Assert.Equal(3, fixture.State.Seen.Count);
        Assert.Equal("email", fixture.Events[0]);
        Assert.All(fixture.Events.Skip(1), x => Assert.Equal("seen", x));
    }

    [Fact]
    public async Task NoNews_StillSendsAndSucceeds()
    {
        var fixture = Fixture(initialized: true, articles: []);
        await fixture.Job.RunAsync(false, TestContext.Current.CancellationToken);
        Assert.Single(fixture.Email.Messages);
        Assert.Contains("新着記事はありません", fixture.Email.Messages[0].Body);
    }

    [Fact]
    public async Task FetchFailure_PerformsNoSideEffects()
    {
        var fixture = Fixture(failFetch: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Job.RunAsync(false, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.State.Seen);
        Assert.Empty(fixture.Email.Messages);
    }

    [Fact]
    public async Task EmailFailure_DoesNotMarkNewArticleSeen()
    {
        var fixture = Fixture(initialized: true, failEmail: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Job.RunAsync(false, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.State.Seen);
    }

    [Fact]
    public async Task DryRun_ReadsButNeverWritesOrEmails()
    {
        var fixture = Fixture();
        await fixture.Job.RunAsync(true, TestContext.Current.CancellationToken);
        Assert.True(fixture.State.ReadCount > 0);
        Assert.Empty(fixture.State.Seen);
        Assert.Empty(fixture.State.Initialized);
        Assert.Empty(fixture.Email.Messages);
    }

    [Fact]
    public async Task AllInvalidArticles_FailsWithoutSideEffects()
    {
        var fixture = Fixture(articles: [new("source", "Source", "Broken", "not-a-url", null, "unmatched")]);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Job.RunAsync(false, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.State.Seen);
        Assert.Empty(fixture.Email.Messages);
    }

    private static Article Article(string path = "/article") => new("source", "Source", "Title", "https://example.com" + path, null, "MATCH");

    private static TestFixture Fixture(bool initialized = false, IReadOnlyList<Article>? articles = null, bool failFetch = false, bool failEmail = false)
    {
        var events = new List<string>();
        var state = new FakeState(events);
        if (initialized) state.Initialized.Add("source");
        var email = new FakeEmail(events) { Fail = failEmail };
        var source = new FakeSource(articles ?? [Article()]) { Fail = failFetch };
        var options = Options.Create(new WatcherOptions { Sources = [new() { Id = "source", Name = "Source", Type = "rss", Url = "https://example.com/feed", Keywords = ["match"] }] });
        var job = new WatcherJob(options, [source], state, new PassThroughArticleProcessor(), email, NullLogger<WatcherJob>.Instance);
        return new(job, state, email, events);
    }

    private sealed record TestFixture(WatcherJob Job, FakeState State, FakeEmail Email, List<string> Events);
    private sealed class FakeSource(IReadOnlyList<Article> articles) : IArticleSource
    {
        public string Type => "rss";
        public bool Fail { get; init; }
        public Task<IReadOnlyList<Article>> FetchAsync(SourceOptions source, CancellationToken token) => Fail ? throw new InvalidOperationException("fetch") : Task.FromResult(articles);
    }
    private sealed class FakeState(List<string> events) : IStateStore
    {
        public HashSet<string> Seen { get; } = [];
        public HashSet<string> Initialized { get; } = [];
        public int ReadCount { get; private set; }
        public Task<bool> IsInitializedAsync(string id, CancellationToken token) { ReadCount++; return Task.FromResult(Initialized.Contains(id)); }
        public Task<bool> IsSeenAsync(string key, CancellationToken token) { ReadCount++; return Task.FromResult(Seen.Contains(key)); }
        public Task MarkSeenAsync(string key, Article article, CancellationToken token) { events.Add("seen"); Seen.Add(key); return Task.CompletedTask; }
        public Task MarkInitializedAsync(string id, CancellationToken token) { Initialized.Add(id); return Task.CompletedTask; }
    }
    private sealed class FakeEmail(List<string> events) : IEmailSender
    {
        public bool Fail { get; init; }
        public List<(string Subject, string Body)> Messages { get; } = [];
        public Task SendAsync(string subject, string body, CancellationToken token)
        {
            if (Fail) throw new InvalidOperationException("email");
            events.Add("email"); Messages.Add((subject, body)); return Task.CompletedTask;
        }
    }
    private sealed class FixedResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(content) });
    }
}
