namespace BlogWatcher;

public interface IArticleSource
{
    string Type { get; }
    Task<IReadOnlyList<Article>> FetchAsync(SourceOptions source, CancellationToken cancellationToken);
}

public interface IStateStore
{
    Task<bool> IsInitializedAsync(string sourceId, CancellationToken cancellationToken);
    Task<bool> IsSeenAsync(string key, CancellationToken cancellationToken);
    Task MarkSeenAsync(string key, Article article, CancellationToken cancellationToken);
    Task MarkInitializedAsync(string sourceId, CancellationToken cancellationToken);
}

public interface IEmailSender
{
    Task SendAsync(string subject, string body, CancellationToken cancellationToken);
}

public interface IArticleProcessor
{
    Task<IReadOnlyList<Article>> ProcessAsync(IReadOnlyList<Article> articles, CancellationToken cancellationToken);
}

public sealed class PassThroughArticleProcessor : IArticleProcessor
{
    public Task<IReadOnlyList<Article>> ProcessAsync(IReadOnlyList<Article> articles, CancellationToken cancellationToken) =>
        Task.FromResult(articles);
}
