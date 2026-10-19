using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LTECurrentTool
{
    public partial class MainWindow : Window
    {
        private CMW500Controller? cmwController;
        private PowerSupplyController? powerController;   // 仅"66319D"电源时有值（可控制电源OFF/ON）
        private ICurrentMeter? currentMeter;               // 电流读取设备: 66319D 或 POWER-Z(KM002C/KM003C)
        private const string POWER_SUPPLY_VISA_ADDRESS = "GPIB0::5::INSTR";
        private ObservableCollection<BandConfig> bandConfigs;
        private ObservableCollection<BandConfig> testResults; // 用于显示测试结果
        private CancellationTokenSource? testCancellation;
        private bool isPaused = false;
        private const string EXCEL_PATH = @"D:\MyFile\CODE\VSCODE\LTECurrentTool\测试参数设置.xlsx";
        private string currentCmwIp = "";

        // 记录上一个测试点，用于判断是否需要重新连接
        private BandConfig? lastTestedBand = null;

        // 信道类型选择：ALL（高中低）或 MID（仅中信道）
        private string channelTypeFilter = "ALL";

        // ========== 自动保存配置 ==========
        private const string DEFAULT_AUTO_SAVE_FOLDER = @"D:\LTE-current-testreport";
        // 当前自动保存文件夹（由界面"结果自动保存位置"设置）
        private string AUTO_SAVE_FOLDER => string.IsNullOrWhiteSpace(txtAutoSaveFolder.Text) ? DEFAULT_AUTO_SAVE_FOLDER : txtAutoSaveFolder.Text.Trim();

        // ========== 用户设置（单文件发布时 BaseDirectory 是临时解压目录，所以存到 LocalAppData）==========
        private static readonly string SETTINGS_FOLDER = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LTECurrentTool");
        private static readonly string AT_COM_PORT_FILE = Path.Combine(SETTINGS_FOLDER, "at_com_port.txt");
        private static readonly string AUTO_SAVE_FOLDER_FILE = Path.Combine(SETTINGS_FOLDER, "auto_save_folder.txt");

        // ========== 日志缓冲（修复卡顿）==========
        private readonly System.Text.StringBuilder _logBuffer = new System.Text.StringBuilder();
        // 完整日志缓冲（仅供保存到文件用，UI只显示最近100行）
        private readonly System.Text.StringBuilder _fullLogBuffer = new System.Text.StringBuilder();
        private readonly object _logLock = new object();
        private System.Windows.Threading.DispatcherTimer? _logFlushTimer;
        // UI日志显示行数上限（超过后自动删除最早的行）
        private const int MAX_DISPLAY_LINES = 100;
        private const int TRIM_TO_LINES = 80;

        public MainWindow()
        {
            InitializeComponent();

            // 设置窗口图标
            try
            {
                var iconUri = new Uri("pack://application:,,,/LTECurrentTool.ICO");
                this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch
            {
                // 图标加载失败，忽略错误
            }

            // 加载所有测试配置
            bandConfigs = new ObservableCollection<BandConfig>(LTEBands.LoadFromExcel(EXCEL_PATH));

            // 创建空的测试结果集合
            testResults = new ObservableCollection<BandConfig>();
            dgResults.ItemsSource = testResults;

            // ========== 初始化日志缓冲定时器（修复卡顿）==========
            _logFlushTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)  // 每200ms刷新一次日志
            };
            _logFlushTimer.Tick += (s, e) => FlushLogBuffer();
            _logFlushTimer.Start();
        }

        private DshNotificationSettings dshNotificationSettings = new();

        private void BtnDshNotificationSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DshNotificationSettingsWindow(dshNotificationSettings.Copy()) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SavedSettings is { } saved)
            {
                dshNotificationSettings = saved;
                Log(saved.Enabled ? "DSH 微信通知已启用（下次测试生效）" : "DSH 微信通知已关闭", "#FFADD8E6");
            }
        }

        private async Task NotifyTestEndAsync(DshNotificationSettings settings, DshTestEndNotification notification)
        {
            if (!settings.Enabled) return;
            try
            {
                await DshNotifier.SendAsync(settings, notification.ToMessage());
                Log("DSH 已接受测试结束通知（不代表微信已送达或已读）", "#FF32CD32");
            }
            catch (OperationCanceledException)
            {
                Log("⚠ 测试结束通知超时，无法确定是否已接受；未自动重试", "#FFFF9900");
            }
            catch (Exception ex)
            {
                Log($"⚠ 测试结束通知失败：{ex.Message}（不影响测试结果）", "#FFFF9900");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                dshNotificationSettings = DshNotificationSettingsStore.Load();
            }
            catch (Exception ex)
            {
                dshNotificationSettings = new();
                Log($"⚠ 通知配置读取失败，通知已关闭：{ex.Message}", "#FFFF9900");
            }
            // 读取上次设置的自动保存位置
            try
            {
                if (File.Exists(AUTO_SAVE_FOLDER_FILE))
                {
                    string saved = File.ReadAllText(AUTO_SAVE_FOLDER_FILE).Trim();
                    if (!string.IsNullOrEmpty(saved)) txtAutoSaveFolder.Text = saved;
                }
            }
            catch { }

            // 创建自动保存文件夹
            try
            {
                if (!Directory.Exists(AUTO_SAVE_FOLDER))
                {
                    Directory.CreateDirectory(AUTO_SAVE_FOLDER);
                    Log($"📁 已创建自动保存文件夹: {AUTO_SAVE_FOLDER}", "#FF00F0FF");
                }
            }
            catch (Exception ex)
            {
                Log($"⚠ 创建自动保存文件夹失败: {ex.Message}", "#FFFF9900");
            }

            // 读取上次填写的AT口COM号
            try
            {
                if (File.Exists(AT_COM_PORT_FILE))
                {
                    txtAtComPort.Text = File.ReadAllText(AT_COM_PORT_FILE).Trim();
                }
            }
            catch { }
        }

        private void TxtAtComPort_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // 填写合法数字后立即保存，下次启动默认该值
            string text = txtAtComPort.Text.Trim();
            if (!int.TryParse(text, out int number) || number <= 0) return;
            try
            {
                Directory.CreateDirectory(SETTINGS_FOLDER);
                File.WriteAllText(AT_COM_PORT_FILE, number.ToString());
            }
            catch { }
        }

        private async void BtnConnectCmw_Click(object sender, RoutedEventArgs e)
        {
            if (cmwController != null)
            {
                cmwController.Disconnect();
                cmwController = null;
                currentCmwIp = "";
                cmbCmwIp.Text = "";
                btnConnectCmw.Content = "🔌 连接CMW500";
                txtCmwStatus.Text = "● 未连接";
                txtCmwStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 68, 68));
                Log("CMW500已断开连接", "#FFFF4444");
                return;
            }

            string ipAddress = cmbCmwIp.Text;
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                MessageBox.Show("请输入CMW500 IP地址", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            btnConnectCmw.IsEnabled = false;
            Log($"🔌 正在连接CMW500: {ipAddress}...", "#FF00F0FF");

            try
            {
                cmwController = new CMW500Controller(ipAddress);
                bool connected = await cmwController.ConnectAsync();

                if (connected)
                {
                    currentCmwIp = ipAddress;
                    Dispatcher.Invoke(() =>
                    {
                        cmbCmwIp.Text = ipAddress;
                        btnConnectCmw.Content = "🔌 断开CMW500";
                        txtCmwStatus.Text = "● 已连接";
                        txtCmwStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                    });
                    Log("✓ CMW500连接成功并完成初始化", "#FF32CD32");
                }
                else
                {
                    cmwController = null;
                    MessageBox.Show("连接CMW500失败，请检查IP地址和网络连接", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    Log("✗ CMW500连接失败", "#FFFF4444");
                }
            }
            catch (Exception ex)
            {
                cmwController = null;
                MessageBox.Show($"连接CMW500时发生错误:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"✗ CMW500连接错误: {ex.Message}", "#FFFF4444");
            }
            finally
            {
                btnConnectCmw.IsEnabled = true;
            }
        }

        private async void BtnConnectPower_Click(object sender, RoutedEventArgs e)
        {
            if (currentMeter != null)
            {
                string name = currentMeter.Name;
                currentMeter.Dispose();
                currentMeter = null;
                powerController = null;
                btnConnectPower.Content = "🔌 连接电源";
                txtPowerStatus.Text = "● 未连接";
                txtPowerStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 68, 68));
                cmbPowerSource.IsEnabled = true;
                Log($"{name}已断开连接", "#FFFF4444");
                return;
            }

            // 电源选择"电池": 通过 POWER-Z(KM002C/KM003C) 读取电池供电电流
            if (cmbPowerSource.SelectedIndex == 1)
            {
                await ConnectKm002cAsync();
                return;
            }

            string address = POWER_SUPPLY_VISA_ADDRESS;
            btnConnectPower.IsEnabled = false;

            // 检查程序有效期（静默检查，不显示提示）
            var (isValid, expiryDate, remainingDays) = PowerSupplyController.GetValidityInfo();
            if (!isValid)
            {
                btnConnectPower.IsEnabled = true;
                return;
            }

            Log($"🔌 正在连接66319D电源: {address}...", "#FF00F0FF");
            Log("   (正在通过VISA驱动连接GPIB设备...)", "#FF808080");

            try
            {
                // 使用通道1（固定）
                powerController = new PowerSupplyController(address, channelNumber: 1);
                bool connected = await powerController.ConnectAsync();

                if (connected)
                {
                    currentMeter = powerController;
                    btnConnectPower.Content = "🔌 断开电源";
                    txtPowerStatus.Text = "● 已连接";
                    txtPowerStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                    cmbPowerSource.IsEnabled = false;
                    Log("✓ 66319D电源连接成功", "#FF00FF00");
                    Log($"  • 测量通道: 通道{LTECurrentRules.POWER_SUPPLY_CHANNEL} (Channel {LTECurrentRules.POWER_SUPPLY_CHANNEL})", "#FFFF9900");
                    Log($"  • 通道{LTECurrentRules.POWER_SUPPLY_CHANNEL} 电压: 保持人工设置值(未修改), 电流限制: {LTECurrentRules.POWER_SUPPLY_CURRENT_LIMIT}A", "#FF808080");
                    Log("  • 电源输出已开启 (OUTP ON)", "#FF808080");

                    // 测试单次电流采样
                    double testCurrent = await powerController.MeasureCurrentAsync();
                    Log($"  • 当前电流读数: {testCurrent:F2} mA (使用 MEAS:CURR?)", "#FF808080");

                    if (testCurrent < 1.0 && testCurrent > -1.0)
                    {
                        Log("  ⚠ 警告: 电流读数接近0，请检查：", "#FFFF9900");
                        Log("    1. UE设备是否已连接到66319D通道1", "#FFFF9900");
                        Log("    2. UE设备是否已开机", "#FFFF9900");
                        Log("    3. 66319D前面板是否显示输出电流", "#FFFF9900");
                    }
                }
                else
                {
                    string lastError = powerController.LastError;
                    powerController.Dispose();
                    powerController = null;
                    MessageBox.Show($"连接66319D电源失败\n请检查：\n1. NI-VISA 或 Keysight IO Libraries 已安装\n2. GPIB接口卡或适配器已连接\n3. 电源已开机\n4. GPIB地址正确(默认5)\n\n详细错误: {lastError}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    Log($"✗ 66319D电源连接失败: {lastError}", "#FFFF4444");
                }
            }
            catch (Exception ex)
            {
                powerController = null;
                MessageBox.Show($"连接66319D电源时发生错误:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"✗ 66319D电源连接错误: {ex.Message}", "#FFFF4444");
            }
            finally
            {
                btnConnectPower.IsEnabled = true;
            }
        }

        private async Task ConnectKm002cAsync()
        {
            btnConnectPower.IsEnabled = false;
            Log("🔌 正在连接 POWER-Z KM002C/KM003C (电池供电)...", "#FF00F0FF");

            try
            {
                var km002c = new Km002cController();
                if (!await km002c.ConnectAsync())
                {
                    string lastError = km002c.LastError;
                    km002c.Dispose();
                    MessageBox.Show($"连接POWER-Z(KM002C/KM003C)失败\n请检查：\n1. 表侧面HID口已用USB线连接电脑\n2. 设备管理器中有表的COM口\n3. POWER-Z上位机软件已关闭（会占用串口）\n\n详细错误: {lastError}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    Log($"✗ POWER-Z连接失败: {lastError}", "#FFFF4444");
                    return;
                }

                currentMeter = km002c;
                btnConnectPower.Content = "🔌 断开电源";
                txtPowerStatus.Text = "● 已连接";
                txtPowerStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                cmbPowerSource.IsEnabled = false;

                double testCurrent = await km002c.MeasureCurrentAsync();
                Log($"✓ {km002c.Name}连接成功 ({km002c.PortName})", "#FF00FF00");
                Log($"  • 电池电压: {km002c.LastVoltage:F4} V, 当前电流: {testCurrent:F2} mA", "#FF808080");
                Log("  • 电池供电无法控制电源OFF/ON，RRC未连接时请使用「AT控制」恢复", "#FFFF9900");

                if (Math.Abs(testCurrent) < 1.0)
                {
                    Log("  ⚠ 警告: 电流读数接近0，请检查样机是否开机、电流是否经过POWER-Z", "#FFFF9900");
                }
            }
            catch (Exception ex)
            {
                currentMeter = null;
                MessageBox.Show($"连接POWER-Z时发生错误:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"✗ POWER-Z连接错误: {ex.Message}", "#FFFF4444");
            }
            finally
            {
                btnConnectPower.IsEnabled = true;
            }
        }

        private void TxtAutoSaveFolder_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveAutoSaveFolderSetting();
        }

        private void BtnAutoSaveFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择测试结果自动保存位置"
            };
            if (Directory.Exists(AUTO_SAVE_FOLDER))
            {
                dialog.InitialDirectory = AUTO_SAVE_FOLDER;
            }

            if (dialog.ShowDialog() == true)
            {
                txtAutoSaveFolder.Text = dialog.FolderName;
                SaveAutoSaveFolderSetting();
                Log($"📁 结果自动保存位置已设置为: {dialog.FolderName}", "#FF00F0FF");
            }
        }

        private void SaveAutoSaveFolderSetting()
        {
            // 保存自动保存位置，下次启动默认该值
            try
            {
                Directory.CreateDirectory(SETTINGS_FOLDER);
                File.WriteAllText(AUTO_SAVE_FOLDER_FILE, AUTO_SAVE_FOLDER);
            }
            catch { }
        }

        private async void BtnStartAllChannelTest_Click(object sender, RoutedEventArgs e)
        {
            // 设置信道过滤器为 ALL（高中低信道）
            channelTypeFilter = "ALL";
            Log("✓ 已选择：高中低信道测试（Low, Mid, High）", "#FF32CD32");

            // 调用通用的测试启动逻辑
            await StartTestAsync();
        }

        private async void BtnStartMidChannelTest_Click(object sender, RoutedEventArgs e)
        {
            // 设置信道过滤器为 MID（仅中信道）
            channelTypeFilter = "MID";
            Log("✓ 已选择：中信道测试（仅Mid）", "#FF32CD32");

            // 调用通用的测试启动逻辑
            await StartTestAsync();
        }

        private async Task StartTestAsync()
        {
            if (cmwController == null || currentMeter == null)
            {
                MessageBox.Show("请先连接CMW500和电源", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 每轮快照：通知等待期间可开始下一轮，不读取新一轮的可变结果或配置。
            var notificationSettings = dshNotificationSettings.Copy();
            var runTimer = System.Diagnostics.Stopwatch.StartNew();
            string notificationOutcome = "测试异常结束";
            string notificationSaveStatus = "无测试结果可保存";

            btnStartAllChannelTest.IsEnabled = false;
            btnStartMidChannelTest.IsEnabled = false;
            btnPauseTest.IsEnabled = true;
            btnPauseTest.Content = "⏸ 暂停测试";
            btnStopTest.IsEnabled = true;
            isPaused = false;
            testCancellation = new CancellationTokenSource();

            // ========== 自动保存上次测试结果（防止数据丢失）==========
            if (testResults.Count > 0)
            {
                string prevTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string prevExcelPath = Path.Combine(AUTO_SAVE_FOLDER, $"上次测试结果_{prevTimestamp}.xlsx");
                string prevLogPath = Path.Combine(AUTO_SAVE_FOLDER, $"上次系统日志_{prevTimestamp}.txt");

                try
                {
                    if (!Directory.Exists(AUTO_SAVE_FOLDER))
                    {
                        Directory.CreateDirectory(AUTO_SAVE_FOLDER);
                    }
                    ExportExcelToFile(prevExcelPath);
                    SaveLogToFile(prevLogPath);
                    Log($"📁 已自动保存上次测试结果 ({testResults.Count}条) 到: {AUTO_SAVE_FOLDER}", "#FF32CD32");
                }
                catch (Exception ex)
                {
                    Log($"⚠ 保存上次结果失败: {ex.Message}", "#FFFF9900");
                }
            }

            // 清空之前的测试结果
            testResults.Clear();

            // 根据勾选的TPC类型和ULRB过滤测试配置
            var filteredConfigs = FilterTestConfigs();

            Log($"开始测试，共 {filteredConfigs.Count} 个配置", "#FF32CD32");

            // ========== 修复：在后台线程运行测试循环（避免UI卡顿）==========
            try
            {
                await Task.Run(async () =>
                {
                    foreach (var band in filteredConfigs)
                    {
                        // 检查暂停状态
                        while (isPaused && !testCancellation.Token.IsCancellationRequested)
                        {
                            await Task.Delay(LTECurrentRules.MEASUREMENT_WAIT_MS);
                        }

                        if (testCancellation.Token.IsCancellationRequested)
                        {
                            Log("⏹ 测试已停止", "#FFFF4444");
                            break;
                        }

                        await TestBandAsync(band, testCancellation.Token);
                    }
                }, testCancellation.Token);

                if (testCancellation.Token.IsCancellationRequested)
                {
                    notificationOutcome = "⏹ 测试已停止";
                    Dispatcher.Invoke(() =>
                    {
                        txtTestProgress.Text = "⏹ 测试已停止";
                        txtTestProgress.Foreground = new SolidColorBrush(Color.FromRgb(255, 99, 71));
                    });
                }
                else
                {
                    notificationOutcome = "✅ 测试完成（不等于所有测试点合格）";
                    Log("测试完成", "#FF00FF00");

                    Dispatcher.Invoke(() =>
                    {
                        txtTestProgress.Text = "✓ 测试完成";
                        txtTestProgress.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                    });
                }
            }
            catch (OperationCanceledException)
            {
                notificationOutcome = "⏹ 测试已取消";
                Log("测试已取消", "#FFFF9900");
            }
            catch (Exception ex)
            {
                notificationOutcome = "❌ 测试异常结束";
                Log($"测试错误: {ex.Message}", "#FFFF4444");
            }
            finally
            {
                // 自动保存测试结果和日志（无论测试如何结束都执行）
                if (testResults.Count > 0)
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string excelPath = Path.Combine(AUTO_SAVE_FOLDER, $"测试结果_{timestamp}.xlsx");
                    string logPath = Path.Combine(AUTO_SAVE_FOLDER, $"系统日志_{timestamp}.txt");

                    try
                    {
                        // 确保 AutoSave 文件夹存在（防止启动时未创建）
                        if (!Directory.Exists(AUTO_SAVE_FOLDER))
                        {
                            Directory.CreateDirectory(AUTO_SAVE_FOLDER);
                        }

                        ExportExcelToFile(excelPath);
                        SaveLogToFile(logPath);
                        notificationSaveStatus = "Excel 和日志已自动保存";
                        Log($"📁 已自动保存 ({testResults.Count}条结果) 到:", "#FF32CD32");
                        Log($"  {AUTO_SAVE_FOLDER}", "#FF32CD32");
                        Log($"  • Excel: 测试结果_{timestamp}.xlsx", "#FFADD8E6");
                        Log($"  • 日志: 系统日志_{timestamp}.txt", "#FFADD8E6");
                    }
                    catch (Exception ex)
                    {
                        notificationSaveStatus = "自动保存失败，请检查工具日志";
                        Log($"⚠ 自动保存失败: {ex.Message}", "#FFFF9900");
                        Log($"  路径: {AUTO_SAVE_FOLDER}", "#FFFF9900");
                    }
                }
                else
                {
                    Log("ℹ 无测试结果可保存（testResults为空）", "#FF808080");
                }

                runTimer.Stop();
                var notification = new DshTestEndNotification(notificationOutcome, testResults.Count,
                    DateTime.Now, runTimer.Elapsed, notificationSaveStatus);

                // 恢复按钮状态
                Dispatcher.Invoke(() =>
                {
                    btnStartAllChannelTest.IsEnabled = true;
                    btnStartMidChannelTest.IsEnabled = true;
                    btnPauseTest.IsEnabled = false;
                    btnPauseTest.Content = "⏸ 暂停测试";
                    btnStopTest.IsEnabled = false;
                });
                isPaused = false;
                await NotifyTestEndAsync(notificationSettings, notification);
            }
        }

        private async Task TestBandAsync(BandConfig band, CancellationToken cancellationToken)
        {
            var testStartTime = System.Diagnostics.Stopwatch.StartNew();  // 添加性能监控

            // 失败的测试点也加入结果列表（电流为0，状态写明原因），避免漏看
            void AddFailedResult(string status)
            {
                band.Status = status;
                Dispatcher.Invoke(() =>
                {
                    if (!testResults.Contains(band))
                        testResults.Add(band);
                });
            }

            try
            {
                Dispatcher.Invoke(() =>
                {
                    txtTestProgress.Text = $"⏱ 正在测试 {band.DisplayName} (Ch:{band.Channel})";
                    txtTestProgress.Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255));
                });

                // 显示测试信息
                string testGroup = band.UplinkRB == "12" ? "UL12组" : "UL50组";
                Log($"▶ 测试: {band.DisplayName}, 信道:{band.Channel} [{testGroup}]", "#FFFFFF00");
                Log($"  • UPLINK RB: {band.UplinkRB}", "#FF808080");
                Log($"  • ACTIVE TPC SETUP: {band.TPCSetup}", "#FF808080");
                Log($"  • CLOSED LOOP TARGET POWER: {band.TargetPower}", "#FF808080");

                // 设置CMW500参数（模式/TPC切换或首点时会自动CELL OFF→配置→CELL ON并等待小区稳定）
                var (setSuccess, tpcVerify, powerVerify) = await cmwController!.SetBandAndChannelAsync(
                    band.BandName,
                    band.Channel,
                    band.DownlinkRB,
                    band.UplinkRB,
                    band.TPCSetup,
                    band.TargetPower
                );

                if (!setSuccess)
                {
                    Log($"  ✗ 设置失败，跳过", "#FFFF4444");
                    AddFailedResult("✗ 设置失败");
                    return;
                }

                // 等待RRC状态为CONNECTED（根据LTE电流规则.md）
                Log($"  ⏱ 等待RRC STAT:CONNECTED...", "#FF00F0FF");
                bool isConnected = await cmwController!.WaitForConnectionAsync(LTECurrentRules.CMW_CONNECTION_TIMEOUT_SECONDS);

                if (!isConnected)
                {
                    Log($"  ✗ RRC未连接，超时", "#FFFF4444");

                    // 按界面选择的恢复方式（电源控制 / AT控制）反复恢复，直到RRC连接
                    isConnected = await RecoverConnectionAsync(cancellationToken);
                    if (!isConnected)
                    {
                        Log($"  ✗ 无法恢复连接，跳过此测试点", "#FFFF4444");
                        AddFailedResult("✗ RRC未连接");
                        return;
                    }
                }

                Log($"  ✓ RRC STAT:CONNECTED，启动BLER持续模式...", "#FF32CD32");

                // 启动BLER Continuous Mode确保UE持续发射PUSCH（避免进入DRX休眠导致RRC断开）
                var (blerStarted, blerDetail) = await cmwController!.StartBLERContinuousModeAsync();
                if (blerStarted)
                {
                    Log($"  ✓ BLER Continuous Mode已启动 ({blerDetail})，等待3秒TPC收敛...", "#FF32CD32");
                }
                else
                {
                    Log($"  ⚠ BLER未真正生效: {blerDetail}", "#FFFF9900");
                    Log($"     → UE可能未持续发射PUSCH，电流会明显偏低(约1/3)，请检查CMW500固件/连接", "#FFFF9900");
                }

                // RRC CONNECTED后等待3秒（实测证明3秒足够TPC收敛，无需探针）
                await Task.Delay(LTECurrentRules.MEASUREMENT_WAIT_MS, cancellationToken);

                // 电流测量（MEAS:CURR? 多次积分平均）
                Log($"  ⏱ 开始电流测量 ({band.TPCSetup}):", "#FF00F0FF");
                Log($"    • 方式: MEAS:CURR? 多次积分平均 (干净, 无负值/纹波伪值)", "#FFADD8E6");
                Log($"    • 预计耗时: ~10秒", "#FFADD8E6");

                async Task<CurrentMeasurement> MeasureWithReconnectAsync()
                {
                    try
                    {
                        return await currentMeter!.MeasureAdaptiveCurrentAsync(band.TPCSetup);
                    }
                    catch (Exception ex)
                    {
                        // 会话丢失后句柄失效，不重连则后续所有测试点都会立即失败
                        Log($"  ⚠ {currentMeter!.Name}通信中断: {ex.Message}，正在重新连接...", "#FFFF9900");
                        if (!await currentMeter.ReconnectAsync())
                        {
                            Log($"  ✗ {currentMeter.Name}重连失败: {currentMeter.LastError}，请检查连接线", "#FFFF4444");
                            throw;
                        }
                        Log($"  ✓ {currentMeter.Name}已重新连接，重新测量...", "#FF32CD32");
                        return await currentMeter!.MeasureAdaptiveCurrentAsync(band.TPCSetup);
                    }
                }

                CurrentMeasurement measurement = await MeasureWithReconnectAsync();

                // 最低电流检查：低于要求判定样机掉线，重启电源重新建立连接后重测
                double? minCurrent = MinCurrentRequirements.Get(band.BandName, band.ChannelType, band.UplinkRB, band.TPCSetup, band.TargetPower);
                int lowCurrentRetry = 0;
                while (measurement.IsValid && minCurrent.HasValue && measurement.Mean < minCurrent.Value
                       && lowCurrentRetry < LTECurrentRules.MIN_CURRENT_MAX_RETRIES)
                {
                    lowCurrentRetry++;
                    Log($"  ⚠ 平均电流 {measurement.Mean:F2} mA 低于最低要求 {minCurrent.Value:F2} mA，重启样机重测 ({lowCurrentRetry}/{LTECurrentRules.MIN_CURRENT_MAX_RETRIES})...", "#FFFF9900");

                    // 按界面选择的恢复方式重启样机（电源控制 / AT控制 严格分离）
                    bool restarted = await RestartUeAsync();
                    if (!restarted)
                    {
                        Log($"  ✗ 样机重启失败，停止重测", "#FFFF4444");
                        break;
                    }
                    await Task.Delay(15000, cancellationToken);

                    if (!await cmwController!.WaitForConnectionAsync(LTECurrentRules.CMW_CONNECTION_TIMEOUT_SECONDS))
                    {
                        Log($"  ✗ 重启样机后RRC未连接，停止重测", "#FFFF4444");
                        break;
                    }
                    Log($"  ✓ 重启样机后连接成功，启动BLER持续模式...", "#FF32CD32");

                    var (retryBlerStarted, retryBlerDetail) = await cmwController!.StartBLERContinuousModeAsync();
                    if (!retryBlerStarted)
                    {
                        Log($"  ⚠ BLER未真正生效: {retryBlerDetail}", "#FFFF9900");
                    }
                    await Task.Delay(LTECurrentRules.MEASUREMENT_WAIT_MS, cancellationToken);

                    Log($"  ⏱ 重新测量电流...", "#FF00F0FF");
                    measurement = await MeasureWithReconnectAsync();
                }

                if (!measurement.IsValid)
                {
                    Log($"  ✗ 采集失败", "#FFFF4444");
                    AddFailedResult("✗ 采集失败");
                    return;
                }

                // 显示简化统计结果
                Log($"  ✓ 采集完成 ({measurement.TotalSamples}次积分读数):", "#FF32CD32");
                Log($"    • 平均电流: {measurement.Mean:F2} mA", "#FF00FF00");
                Log($"    • 最大/最小: {measurement.Max:F2} / {measurement.Min:F2} mA", "#FFADD8E6");
                if (currentMeter is Km002cController km002c)
                {
                    Log($"    • 电池电压: {km002c.LastVoltage:F4} V", "#FFADD8E6");
                }

                // 保存统计到BandConfig
                band.MeasuredCurrent = measurement.Mean;
                band.MaxCurrent = measurement.Max;
                band.MinCurrent = measurement.Min;
                band.TotalSamples = measurement.TotalSamples;
                band.Status = measurement.Status;

                if (minCurrent.HasValue && measurement.Mean < minCurrent.Value)
                {
                    Log($"  ✗ 重测后平均电流仍低于最低要求 {minCurrent.Value:F2} mA，标记为失败", "#FFFF4444");
                    band.Status = "✗ 低于下限";
                }

                // 停止BLER（测试点完成后）
                await cmwController!.StopBLERAsync();

                // 添加到结果列表
                Dispatcher.Invoke(() => testResults.Add(band));

                // 测试完成后查询CMW500错误队列
                if (cmwController != null)
                lastTestedBand = band;

                // 性能监控：记录单点总耗时
                testStartTime.Stop();
                Log($"  ⏱ 单点耗时: {testStartTime.ElapsedMilliseconds / 1000.0:F1}秒", "#FF808080");
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    Log($"  ⏹ {band.DisplayName} 测试已中止", "#FFFF9900");
                }
                else
                {
                    Log($"  ✗ {band.DisplayName} 测试错误: {ex.Message}，跳过", "#FFFF4444");
                    AddFailedResult("✗ 测试错误");
                }
            }
        }


        private void BtnPauseTest_Click(object sender, RoutedEventArgs e)
        {
            isPaused = !isPaused;
            if (isPaused)
            {
                btnPauseTest.Content = "▶ 继续测试";
                Log("⏸ 测试已暂停", "#FFFFFF00");
                txtTestProgress.Text = "⏸ 已暂停";
                txtTestProgress.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0));
            }
            else
            {
                btnPauseTest.Content = "⏸ 暂停测试";
                Log("▶ 测试已继续", "#FF32CD32");
                txtTestProgress.Text = "⏱ 测试中...";
                txtTestProgress.Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255));
            }
        }


        private void BtnStopTest_Click(object sender, RoutedEventArgs e)
        {
            if (testCancellation == null || testCancellation.IsCancellationRequested) return;

            var result = MessageBox.Show("确定要停止当前测试吗？已完成的结果会自动保存。", "停止测试",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            btnStopTest.IsEnabled = false;
            btnPauseTest.IsEnabled = false;
            isPaused = false;
            testCancellation.Cancel();
            Log("⏹ 正在停止测试，等待当前测试点结束...", "#FFFF4444");
            txtTestProgress.Text = "⏹ 正在停止...";
            txtTestProgress.Foreground = new SolidColorBrush(Color.FromRgb(255, 99, 71));
        }

        /// <summary>
        /// 导出Excel到指定路径
        /// </summary>
        private void ExportExcelToFile(string filepath)
        {
            // 使用EPPlus导出Excel
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("测试结果");

                // 表头（简化版 - 11列）
                worksheet.Cells[1, 1].Value = "BAND";
                worksheet.Cells[1, 2].Value = "信道类型";
                worksheet.Cells[1, 3].Value = "信道(EARFCN)";
                worksheet.Cells[1, 4].Value = "TPC Setup";
                worksheet.Cells[1, 5].Value = "Target Power";
                worksheet.Cells[1, 6].Value = "上行RB";
                worksheet.Cells[1, 7].Value = "平均(mA)";
                worksheet.Cells[1, 8].Value = "最大(mA)";
                worksheet.Cells[1, 9].Value = "最小(mA)";
                worksheet.Cells[1, 10].Value = "样本数";
                worksheet.Cells[1, 11].Value = "状态";

                // 表头样式
                using (var range = worksheet.Cells[1, 1, 1, 11])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(58, 58, 58));
                    range.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(65, 105, 225));
                }

                // 数据行（只导出实际测试的结果）
                int row = 2;
                foreach (var band in testResults)
                {
                    worksheet.Cells[row, 1].Value = band.BandName;
                    worksheet.Cells[row, 2].Value = band.ChannelType;
                    worksheet.Cells[row, 3].Value = band.Channel;
                    worksheet.Cells[row, 4].Value = band.TPCSetup;
                    worksheet.Cells[row, 5].Value = band.TargetPower;
                    worksheet.Cells[row, 6].Value = band.UplinkRB;
                    worksheet.Cells[row, 7].Value = band.MeasuredCurrent;
                    worksheet.Cells[row, 8].Value = band.MaxCurrent;
                    worksheet.Cells[row, 9].Value = band.MinCurrent;
                    worksheet.Cells[row, 10].Value = band.TotalSamples;
                    worksheet.Cells[row, 11].Value = band.Status;

                    row++;
                }

                // 设置固定列宽（避免AutoFitColumns在大量数据时的O(n)性能问题）
                worksheet.Column(1).Width = 8;    // BAND
                worksheet.Column(2).Width = 10;   // 信道类型
                worksheet.Column(3).Width = 14;   // 信道(EARFCN)
                worksheet.Column(4).Width = 14;   // TPC Setup
                worksheet.Column(5).Width = 14;   // Target Power
                worksheet.Column(6).Width = 10;   // 上行RB
                worksheet.Column(7).Width = 12;   // 平均(mA)
                worksheet.Column(8).Width = 12;   // 最大(mA)
                worksheet.Column(9).Width = 12;   // 最小(mA)
                worksheet.Column(10).Width = 10;  // 样本数
                worksheet.Column(11).Width = 12;  // 状态

                // 保存文件
                FileInfo file = new FileInfo(filepath);
                package.SaveAs(file);
            }
        }

        /// <summary>
        /// 保存系统日志到指定路径（使用完整日志缓冲区，包含所有历史记录）
        /// </summary>
        private void SaveLogToFile(string filepath)
        {
            string fullLog;
            lock (_logLock)
            {
                fullLog = _fullLogBuffer.ToString();
            }
            File.WriteAllText(filepath, fullLog, Encoding.UTF8);
        }

        private async void BtnExportResults_Click(object sender, RoutedEventArgs e)
        {
            // 创建保存文件对话框
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Title = "导出测试结果",
                Filter = "Excel文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
                FilterIndex = 1,
                FileName = $"LTE电流测试结果_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                DefaultExt = "xlsx",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            // 显示对话框，如果用户取消则退出
            if (saveFileDialog.ShowDialog() != true)
            {
                Log("✗ 用户取消导出", "#FFFF9900");
                return;
            }

            string filepath = saveFileDialog.FileName;
            int rowCount = testResults.Count;

            // 禁用导出按钮，避免重复点击
            btnExportResults.IsEnabled = false;
            Log($"💾 正在导出 {rowCount} 条结果到Excel... (后台执行，UI不会卡顿)", "#FF00F0FF");

            try
            {
                // 在后台线程执行Excel生成（避免UI卡死）
                // 拷贝数据到本地数组，避免后台线程访问UI绑定集合
                var snapshot = testResults.ToArray();

                await Task.Run(() => ExportExcelToFileFromSnapshot(filepath, snapshot));

                Log($"✓ 导出完成: {filepath}", "#FF00FF00");
                MessageBox.Show($"测试结果已导出到:\n{filepath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"✗ 导出结果失败: {ex.Message}", "#FFFF4444");
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnExportResults.IsEnabled = true;
            }
        }

        /// <summary>
        /// 从快照数组导出Excel（线程安全，可在后台线程调用）
        /// </summary>
        private void ExportExcelToFileFromSnapshot(string filepath, BandConfig[] snapshot)
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("测试结果");

                // 表头
                worksheet.Cells[1, 1].Value = "BAND";
                worksheet.Cells[1, 2].Value = "信道类型";
                worksheet.Cells[1, 3].Value = "信道(EARFCN)";
                worksheet.Cells[1, 4].Value = "TPC Setup";
                worksheet.Cells[1, 5].Value = "Target Power";
                worksheet.Cells[1, 6].Value = "上行RB";
                worksheet.Cells[1, 7].Value = "平均(mA)";
                worksheet.Cells[1, 8].Value = "最大(mA)";
                worksheet.Cells[1, 9].Value = "最小(mA)";
                worksheet.Cells[1, 10].Value = "样本数";
                worksheet.Cells[1, 11].Value = "状态";

                using (var range = worksheet.Cells[1, 1, 1, 11])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(58, 58, 58));
                    range.Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(65, 105, 225));
                }

                // 数据行
                int row = 2;
                foreach (var band in snapshot)
                {
                    worksheet.Cells[row, 1].Value = band.BandName;
                    worksheet.Cells[row, 2].Value = band.ChannelType;
                    worksheet.Cells[row, 3].Value = band.Channel;
                    worksheet.Cells[row, 4].Value = band.TPCSetup;
                    worksheet.Cells[row, 5].Value = band.TargetPower;
                    worksheet.Cells[row, 6].Value = band.UplinkRB;
                    worksheet.Cells[row, 7].Value = band.MeasuredCurrent;
                    worksheet.Cells[row, 8].Value = band.MaxCurrent;
                    worksheet.Cells[row, 9].Value = band.MinCurrent;
                    worksheet.Cells[row, 10].Value = band.TotalSamples;
                    worksheet.Cells[row, 11].Value = band.Status;
                    row++;
                }

                // 固定列宽
                worksheet.Column(1).Width = 8;
                worksheet.Column(2).Width = 10;
                worksheet.Column(3).Width = 14;
                worksheet.Column(4).Width = 14;
                worksheet.Column(5).Width = 14;
                worksheet.Column(6).Width = 10;
                worksheet.Column(7).Width = 12;
                worksheet.Column(8).Width = 12;
                worksheet.Column(9).Width = 12;
                worksheet.Column(10).Width = 10;
                worksheet.Column(11).Width = 12;

                FileInfo file = new FileInfo(filepath);
                package.SaveAs(file);
            }
        }

        private List<BandConfig> FilterTestConfigs()
        {
            var filtered = new List<BandConfig>();

            // 获取勾选的TPC类型
            var selectedTpcs = new List<string>();
            if (chkTpcMin.IsChecked == true) selectedTpcs.Add("Min Power");
            if (chkTpc0.IsChecked == true) selectedTpcs.Add("0dBm");
            if (chkTpc10.IsChecked == true) selectedTpcs.Add("10dBm");
            if (chkTpc15.IsChecked == true) selectedTpcs.Add("15dBm");
            if (chkTpcMax.IsChecked == true) selectedTpcs.Add("Max Power");

            // 获取勾选的ULRB
            var selectedUlrbs = new List<string>();
            if (chkUlrb12.IsChecked == true) selectedUlrbs.Add("12");
            if (chkUlrb50.IsChecked == true) selectedUlrbs.Add("50");

            // 获取勾选的BAND
            var selectedBands = new List<string>();
            if (chkB1.IsChecked == true) selectedBands.Add("B1");
            if (chkB2.IsChecked == true) selectedBands.Add("B2");
            if (chkB3.IsChecked == true) selectedBands.Add("B3");
            if (chkB4.IsChecked == true) selectedBands.Add("B4");
            if (chkB5.IsChecked == true) selectedBands.Add("B5");
            if (chkB7.IsChecked == true) selectedBands.Add("B7");
            if (chkB8.IsChecked == true) selectedBands.Add("B8");
            if (chkB12.IsChecked == true) selectedBands.Add("B12");
            if (chkB13.IsChecked == true) selectedBands.Add("B13");
            if (chkB17.IsChecked == true) selectedBands.Add("B17");
            if (chkB18.IsChecked == true) selectedBands.Add("B18");
            if (chkB19.IsChecked == true) selectedBands.Add("B19");
            if (chkB20.IsChecked == true) selectedBands.Add("B20");
            if (chkB25.IsChecked == true) selectedBands.Add("B25");
            if (chkB26.IsChecked == true) selectedBands.Add("B26");
            if (chkB28.IsChecked == true) selectedBands.Add("B28");
            if (chkB66.IsChecked == true) selectedBands.Add("B66");
            if (chkB34.IsChecked == true) selectedBands.Add("B34");
            if (chkB38.IsChecked == true) selectedBands.Add("B38");
            if (chkB39.IsChecked == true) selectedBands.Add("B39");
            if (chkB40.IsChecked == true) selectedBands.Add("B40");
            if (chkB41.IsChecked == true) selectedBands.Add("B41");

            // 过滤配置
            foreach (var band in bandConfigs)
            {
                // 应用TPC、ULRB、BAND过滤
                if (selectedTpcs.Contains(band.TestType) &&
                    selectedUlrbs.Contains(band.UplinkRB) &&
                    selectedBands.Contains(band.BandName))
                {
                    // 应用信道类型过滤
                    if (channelTypeFilter == "ALL" || band.ChannelType == "Mid")
                    {
                        filtered.Add(band);
                    }
                }
            }

            return filtered;
        }


        /// <summary>
        /// 日志方法（异步缓冲，避免UI卡顿）
        /// UI仅显示最近100行，完整日志保存在 _fullLogBuffer 中（用于自动保存到文件）
        /// </summary>
        private void Log(string message, string colorHex = "#FF00F0FF")
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logLine = $"[{timestamp}] {message}\n";

            lock (_logLock)
            {
                _fullLogBuffer.Append(logLine);  // 完整日志（保存用）
                _logBuffer.Append(logLine);

                // 如果缓冲区超过10KB，立即刷新（避免内存累积）
                if (_logBuffer.Length > 10240)
                {
                    var content = _logBuffer.ToString();
                    _logBuffer.Clear();

                    // 异步刷新到UI（不阻塞）
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        AppendAndTrimLog(content);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// 定时刷新日志缓冲区到UI（200ms一次）
        /// </summary>
        private void FlushLogBuffer()
        {
            string content;
            lock (_logLock)
            {
                if (_logBuffer.Length == 0) return;
                content = _logBuffer.ToString();
                _logBuffer.Clear();
            }

            AppendAndTrimLog(content);
        }

        /// <summary>
        /// 追加日志到 txtLog，超过100行时删除最早的部分（避免UI卡顿）
        /// </summary>
        private void AppendAndTrimLog(string content)
        {
            txtLog.AppendText(content);

            // 检查行数，超过上限则截断
            if (txtLog.LineCount > MAX_DISPLAY_LINES)
            {
                int linesToRemove = txtLog.LineCount - TRIM_TO_LINES;
                int startIndex = txtLog.GetCharacterIndexFromLineIndex(linesToRemove);
                if (startIndex > 0 && startIndex < txtLog.Text.Length)
                {
                    txtLog.Text = txtLog.Text.Substring(startIndex);
                }
            }

            txtLog.ScrollToEnd();
        }

        /// <summary>
        /// 异常诊断：检测瞬态尖峰、掉发等异常
        /// </summary>

        /// <summary>
        /// RRC未连接时按界面选择的方式恢复（电源控制: 66319D OFF/ON；AT控制: AT+CFUN=0/1），
        /// 每次操作后等15秒检查RRC，直到连接成功
        /// </summary>
        private async Task<bool> RecoverConnectionAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log($"  🔄 第{attempt}次 恢复连接...", "#FFFF9900");
                if (!await RestartUeAsync())
                {
                    return false;
                }
                Log($"  ⏱ 等待15秒后检查RRC状态...", "#FF00F0FF");

                await Task.Delay(15000, cancellationToken);

                if (await cmwController!.WaitForConnectionAsync(LTECurrentRules.CMW_CONNECTION_TIMEOUT_SECONDS))
                {
                    Log($"  ✓ 第{attempt}次恢复后RRC连接成功", "#FF32CD32");
                    return true;
                }
                Log($"  ✗ 第{attempt}次恢复后RRC仍未连接，继续恢复...", "#FFFF4444");
            }
        }

        private bool IsAtRecoveryMode => Dispatcher.Invoke(() => cmbRecoveryMode.SelectedIndex == 1);

        /// <summary>
        /// 按界面选择的恢复方式重启样机，两种方式严格分离：
        /// 电源控制: 只做 66319D 电源 OFF/ON；AT控制: 只发 AT+CFUN=0/1，绝不操作电源
        /// </summary>
        private async Task<bool> RestartUeAsync()
        {
            if (IsAtRecoveryMode)
            {
                string comNumber = Dispatcher.Invoke(() => txtAtComPort.Text.Trim());
                Log($"  🔄 AT控制重启射频 (COM{comNumber})...", "#FFFF9900");
                if (!await SendCfunCycleAsync(comNumber))
                {
                    return false;
                }
                Log($"  ✓ AT+CFUN已重启射频", "#FF32CD32");
                return true;
            }

            if (powerController == null)
            {
                Log($"  ✗ 电池供电无法控制电源OFF/ON，请把恢复方式改为「AT控制」", "#FFFF4444");
                return false;
            }

            Log($"  🔄 电源控制重启样机 (OFF/ON)...", "#FFFF9900");
            if (!await RestartPowerSupplyAsync())
            {
                Log($"  ✗ 电源重启失败", "#FFFF4444");
                return false;
            }
            Log($"  ✓ 电源已重启", "#FF32CD32");
            return true;
        }

        /// <summary>
        /// 通过样机AT口发送 AT+CFUN=0 → 等1秒 → AT+CFUN=1
        /// </summary>
        private async Task<bool> SendCfunCycleAsync(string comNumber)
        {
            if (!int.TryParse(comNumber, out int number) || number <= 0)
            {
                Log($"  ✗ AT口COM号无效: \"{comNumber}\"，请在界面填写数字", "#FFFF4444");
                return false;
            }

            try
            {
                using var port = new System.IO.Ports.SerialPort($"COM{number}", 115200)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 3000,
                    DtrEnable = true,
                    RtsEnable = true
                };
                await Task.Run(() => port.Open());

                string reply = await SendAtCommandAsync(port, "AT+CFUN=0");
                Log($"    • AT+CFUN=0 → {reply}", "#FF808080");

                await Task.Delay(1000);

                reply = await SendAtCommandAsync(port, "AT+CFUN=1");
                Log($"    • AT+CFUN=1 → {reply}", "#FF808080");
                return true;
            }
            catch (Exception ex)
            {
                Log($"  ✗ AT口COM{number}操作失败: {ex.Message}", "#FFFF4444");
                return false;
            }
        }

        private static Task<string> SendAtCommandAsync(System.IO.Ports.SerialPort port, string command)
        {
            return Task.Run(() =>
            {
                port.DiscardInBuffer();
                port.Write(command + "\r");

                // 等待 OK/ERROR（CFUN 切换可能需要几秒），最多5秒
                var reply = new StringBuilder();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 5000)
                {
                    reply.Append(port.ReadExisting());
                    string text = reply.ToString();
                    if (text.Contains("OK") || text.Contains("ERROR"))
                        break;
                    Thread.Sleep(50);
                }

                string result = reply.ToString().Replace("\r", " ").Replace("\n", " ").Trim();
                return string.IsNullOrEmpty(result) ? "(无回复)" : result;
            });
        }

        private async Task<bool> RestartPowerSupplyAsync()
        {
            // 重启电源以恢复 UE 连接
            try
            {
                if (powerController == null)
                    return false;

                // AT控制模式下禁止操作电源
                if (IsAtRecoveryMode)
                {
                    Log($"    • 当前为「AT控制」模式，禁止电源OFF/ON", "#FFFF4444");
                    return false;
                }

                // 关闭电源输出
                bool offResult = await powerController.PowerOffAsync();
                if (!offResult)
                {
                    Log($"    • 电源关闭失败", "#FFFF4444");
                    return false;
                }

                Log($"    • 电源输出已关闭 (OFF)", "#FFFF9900");
                await Task.Delay(2000);  // 等待2秒

                // 重新开启电源输出
                bool onResult = await powerController.PowerOnAsync();
                if (!onResult)
                {
                    Log($"    • 电源开启失败", "#FFFF4444");
                    return false;
                }

                Log($"    • 电源输出已开启 (ON)", "#FF32CD32");
                await Task.Delay(1000);  // 等待1秒稳定

                return true;
            }
            catch (Exception ex)
            {
                Log($"    • 电源重启失败: {ex.Message}", "#FFFF4444");
                return false;
            }
        }

        private void BtnSelectAllBands_Click(object sender, RoutedEventArgs e)
        {
            // 全选所有 BAND
            chkB1.IsChecked = true;
            chkB2.IsChecked = true;
            chkB3.IsChecked = true;
            chkB4.IsChecked = true;
            chkB5.IsChecked = true;
            chkB7.IsChecked = true;
            chkB8.IsChecked = true;
            chkB12.IsChecked = true;
            chkB13.IsChecked = true;
            chkB17.IsChecked = true;
            chkB18.IsChecked = true;
            chkB19.IsChecked = true;
            chkB20.IsChecked = true;
            chkB25.IsChecked = true;
            chkB26.IsChecked = true;
            chkB28.IsChecked = true;
            chkB66.IsChecked = true;
            chkB34.IsChecked = true;
            chkB38.IsChecked = true;
            chkB39.IsChecked = true;
            chkB40.IsChecked = true;
            chkB41.IsChecked = true;

            Log("✓ 已全选所有BAND", "#FF32CD32");
        }

        private void BtnDeselectAllBands_Click(object sender, RoutedEventArgs e)
        {
            // 取消所有 BAND 勾选
            chkB1.IsChecked = false;
            chkB2.IsChecked = false;
            chkB3.IsChecked = false;
            chkB4.IsChecked = false;
            chkB5.IsChecked = false;
            chkB7.IsChecked = false;
            chkB8.IsChecked = false;
            chkB12.IsChecked = false;
            chkB13.IsChecked = false;
            chkB17.IsChecked = false;
            chkB18.IsChecked = false;
            chkB19.IsChecked = false;
            chkB20.IsChecked = false;
            chkB25.IsChecked = false;
            chkB26.IsChecked = false;
            chkB28.IsChecked = false;
            chkB66.IsChecked = false;
            chkB34.IsChecked = false;
            chkB38.IsChecked = false;
            chkB39.IsChecked = false;
            chkB40.IsChecked = false;
            chkB41.IsChecked = false;

            Log("✓ 已取消所有BAND勾选", "#FFFF9900");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 停止日志刷新定时器
            _logFlushTimer?.Stop();

            // 最后一次刷新日志缓冲区
            FlushLogBuffer();

            testCancellation?.Cancel();
            cmwController?.Dispose();
            currentMeter?.Dispose();
        }
    }
}
