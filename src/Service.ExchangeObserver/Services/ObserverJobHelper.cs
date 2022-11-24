using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MyNoSqlServer.Abstractions;
using Service.ExchangeObserver.Domain.Models;
using Service.ExchangeObserver.Domain.Models.NoSql;
using Service.ExchangeObserver.Postgres;

namespace Service.ExchangeObserver.Services
{
    public class ObserverJobHelper
    {
        private readonly IMyNoSqlServerDataWriter<TransfersMonitorNoSqlEntity> _monitoringWriter;
        private readonly DbContextOptionsBuilder<DatabaseContext> _dbContextOptionsBuilder;

        public ObserverJobHelper(DbContextOptionsBuilder<DatabaseContext> dbContextOptionsBuilder, IMyNoSqlServerDataWriter<TransfersMonitorNoSqlEntity> monitoringWriter)
        {
            _dbContextOptionsBuilder = dbContextOptionsBuilder;
            _monitoringWriter = monitoringWriter;
        }

        public async Task SaveTransfer(ObserverTransfer transfer)
        {
            await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
            await context.AddAsync(transfer);
            await context.SaveChangesAsync();
        }
        public async Task<List<ObserverAsset>> GetAssets()
        {
            await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
            var assets = await context.Assets.ToListAsync();
            return assets;
        }
        public async Task LockAsset(string assetSymbol, int assetLockTime)
        {
            await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);

            var assets = await context.Assets.Where(t => t.AssetSymbol == assetSymbol).ToListAsync();
            foreach (var asset in assets)
            {
                asset.LockedUntil = MaxDate(asset.LockedUntil, DateTime.UtcNow.AddMinutes(assetLockTime));
            }
            await context.SaveChangesAsync();
            
            DateTime MaxDate(DateTime currentLock, DateTime newLock)
            {
                var maxTicks = Math.Max(currentLock.Ticks, newLock.Ticks);
                return new DateTime(maxTicks);
            }
        }
        public async Task AddToMonitor(string symbol, decimal borrowedBalance, string reason, string comment, string type)
        {
            await _monitoringWriter.InsertOrReplaceAsync(TransfersMonitorNoSqlEntity.Create(symbol,borrowedBalance,  DateTime.UtcNow, reason, comment, type));
        }
        public async Task ResetMonitor(string asset)
        {
            await _monitoringWriter.DeleteAsync(TransfersMonitorNoSqlEntity.GeneratePartitionKey(),
                TransfersMonitorNoSqlEntity.GenerateRowKey(asset));
        }
    }
}