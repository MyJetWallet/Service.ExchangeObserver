using System.Runtime.Serialization;

namespace Service.ExchangeObserver.Grpc.Models
{
    [DataContract]
    public class UpdateAssetRequest
    {
        [DataMember(Order = 1)] public string AssetSymbol { get; set; }
        [DataMember(Order = 2)] public int Weight { get; set; }
        [DataMember(Order = 3)] public string Network { get; set; }
        [DataMember(Order = 4)] public decimal MinTransferAmount { get; set; }
        [DataMember(Order = 5)] public string BinanceSymbol { get; set; }
        [DataMember(Order = 6)] public int LockTimeInMin { get; set; }
        [DataMember(Order = 7)] public bool IsEnabled { get; set; }
        [DataMember(Order = 8)] public decimal FireblocksToBinanceFee { get; set; }
    }
}