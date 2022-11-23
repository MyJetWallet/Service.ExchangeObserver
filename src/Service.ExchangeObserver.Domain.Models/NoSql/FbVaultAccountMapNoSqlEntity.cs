using System;
using System.Collections.Generic;
using MyNoSqlServer.Abstractions;

namespace Service.ExchangeObserver.Domain.Models.NoSql
{
    public class FbVaultAccountMapNoSqlEntity: MyNoSqlDbEntity
    {
        public const string TableName = "jetwallet-external-fireblocks-vaults";
    
        public static string GeneratePartitionKey() => "VaultAccountMap";
        public static string GenerateRowKey(int vaultAccountId) => vaultAccountId.ToString();
        
        public int VaultAccountId { get; set; }
        public int Weight { get; set; }
        public List<AssetAndBalance> FireblocksAssetsWithBalances { get; set; }
        
        public static FbVaultAccountMapNoSqlEntity Create(int vaultAccountId, int weight, List<AssetAndBalance> fireblocksAssets)
        {
            return new FbVaultAccountMapNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(),
                RowKey = GenerateRowKey(vaultAccountId),
                VaultAccountId = vaultAccountId,
                Weight = weight,
                FireblocksAssetsWithBalances = fireblocksAssets,
            };
        }
    }

    public class AssetAndBalance
    {
        public string Asset { get; set; }
        public decimal MinBalance { get; set; }
    }
}