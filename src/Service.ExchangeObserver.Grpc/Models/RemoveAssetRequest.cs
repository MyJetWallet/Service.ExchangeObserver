using System.Runtime.Serialization;

namespace Service.ExchangeObserver.Grpc.Models;

[DataContract]
public class RemoveAssetRequest
{
    [DataMember(Order = 1)] public string AssetSymbol { get; set; }
    [DataMember(Order = 2)] public string Network { get; set; }
}