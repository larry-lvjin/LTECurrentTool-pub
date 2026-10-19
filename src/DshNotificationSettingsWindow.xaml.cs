using System.Windows;
using System.Windows.Media;

namespace LTECurrentTool;

public partial class DshNotificationSettingsWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    public DshNotificationSettings? SavedSettings { get; private set; }

    public DshNotificationSettingsWindow(DshNotificationSettings settings)
    {
        InitializeComponent();
        chkEnabled.IsChecked = settings.Enabled;
        txtBaseUrl.Text = settings.BaseUrl;
        txtBotId.Text = settings.BotId;
        txtTargetId.Text = settings.TargetId;
        Closed += (_, _) => lifetime.Cancel();
    }

    private DshNotificationSettings ReadForm()
    {
        var settings = new DshNotificationSettings
        {
            Enabled = chkEnabled.IsChecked == true,
            BaseUrl = txtBaseUrl.Text,
            BotId = txtBotId.Text,
            TargetId = txtTargetId.Text
        };
        settings.Normalize();
        return settings;
    }

    private void SetStatus(string message, bool error = false)
    {
        txtStatus.Text = message;
        txtStatus.Foreground = new SolidColorBrush(error ? Colors.Salmon : Colors.LightBlue);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = ReadForm();
            settings.GetDeliveryUri();
            DshNotificationSettingsStore.Save(settings);
            SavedSettings = settings;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetStatus($"保存失败：{ex.Message}", true);
        }
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        btnTest.IsEnabled = false;
        btnSave.IsEnabled = false;
        SetStatus("正在向 DSH 发送测试通知…");
        try
        {
            await DshNotifier.SendAsync(ReadForm(),
                $"【测试通知】LTECurrentTool 的 DSH 微信通知通道测试。\n时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n这不是实际测试结束通知。",
                lifetime.Token);
            SetStatus("DSH 已接受测试通知，请在手机微信确认。接受不代表已送达或已读。");
        }
        catch (OperationCanceledException)
        {
            if (!lifetime.IsCancellationRequested) SetStatus("发送超时；无法确定是否已接受，请先查看微信，避免重复发送。", true);
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested) SetStatus($"测试通知失败：{ex.Message}", true);
        }
        finally
        {
            if (!lifetime.IsCancellationRequested)
            {
                btnTest.IsEnabled = true;
                btnSave.IsEnabled = true;
            }
        }
    }
}
