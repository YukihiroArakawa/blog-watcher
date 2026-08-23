using System.Security.Cryptography;
using System.Text;

namespace BlogWatcher;

public static class ArticleRules
{
    public static bool Matches(Article article, IReadOnlyCollection<string> keywords) =>
        keywords.Any(keyword => article.SearchableText.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new FormatException($"Invalid HTTP(S) article URL: {value}");

        var builder = new UriBuilder(uri) { Scheme = uri.Scheme.ToLowerInvariant(), Host = uri.IdnHost.ToLowerInvariant(), Fragment = "" };
        if ((builder.Scheme == "http" && builder.Port == 80) || (builder.Scheme == "https" && builder.Port == 443)) builder.Port = -1;
        return builder.Uri.AbsoluteUri;
    }

    public static string SeenKey(string normalizedUrl) =>
        "seen:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)));

    public static IReadOnlyList<Article> NormalizeAndDeduplicate(IEnumerable<Article> articles, Action<Article, Exception>? invalid = null)
    {
        var result = new Dictionary<string, Article>(StringComparer.Ordinal);
        foreach (var article in articles)
        {
            try
            {
                var normalized = NormalizeUrl(article.Url);
                result.TryAdd(normalized, article with { Url = normalized });
            }
            catch (FormatException exception) { invalid?.Invoke(article, exception); }
        }
        return result.Values.ToList();
    }
}
