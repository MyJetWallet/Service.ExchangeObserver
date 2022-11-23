using System;
using System.Runtime.Serialization;

namespace Service.ExchangeObserver.Grpc.Models;

[DataContract]
public class GetTransfersRequest
{
    [DataMember(Order = 1)] public string SearchText { get; set; }
    [DataMember(Order = 2)] public int LastSeenId { get; set; }
    [DataMember(Order = 3)] public int Take { get; set; }
    [DataMember(Order = 4)] public DateTime? TsDateFrom { get; set; }
    [DataMember(Order = 5)] public DateTime? TsDateTo { get; set; }
    [DataMember(Order = 6)] public string Asset { get; set; }
}