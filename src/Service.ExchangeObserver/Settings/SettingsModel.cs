using System.Collections.Generic;
using MyJetWallet.Sdk.Service;
using MyYamlParser;

namespace Service.ExchangeObserver.Settings
{
    public class SettingsModel
    {
        [YamlProperty("ExchangeObserver.SeqServiceUrl")]
        public string SeqServiceUrl { get; set; }

        [YamlProperty("ExchangeObserver.ZipkinUrl")]
        public string ZipkinUrl { get; set; }

        [YamlProperty("ExchangeObserver.ElkLogs")]
        public LogElkSettings ElkLogs { get; set; }
        
        [YamlProperty("ExchangeObserver.TimerPeriodInSec")]
        public int TimerPeriodInSec { get; set; }
        
        [YamlProperty("ExchangeObserver.MyNoSqlReaderHostPort")]
        public string MyNoSqlReaderHostPort { get; set; }
		
        [YamlProperty("ExchangeObserver.MyNoSqlWriterUrl")]
        public string MyNoSqlWriterUrl { get; set; }
        
        [YamlProperty("ExchangeObserver.ExternalApiGrpcUrl")]
        public string ExternalApiGrpcUrl { get; set; }
        
        [YamlProperty("ExchangeObserver.ExchangeSettings")]
        public Dictionary<string, ExchangeBalanceSettings> ExternalExchanges { get; set; }
    }

    public class ExchangeBalanceSettings
    {
        [YamlProperty("BorrowedThresholdUsd")]
        public int BorrowedThresholdUsd { get; set; } 
        
        [YamlProperty("MinimalExchangeBalanceUsd")]
        public int MinimalExchangeBalanceUsd { get; set; }
        
        [YamlProperty("MaximumExchangeBalanceUsd")]
        public int MaximumExchangeBalanceUsd { get; set; }
    }
}
