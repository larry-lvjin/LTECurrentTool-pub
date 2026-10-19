using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LTECurrentTool;

public sealed class DshNotificationSettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://127.0.0.1:3080";
    public string BotId { get; set; } = "";
    public string TargetId { get; set; } = "lte-test-report";

    public DshNotificationSettings Copy() => new()
    {
        Enabled = Enabled, BaseUrl = BaseUrl, BotId = BotId, TargetId = TargetId
    };

    public void Normalize()
    {
        BaseUrl = BaseUrl.Trim().TrimEnd('/');
        BotId = BotId.Trim();
        TargetId = TargetId.Trim();
    }

    public Uri GetDeliveryUri()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new ArgumentException("DSH 地址应为 http(s)://主机:端口，例如 http://127.0.0.1:3080。");
        return new Uri(uri, "/api/dsh-im/delivery/messages");
    }

    public void Validate()
    {
        GetDeliveryUri();
        if (string.IsNullOrWhiteSpace(BotId) || string.IsNullOrWhiteSpace(TargetId))
            throw new ArgumentException("请填写 Bot ID 和 Target ID。");
    }
}

public static class DshNotificationSettingsStore
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LTECurrentTool", "dsh_notification.json");

    public static DshNotificationSettings Load(string? path = null)
    {
        path ??= FilePath;
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<DshNotificationSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("通知配置为空。");
        settings.Normalize();
        if (settings.Enabled) settings.Validate();
        return settings;
    }

    public static void Save(DshNotificationSettings settings, string? path = null)
    {
        path ??= FilePath;
        settings.Normalize();
        if (settings.Enabled) settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(temporaryPath, path, overwrite: true);
    }
}

public sealed record DshTestEndNotification(string Outcome, int ResultCount,
    DateTime EndedAt, TimeSpan Duration, string SaveStatus)
{
    public string ToMessage() =>
        $"LTE 电流测试结束\n状态：{Outcome}\n结束时间：{EndedAt:yyyy-MM-dd HH:mm:ss}\n" +
        $"结果：{ResultCount} 个测试点\n耗时：{(int)Duration.TotalHours}小时{Duration.Minutes}分钟{Duration.Seconds}秒\n报告：{SaveStatus}";
}

public static class DshNotifier
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    // 一次请求，不自动重试；超时后无法确定平台是否已接受，避免重复通知。
    public static async Task SendAsync(DshNotificationSettings settings, string text,
        CancellationToken cancellationToken = default, HttpClient? client = null,
        TimeSpan? timeout = null)
    {
        settings = settings.Copy();
        settings.Normalize();
        settings.Validate();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        using var content = new StringContent(JsonSerializer.Serialize(new
        {
            botId = settings.BotId, targetId = settings.TargetId, text
        }), Encoding.UTF8, "application/json");
        using var response = await (client ?? Client).PostAsync(settings.GetDeliveryUri(), content, deadline.Token);
        var body = await response.Content.ReadAsStringAsync(deadline.Token);
        if (!response.IsSuccessStatusCode)
        {
            string? code = null;
            try
            {
                using var error = JsonDocument.Parse(body);
                code = error.RootElement.GetProperty("error").GetProperty("code").GetString();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            throw new HttpRequestException($"DSH 返回 HTTP {(int)response.StatusCode}" +
                (string.IsNullOrEmpty(code) ? "。" : $"（{code}）。"));
        }
        using var result = JsonDocument.Parse(body);
        if (!result.RootElement.TryGetProperty("sent", out var sent) || sent.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("DSH 未返回 sent: true，无法确认平台已接受通知。");
    }
}
