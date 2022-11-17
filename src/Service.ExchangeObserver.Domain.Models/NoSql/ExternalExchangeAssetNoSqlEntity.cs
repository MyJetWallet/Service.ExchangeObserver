using MyNoSqlServer.Abstractions;

namespace Service.ExchangeObserver.Domain.Models.NoSql
{
    public class ExternalExchangeAssetNoSqlEntity: MyNoSqlDbEntity
    {
        public const string TableName = "jetwallet-external-exchange-asset";
    
        public static string GeneratePartitionKey(string exchangeName) => exchangeName;
        public static string GenerateRowKey(string assetSymbol) => assetSymbol;
    
        public string AssetSymbol { get; set; }
        public string Exchange { get; set; }
        public int Weight { get; set; }

        public static ExternalExchangeAssetNoSqlEntity Create(string assetSymbol, string exchange, int weight)
        {
            return new ExternalExchangeAssetNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(exchange),
                RowKey = GenerateRowKey(assetSymbol),
                AssetSymbol = assetSymbol,
                Exchange = exchange,
                Weight = weight,
            };
        }
        
        public static ExternalExchangeAssetNoSqlEntity Create(ExternalExchangeAssetWithBalance entity)
        {
            return new ExternalExchangeAssetNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(entity.Exchange),
                RowKey = GenerateRowKey(entity.RowKey),
                AssetSymbol = entity.AssetSymbol,
                Exchange = entity.Exchange,
                Weight = entity.Weight,
            };
        }
    }
    
    
    public class ExternalExchangeAssetWithBalance: ExternalExchangeAssetNoSqlEntity
    {
        public decimal Balance { get; set; }
        public decimal BalanceUsd { get; set; }
    }
}