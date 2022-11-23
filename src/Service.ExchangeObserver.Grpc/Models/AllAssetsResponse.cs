using System.Collections.Generic;
using System.Runtime.Serialization;
using Service.ExchangeObserver.Domain.Models;

namespace Service.ExchangeObserver.Grpc.Models
{
    [DataContract]
    public class AllAssetsResponse
    {
        [DataMember(Order = 1)]
        public List<ObserverAsset> Assets { get; set; }
    }
}