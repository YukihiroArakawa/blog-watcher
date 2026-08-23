using BlogWatcher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly;

var unknownArguments = args.Where(x => x != "--execute").ToArray();
if (unknownArguments.Length > 0) throw new ArgumentException("Unknown arguments: " + string.Join(", ", unknownArguments));
var execute = args.Contains("--execute", StringComparer.Ordinal);

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false, reloadOnChange: false);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["External:CloudflareAccountId"] = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID"),
    ["External:CloudflareNamespaceId"] = Environment.GetEnvironmentVariable("CLOUDFLARE_KV_NAMESPACE_ID"),
    ["External:CloudflareApiToken"] = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN"),
    ["External:GmailAddress"] = Environment.GetEnvironmentVariable("GMAIL_ADDRESS"),
    ["External:GmailAppPassword"] = Environment.GetEnvironmentVariable("GMAIL_APP_PASSWORD")
});
builder.Services.AddSingleton<IValidateOptions<WatcherOptions>, WatcherOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<ExternalOptions>>(new ExternalOptionsValidator(execute));
builder.Services.AddOptions<WatcherOptions>().Bind(builder.Configuration.GetSection("Watcher")).ValidateOnStart();
builder.Services.AddOptions<ExternalOptions>().Bind(builder.Configuration.GetSection("External")).ValidateOnStart();
builder.Services.AddHttpClient<IArticleSource, RssArticleSource>().AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 2;
    options.Retry.Delay = TimeSpan.FromSeconds(1);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IStateStore, CloudflareKvStateStore>((services, client) =>
{
    var settings = services.GetRequiredService<IOptions<ExternalOptions>>().Value;
    client.BaseAddress = new Uri("https://api.cloudflare.com/");
    client.DefaultRequestHeaders.Authorization = new("Bearer", settings.CloudflareApiToken);
}).AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 2;
    options.Retry.Delay = TimeSpan.FromSeconds(1);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddSingleton<IArticleProcessor, PassThroughArticleProcessor>();
builder.Services.AddSingleton<IEmailSender, GmailEmailSender>();
builder.Services.AddTransient<WatcherJob>();

using var host = builder.Build();
await host.StartAsync();
var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
await host.Services.GetRequiredService<WatcherJob>().RunAsync(!execute, stopping);
await host.StopAsync();
