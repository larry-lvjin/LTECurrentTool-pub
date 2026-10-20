using System.IO.Ports;
using Microsoft.Win32;

namespace LTECurrentTool
{
    /// <summary>
    /// POWER-Z KM002C / KM003C 电流读取（电池 → 表 → 样机，电脑通过表的 CDC 虚拟串口读数）
    /// </summary>
    /// <remarks>
    /// 协议来源: ChargerLAB 官方 hiddemo_vs2019_for KM002C&3C /《KM002C&3C API Description》
    /// 请求: 4字节 MsgHeader, type=CMD_GET_DATA(0x0C), att=ATT_ADC(1)
    /// 回复: 4字节 header(type=CMD_PUT_DATA 0x41) + 4字节扩展 header(att/size) + AdcData(40字节)
    ///       AdcData: int32 Vbus(uV), Ibus(uA), Vbus_avg, Ibus_avg, ...
    /// </remarks>
    public class Km002cController : ICurrentMeter
    {
        private const string USB_VID = "VID_5FC9";
        private static readonly (string Pid, string Model)[] MODELS = { ("PID_0061", "KM002C"), ("PID_0063", "KM003C") };
        private const byte CMD_GET_DATA = 0x0C;
        private const byte CMD_PUT_DATA = 0x41;
        private const int ATT_ADC = 0x001;
        private const int ADC_DATA_SIZE = 40;

        /// <summary>单个测试点测量时长（与66319D的~10秒一致）</summary>
        private const int MEASURE_DURATION_MS = 10000;

        private SerialPort? port;
        private byte msgId = 0;

        public string Name { get; private set; } = "POWER-Z";
        public string LastError { get; private set; } = "";
        public string PortName { get; private set; } = "";

        /// <summary>最近一次读到的电池电压(V)</summary>
        public double LastVoltage { get; private set; }

        public async Task<bool> ConnectAsync()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public Task<bool> ReconnectAsync() => ConnectAsync();

        public void Disconnect()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public void Dispose()
        {
            Disconnect();
        }

        public async Task<double> MeasureCurrentAsync()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        public async Task<CurrentMeasurement> MeasureAdaptiveCurrentAsync(string tpcSetup)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 读一次 ADC 数据，返回瞬时电流(mA)、电压(V)和原始值（用于判断表是否刷新）
        /// </summary>
        private (double CurrentMa, double VoltageV, long Raw) ReadAdc()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        private byte[] ReadExact(int count)
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }

        /// <summary>
        /// 从注册表查找表的 CDC 串口号（USB\VID_5FC9&PID_0061/0063&MI_xx\...\Device Parameters\PortName）
        /// </summary>
        private static (string? PortName, string? Model) FindPortName()
        {
            // 实现细节未公开（算法核心）
            throw new NotImplementedException("该方法的实现不在开源范围内");
        }
    }
}
