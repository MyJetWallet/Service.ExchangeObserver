using System;
using MyNoSqlServer.Abstractions;

namespace Service.ExchangeObserver.Domain.Models.NoSql
{
    public class BinanceExchangeAssetNoSqlEntity: MyNoSqlDbEntity
    {
        public const string TableName = "jetwallet-binance-exchange-asset";
    
        public static string GeneratePartitionKey() => "Binance";
        public static string GenerateRowKey(string assetSymbol) => assetSymbol;
    
        public string AssetSymbol { get; set; }
        public int Weight { get; set; }
        public DateTime LockedUntil { get; set; }

        public static BinanceExchangeAssetNoSqlEntity Create(string assetSymbol, int weight)
        {
            return new BinanceExchangeAssetNoSqlEntity
            {
                RowKey = GenerateRowKey(assetSymbol),
                AssetSymbol = assetSymbol,
                Weight = weight,
                LockedUntil = DateTime.MinValue
            };
        }
    }
}