using System;
using System.Collections.Generic;
using MyNoSqlServer.Abstractions;

namespace Service.ExchangeObserver.Domain.Models.NoSql
{
    public class ObserverSettingsNoSqlEntity: MyNoSqlDbEntity
    {
        public const string TableName = "jetwallet-external-observer-settings";
    
        public static string GeneratePartitionKey() => "ObserverSettings";
        public static string GenerateRowKey() => "ObserverSettings";
        
        public decimal MinimalExchangeBalanceUsd { get; set; }
        public decimal MaximumExchangeBalanceUsd { get; set; }
        //public List<string> ObservingExchanges { get; set; }
        
        public static ObserverSettingsNoSqlEntity Create(decimal minAmount, decimal maxAmount 
            //,List<string> exchanges
            )
        {
            return new ObserverSettingsNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(),
                RowKey = GenerateRowKey(),
                MinimalExchangeBalanceUsd = minAmount,
                MaximumExchangeBalanceUsd = maxAmount,
            };
        }
    }
}