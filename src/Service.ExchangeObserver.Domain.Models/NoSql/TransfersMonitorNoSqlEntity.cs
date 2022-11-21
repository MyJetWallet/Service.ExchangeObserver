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
        public decimal DebtAmount { get; set; }
        public DateTime LastTs { get; set; }
        
        public static TransfersMonitorNoSqlEntity Create(string asset, decimal debt, DateTime timestamp)
        {
            return new TransfersMonitorNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(),
                RowKey = GenerateRowKey(asset),
                Asset = asset,
                DebtAmount = debt,
                LastTs = timestamp
            };
        }
    }
}