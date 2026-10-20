using System.Globalization;
using NationalInstruments.Visa;

namespace LTECurrentTool
{
    /// <summary>
    /// 电流测量结果（简化版 - 仅5个核心统计量）
    /// </summary>
    public class CurrentMeasurement
    {
        public bool IsValid { get; set; }
        public string TpcMode { get; set; } = "";

        // === 5个核心统计量 ===
        public double Mean { get; set; }          // 平均(mA)
        public double Max { get; set; }           // 最大(mA)
        public double Min { get; set; }           // 最小(mA)
        public double P95 { get; set; }           // P95(mA)
        public double MaxMinusP95 { get; set; }   // Max-P95(mA)

        // 样本数
        public int TotalSamples { get; set; }     // 实际采集样本数

        // === 诊断字段（用于调稳健滤波阈值）===
        public int RejectedOutliers { get; set; } // 被剔除的毛刺点数
        public double RawMax { get; set; }         // 剔除前的原始Max(mA)
        public double RawMin { get; set; }         // 剔除前的原始Min(mA)

        // 状态（✓ 完成 / ✗ 失败 / ⚪ 跳过）
        public string Status { get; set; } = "✓ 完成";
    }

    public class PowerSupplyController : ICurrentMeter
    {
        public string Name => "66319D";

        private MessageBasedSession? session;
        private ResourceManager? resourceManager;
        private readonly string resourceName;
        private readonly int channel;

        // 程序发布时间（绑定到.exe文件修改时间，每次重新编译会自动更新）
        private static readonly DateTime BUILD_TIME = GetBuildTime();
        // 有效期：518400分钟（360天 ≈ 12个月），从每次生成(编译发布)时刻起算
        private const int VALIDITY_MINUTES = 518400;

        /// <summary>
        /// 获取程序编译时间（基于.exe文件修改时间）
        /// 这样可以保证: 重启程序不会重置计时器，5分钟从编译时刻开始算
        /// </summary>
        private static DateTime GetBuildTime()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public PowerSupplyController(string visaResourceName = "GPIB0::5::INSTR", int channelNumber = LTECurrentRules.POWER_SUPPLY_CHANNEL)
        {
            resourceName = visaResourceName;
            channel = channelNumber;
        }

        /// <summary>
        /// 检查程序是否在有效期内（静默检查，过期不提示）
        /// </summary>
        private bool CheckValidity()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 获取程序有效期信息 - 测试版本：返回剩余分钟数
        /// </summary>
        public static (bool IsValid, DateTime ExpiryTime, int RemainingMinutes) GetValidityInfo()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public async Task<bool> ConnectAsync()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 重新建立GPIB会话（测试中出现"连接已丢失"时调用，不修改电压）
        /// </summary>
        public async Task<bool> ReconnectAsync()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private const int CONNECT_MAX_ATTEMPTS = 3;

        /// <summary>最近一次连接失败的原因（用于弹窗显示）</summary>
        public string LastError { get; private set; } = "";

        private bool TryConnectOnce(int attempt)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public void Disconnect()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private void Write(string command)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private string Query(string command)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private string QueryOpc()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private void ClearErrorQueue()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public async Task<double> MeasureCurrentAsync()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private bool digitizerInitialized = false;

        /// <summary>
        /// 初始化Digitizer模式（BUS触发 + *TRG，每次会话只调用一次）
        /// </summary>
        private void InitializeDigitizer()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// Digitizer模式：BUS触发 + *TRG 采集（4ms/点，快12倍）
        /// </summary>
        private double[] AcquireDigitizer(int points)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 整帧对齐的分段Digitizer采集（保护老66319D不死锁）
        /// 单段500点（50个LTE radio frame），采集耗时约0.5秒，段间80ms
        /// </summary>
        private double[] AcquireFrameAligned(int totalPoints, int segmentSize = 500)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 该步骤具体实现与仪器指令未公开
        /// 该步骤具体实现与仪器指令未公开
        /// 20次约10秒，均值std从单次~8%降到~1.8%。Max/Min/P95为这些0.5s积分值的分布(可作稳定性参考，永不为负)。
        /// </summary>
        private CurrentMeasurement MeasureByMeasCurr(string tpcMode)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 4段连续采集（500×4连续拼接，总2000点）
        /// 主结果: 2000个合并样本的直接平均
        /// </summary>
        private CurrentMeasurement MeasureSegmented(string tpcMode)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// TPC自适应电流测量（使用正确的Digitizer模式）
        /// </summary>
        /// <param name="tpcSetup">TPC模式（用于确定采样点数）</param>
        /// <returns>(测量结果)</returns>
        public async Task<CurrentMeasurement> MeasureAdaptiveCurrentAsync(string tpcSetup)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 计算完整统计（不做离群值剔除）
        /// </summary>
        private CurrentMeasurement CalculateStatistics(double[] samples, string tpcMode, int expectedSamples)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 计算百分位数
        /// </summary>
        private double Percentile(double[] sortedData, double p)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 兼容旧接口（已弃用，仅保留向后兼容）
        /// </summary>
        [Obsolete("使用 MeasureAdaptiveCurrentAsync 替代")]
        public async Task<(double Average, List<double> Samples)> MeasureAverageCurrentAsync(int totalSamples = 50)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public async Task<bool> PowerOffAsync()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public async Task<bool> PowerOnAsync()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public async Task<bool> CyclePowerAsync(int offDurationMs = LTECurrentRules.POWER_CYCLE_OFF_DURATION_MS)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
