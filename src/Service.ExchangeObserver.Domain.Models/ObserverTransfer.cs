using System;
using System.Runtime.Serialization;

namespace Service.ExchangeObserver.Domain.Models
{
    [DataContract]
    public class ObserverTransfer
    {
        [DataMember(Order = 1)]
        public int TransferId { get; set; }
        [DataMember(Order = 2)]
        public string From { get; set; }
        [DataMember(Order = 3)]
        public string To { get; set; }
        [DataMember(Order = 4)]
        public string Asset { get; set; }
        [DataMember(Order = 5)]
        public decimal Amount { get; set; }
        [DataMember(Order = 6)]
        public decimal IndexPrice { get; set; }
        [DataMember(Order = 7)]
        public string Reason { get; set; }
        [DataMember(Order = 8)]
        public DateTime TimeStamp { get; set; }
    }
}