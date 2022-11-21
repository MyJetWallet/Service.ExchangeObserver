using System;

namespace Service.ExchangeObserver.Domain.Models
{
    public class ObserverTransfer
    {
        public string Id { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Asset { get; set; }
        public decimal Amount { get; set; }
        public decimal IndexPrice { get; set; }
        public string Reason { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}