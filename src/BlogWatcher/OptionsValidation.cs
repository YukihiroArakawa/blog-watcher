using Microsoft.Extensions.Options;

namespace BlogWatcher;

public sealed class WatcherOptionsValidator : IValidateOptions<WatcherOptions>
{
    public ValidateOptionsResult Validate(string? name, WatcherOptions options)
    {
        var errors = new List<string>();
        if (options.Sources.Count == 0) errors.Add("At least one source is required.");
        foreach (var duplicate in options.Sources.GroupBy(x => x.Id).Where(x => x.Count() > 1)) errors.Add($"Duplicate source Id: {duplicate.Key}");
        foreach (var source in options.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.Name)) errors.Add("Source Id and Name are required.");
            if (!string.Equals(source.Type, "rss", StringComparison.OrdinalIgnoreCase)) errors.Add($"Unknown source Type '{source.Type}'.");
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) errors.Add($"Invalid source URL: {source.Url}");
            if (source.Keywords.Length == 0 || source.Keywords.Any(string.IsNullOrWhiteSpace)) errors.Add($"Source '{source.Id}' requires non-empty keywords.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public sealed class ExternalOptionsValidator(bool execute) : IValidateOptions<ExternalOptions>
{
    public ValidateOptionsResult Validate(string? name, ExternalOptions options)
    {
        var missing = new List<string>();
        void Require(string value, string key) { if (string.IsNullOrWhiteSpace(value)) missing.Add(key); }
        Require(options.CloudflareAccountId, "CLOUDFLARE_ACCOUNT_ID");
        Require(options.CloudflareNamespaceId, "CLOUDFLARE_KV_NAMESPACE_ID");
        Require(options.CloudflareApiToken, "CLOUDFLARE_API_TOKEN");
        if (execute) { Require(options.GmailAddress, "GMAIL_ADDRESS"); Require(options.GmailAppPassword, "GMAIL_APP_PASSWORD"); }
        return missing.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("Missing environment variables: " + string.Join(", ", missing));
    }
}
