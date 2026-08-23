using System.Text;

namespace BlogWatcher;

public static class NotificationFormatter
{
    public static (string Subject, string Body) Format(Notification notification)
    {
        var count = notification.Articles.Count;
        var subject = $"[blog-watcher] 新着{count}件 - {notification.Date:yyyy-MM-dd}";
        var body = new StringBuilder().AppendLine($"日付: {notification.Date:yyyy-MM-dd} (JST)").AppendLine($"新着件数: {count}").AppendLine();
        if (count == 0) body.AppendLine("新着記事はありません");
        else foreach (var article in notification.Articles)
        {
            body.AppendLine($"監視元: {article.SourceName}").AppendLine($"タイトル: {article.Title}").AppendLine($"URL: {article.Url}");
            if (article.PublishedAt is { } date) body.AppendLine($"公開日時: {date.ToOffset(TimeSpan.FromHours(9)):yyyy-MM-dd HH:mm zzz}");
            body.AppendLine();
        }
        return (subject, body.ToString());
    }
}
