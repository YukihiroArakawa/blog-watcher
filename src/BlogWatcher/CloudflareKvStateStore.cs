using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace BlogWatcher;

public sealed class CloudflareKvStateStore(HttpClient client, IOptions<ExternalOptions> options) : IStateStore
{
    private readonly ExternalOptions settings = options.Value;
    private string BaseUrl => $"client/v4/accounts/{Uri.EscapeDataString(settings.CloudflareAccountId)}/storage/kv/namespaces/{Uri.EscapeDataString(settings.CloudflareNamespaceId)}/values/";

    public Task<bool> IsInitializedAsync(string sourceId, CancellationToken cancellationToken) => ExistsAsync("initialized:" + sourceId, cancellationToken);
    public Task<bool> IsSeenAsync(string key, CancellationToken cancellationToken) => ExistsAsync(key, cancellationToken);

    private async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(BaseUrl + Uri.EscapeDataString(key), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public Task MarkSeenAsync(string key, Article article, CancellationToken cancellationToken) => PutAsync(key, new
    {
        FirstSeenAt = DateTimeOffset.UtcNow,
        article.SourceName,
        article.Url
    }, cancellationToken);

    public Task MarkInitializedAsync(string sourceId, CancellationToken cancellationToken) =>
        PutAsync("initialized:" + sourceId, new { InitializedAt = DateTimeOffset.UtcNow, SourceId = sourceId }, cancellationToken);

    private async Task PutAsync(string key, object value, CancellationToken cancellationToken)
    {
        using var response = await client.PutAsJsonAsync(BaseUrl + Uri.EscapeDataString(key), value, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
