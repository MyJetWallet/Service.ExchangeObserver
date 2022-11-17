using System.Runtime.Serialization;
using Service.ExchangeObserver.Domain.Models;

namespace Service.ExchangeObserver.Grpc.Models
{
    [DataContract]
    public class HelloMessage : IHelloMessage
    {
        [DataMember(Order = 1)]
        public string Message { get; set; }
    }
}