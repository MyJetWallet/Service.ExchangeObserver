using System;
using MyNoSqlServer.Abstractions;

namespace Service.ExchangeObserver.Domain.Models.NoSql
{
    public class BinanceToFireblocksAssetNoSqlEntity: MyNoSqlDbEntity
    {
        public const string TableName = "jetwallet-external-binance-to-fireblocks";
    
        public static string GeneratePartitionKey(string binanceAsset) => binanceAsset;
        public static string GenerateRowKey(string fireblocksAsset) => fireblocksAsset;
        
        public string BinanceAssetSymbol { get; set; }
        public string FireblocksAsset { get; set; }
        public string FireblocksNetwork { get; set; }
        public decimal MinTransferAmount { get; set; }
        
        public static BinanceToFireblocksAssetNoSqlEntity Create(string binanceAsset, string fireblocksAsset, string network, decimal minAmount)
        {
            return new BinanceToFireblocksAssetNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(binanceAsset),
                RowKey = GenerateRowKey(fireblocksAsset),
                BinanceAssetSymbol = binanceAsset,
                FireblocksAsset = fireblocksAsset,
                FireblocksNetwork = network,
                MinTransferAmount = minAmount,
            };
        }
    }
}