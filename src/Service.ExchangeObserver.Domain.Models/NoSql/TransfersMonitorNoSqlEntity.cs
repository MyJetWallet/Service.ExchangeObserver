using System;
using System.Collections.Generic;
using MyNoSqlServer.Abstractions;

namespace Service.ExchangeObserver.Domain.Models.NoSql
{
    public class TransfersMonitorNoSqlEntity: MyNoSqlDbEntity
    {
        public const string TableName = "jetwallet-observer-failed-transfers";
    
        public static string GeneratePartitionKey() => "ObserverFailed";
        public static string GenerateRowKey(string asset) => asset;
    
        public string Asset { get; set; }
        public decimal Amount { get; set; }
        public DateTime LastTs { get; set; }
        public string Reason { get; set; }
        public string Comment { get; set; }
        public string Type { get; set; }

        
        public static TransfersMonitorNoSqlEntity Create(string asset, decimal debt, DateTime timestamp, string reason, string comment, string type)
        {
            return new TransfersMonitorNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(),
                RowKey = GenerateRowKey(asset),
                Asset = asset,
                Amount = debt,
                LastTs = timestamp,
                Comment = comment,
                Reason = reason
            };
        }
    }
}