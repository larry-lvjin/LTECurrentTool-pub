using System.IO;
using OfficeOpenXml;

namespace LTECurrentTool
{
    public class BandConfig
    {
        public string BandName { get; set; } = "";
        public string ChannelType { get; set; } = "";
        public int Channel { get; set; }
        public string TestType { get; set; } = "";
        public string DownlinkRB { get; set; } = "50";
        public string UplinkRB { get; set; } = "12";
        public string TPCSetup { get; set; } = "";
        public string TargetPower { get; set; } = "";

        // 主结果（平均值）
        public double MeasuredCurrent { get; set; }

        // 辅助统计（简化版 - 仅核心指标）
        public double MaxCurrent { get; set; }      // 最大值
        public double MinCurrent { get; set; }      // 最小值
        public double P95Current { get; set; }      // 95百分位

        // 数据完整性
        public int TotalSamples { get; set; }       // 实际采集样本数

        public string Status { get; set; } = "待测试";

        public BandConfig(string bandName, string channelType, int channel, string testType = "",
                          string dlRB = "50", string ulRB = "12", string tpc = "", string power = "")
        {
            BandName = bandName;
            ChannelType = channelType;
            Channel = channel;
            TestType = testType;
            DownlinkRB = dlRB;
            UplinkRB = ulRB;
            TPCSetup = tpc;
            TargetPower = power;
        }

        public string DisplayName => $"{BandName} {ChannelType} ({TestType})";

        /// <summary>
        /// Max - P95 差值（mA）
        /// </summary>
        public double MaxMinusP95
        {
            get { return MaxCurrent - P95Current; }
        }
    }

    public static class LTEBands
    {
        public static List<BandConfig> LoadFromExcel(string excelPath)
        {
            if (!File.Exists(excelPath))
            {
                return GetDefaultBands();
            }

            try
            {
                return GetDefaultBands();  // 简化处理，使用默认配置
            }
            catch
            {
                return GetDefaultBands();
            }
        }

        public static List<BandConfig> GetDefaultBands()
        {
            var bands = new List<BandConfig>();

            // 信道号依据 3GPP TS 36.508 §4.3.1 的 10MHz 测试频点（上行 EARFCN），并按 TS 36.101 表5.7.3-1 校验
            // === FDD 信道（49个）===
            // B1-B66（所有FDD BAND）
            string[,] fddChannels = {
                // B1-B8
                {"B1", "Low", "18050"}, {"B1", "Mid", "18300"}, {"B1", "High", "18550"},
                {"B2", "Low", "18650"}, {"B2", "Mid", "18900"}, {"B2", "High", "19150"},
                {"B3", "Low", "19250"}, {"B3", "Mid", "19575"}, {"B3", "High", "19900"},
                {"B4", "Low", "20000"}, {"B4", "Mid", "20175"}, {"B4", "High", "20350"},
                {"B5", "Low", "20450"}, {"B5", "Mid", "20525"}, {"B5", "High", "20600"},
                {"B7", "Low", "20800"}, {"B7", "Mid", "21100"}, {"B7", "High", "21400"},
                {"B8", "Low", "21500"}, {"B8", "Mid", "21625"}, {"B8", "High", "21750"},
                // B12-B20
                {"B12", "Low", "23060"}, {"B12", "Mid", "23095"}, {"B12", "High", "23130"},
                {"B13", "Mid", "23230"},  // B13特殊：上行仅10MHz宽，10MHz下高中低同为23230，只测Mid
                {"B17", "Low", "23780"}, {"B17", "Mid", "23790"}, {"B17", "High", "23800"},
                {"B18", "Low", "23900"}, {"B18", "Mid", "23925"}, {"B18", "High", "23950"},
                {"B19", "Low", "24050"}, {"B19", "Mid", "24075"}, {"B19", "High", "24100"},
                {"B20", "Low", "24200"}, {"B20", "Mid", "24300"}, {"B20", "High", "24400"},
                // B25-B28
                {"B25", "Low", "26090"}, {"B25", "Mid", "26365"}, {"B25", "High", "26640"},
                {"B26", "Low", "26740"}, {"B26", "Mid", "26865"}, {"B26", "High", "26990"},
                {"B28", "Low", "27260"}, {"B28", "Mid", "27410"}, {"B28", "High", "27610"},
                // B66
                {"B66", "Low", "132022"}, {"B66", "Mid", "132322"}, {"B66", "High", "132622"}
            };

            // === TDD 信道（15个）===
            // B34-B41（所有TDD BAND）
            string[,] tddChannels = {
                {"B34", "Low", "36250"}, {"B34", "Mid", "36275"}, {"B34", "High", "36300"},
                {"B38", "Low", "37800"}, {"B38", "Mid", "38000"}, {"B38", "High", "38200"},
                {"B39", "Low", "38300"}, {"B39", "Mid", "38450"}, {"B39", "High", "38600"},
                {"B40", "Low", "38700"}, {"B40", "Mid", "39150"}, {"B40", "High", "39600"},
                {"B41", "Low", "39700"}, {"B41", "Mid", "40620"}, {"B41", "High", "41540"}
            };

            // 10种测试类型（按LTE电流规则.md）- 使用集中定义的只读配置
            var testConfigs = LTECurrentRules.TPC_CONFIGURATIONS;

            // ========== 第一阶段：FDD 组（490个配置 = 10种TPC × 49个FDD信道）==========
            foreach (var (testType, dlRB, ulRB, tpc, power) in testConfigs)
            {
                // 遍历所有FDD信道
                for (int i = 0; i < fddChannels.GetLength(0); i++)
                {
                    string bandName = fddChannels[i, 0];
                    string channelType = fddChannels[i, 1];
                    int channel = int.Parse(fddChannels[i, 2]);

                    bands.Add(new BandConfig(bandName, channelType, channel, testType,
                                           dlRB, ulRB, tpc, power));
                }
            }

            // ========== 第二阶段：TDD 组（150个配置 = 10种TPC × 15个TDD信道）==========
            // 注意：FDD→TDD切换时，CMW500会自动执行完整的Cell重启
            foreach (var (testType, dlRB, ulRB, tpc, power) in testConfigs)
            {
                // 遍历所有TDD信道
                for (int i = 0; i < tddChannels.GetLength(0); i++)
                {
                    string bandName = tddChannels[i, 0];
                    string channelType = tddChannels[i, 1];
                    int channel = int.Parse(tddChannels[i, 2]);

                    bands.Add(new BandConfig(bandName, channelType, channel, testType,
                                           dlRB, ulRB, tpc, power));
                }
            }

            // 总配置数: 490 (FDD) + 150 (TDD) = 640个测试点
            // 双工模式切换次数: 仅1次（FDD组 → TDD组）
            return bands;
        }
    }
}

