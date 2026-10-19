namespace LTECurrentTool
{
    /// <summary>
    /// 电流测量设备（66319D 电源 / POWER-Z KM002C 电池供电）
    /// </summary>
    public interface ICurrentMeter : IDisposable
    {
        /// <summary>设备名称（用于日志）</summary>
        string Name { get; }

        /// <summary>最近一次连接失败的原因</summary>
        string LastError { get; }

        /// <summary>单次电流读数(mA)</summary>
        Task<double> MeasureCurrentAsync();

        /// <summary>测试点电流测量（平均/最大/最小）</summary>
        Task<CurrentMeasurement> MeasureAdaptiveCurrentAsync(string tpcSetup);

        /// <summary>通信中断后重新连接</summary>
        Task<bool> ReconnectAsync();

        void Disconnect();
    }
}
