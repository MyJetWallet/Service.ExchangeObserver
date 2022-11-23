using System;
using MyNoSqlServer.Abstractions;

namespace Service.ExchangeObserver.Domain.Models.NoSql
{
    public class BinanceExchangeAssetNoSqlEntity: MyNoSqlDbEntity
    {
        public const string TableName = "jetwallet-binance-exchange-asset";
    
        public static string GeneratePartitionKey() => "Binance";
        public static string GenerateRowKey(string assetSymbol, string network) => $"{assetSymbol}|{network}";
    
        public string AssetSymbol { get; set; }
        public int Weight { get; set; }
        public string Network { get; set; }
        public decimal MinTransferAmount { get; set; }
        public DateTime LockedUntil { get; set; }

        public static BinanceExchangeAssetNoSqlEntity Create(string assetSymbol, int weight, string network, decimal minAmount)
        {
            return new BinanceExchangeAssetNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(),
                RowKey = GenerateRowKey(assetSymbol, network),
                AssetSymbol = assetSymbol,
                Weight = weight,
                LockedUntil = DateTime.MinValue,
                Network = network,
                MinTransferAmount = minAmount,
            };
        }
    }
}