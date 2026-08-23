using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BlogWatcher;

public sealed class GmailEmailSender(IOptions<ExternalOptions> options, ILogger<GmailEmailSender> logger) : IEmailSender
{
    private readonly ExternalOptions settings = options.Value;
    public async Task SendAsync(string subject, string body, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(settings.GmailAddress));
                message.To.Add(MailboxAddress.Parse(settings.GmailAddress));
                message.Subject = subject;
                message.Body = new TextPart("plain") { Text = body };
                using var client = new SmtpClient { Timeout = 30_000 };
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, cancellationToken);
                await client.AuthenticateAsync(settings.GmailAddress, settings.GmailAppPassword, cancellationToken);
                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException && attempt < 3)
            {
                last = exception;
                logger.LogWarning(exception, "SMTP attempt failed; retrying. RetryCount={RetryCount}", attempt);
                await Task.Delay(RetryDelay(attempt), cancellationToken);
            }
        }
        throw new InvalidOperationException("SMTP send failed after three attempts.", last);
    }

    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
}
