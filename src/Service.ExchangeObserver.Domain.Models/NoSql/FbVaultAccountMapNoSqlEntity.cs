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
        public Dictionary<string, decimal> FireblocksAssetsWithBalances { get; set; }
        
        public static FbVaultAccountMapNoSqlEntity Create(int vaultAccountId, Dictionary<string, decimal> fireblocksAssets)
        {
            return new FbVaultAccountMapNoSqlEntity
            {
                PartitionKey = GeneratePartitionKey(),
                RowKey = GenerateRowKey(vaultAccountId),
                VaultAccountId = vaultAccountId,
                FireblocksAssetsWithBalances = fireblocksAssets,
            };
        }
    }
}