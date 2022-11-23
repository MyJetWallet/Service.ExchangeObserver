using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Service.ExchangeObserver.Domain.Models;
using Service.ExchangeObserver.Grpc;
using Service.ExchangeObserver.Grpc.Models;
using Service.ExchangeObserver.Postgres;
using Service.ExchangeObserver.Settings;

namespace Service.ExchangeObserver.Services
{
    public class ObserverService : IObserverService
    {
        private readonly ILogger<ObserverService> _logger;
        private readonly DbContextOptionsBuilder<DatabaseContext> _dbContextOptionsBuilder;


        public ObserverService(ILogger<ObserverService> logger,
            DbContextOptionsBuilder<DatabaseContext> dbContextOptionsBuilder)
        {
            _logger = logger;
            _dbContextOptionsBuilder = dbContextOptionsBuilder;
        }

        public async Task<AllAssetsResponse> GetAllAssets()
        {
            await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
            var assets = await context.Assets.ToListAsync();
            return new AllAssetsResponse
            {
                Assets = assets
            };
        }

        public async Task<OperationResponse> AddOrUpdateAsset(UpdateAssetRequest request)
        {
            try
            {
                await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
                var asset = await context.Assets.FirstOrDefaultAsync(t =>
                                t.AssetSymbol == request.AssetSymbol && t.Network == request.Network)
                            ?? new ObserverAsset()
                            {
                                AssetSymbol = request.AssetSymbol,
                                Network = request.Network
                            };

                asset.Weight = request.Weight;
                asset.MinTransferAmount = request.MinTransferAmount;
                asset.BinanceSymbol = request.BinanceSymbol;
                asset.LockTimeInMin = request.LockTimeInMin;

                await context.UpsertAsync(new[] {asset});
                return new OperationResponse()
                {
                    IsSuccess = true
                };
            }
            catch (Exception e)
            {
                return new OperationResponse()
                {
                    IsSuccess = false,
                    ErrorMessage = e.Message
                };
            }
        }

        public async Task<OperationResponse> RemoveAsset(RemoveAssetRequest request)
        {
            try
            {
                await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
                var asset = await context.Assets.FirstOrDefaultAsync(t =>
                    t.AssetSymbol == request.AssetSymbol && t.Network == request.Network);
                if (asset != null)
                {
                    context.Remove(asset);
                    await context.SaveChangesAsync();
                }

                return new OperationResponse()
                {
                    IsSuccess = true
                };
            }
            catch (Exception e)
            {
                return new OperationResponse()
                {
                    IsSuccess = false,
                    ErrorMessage = e.Message
                };
            }
        }

        public async IAsyncEnumerable<ObserverTransfer> GetTransfers(GetTransfersRequest request)
        {
            await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);

            if (request.Take == 0)
            {
                request.Take = 20;
            }

            var query = context.Transfers.AsQueryable();

            if (request.LastSeenId != 0)
            {
                query = query.Where(t => t.TransferId < request.LastSeenId);
            }


            if (!string.IsNullOrWhiteSpace(request.Asset))
            {
                query = query.Where(t => t.Asset == request.Asset);
            }

            if (request.TsDateFrom != null)
            {
                query = query.Where(t => t.TimeStamp >= request.TsDateFrom);
            }

            if (request.TsDateTo != null)
            {
                query = query.Where(t => t.TimeStamp <= request.TsDateTo);
            }

            if (!string.IsNullOrWhiteSpace(request.SearchText))
            {
                query = query.Where(t => t.From.Contains(request.SearchText) ||
                                         t.To.Contains(request.SearchText) ||
                                         t.Reason.Contains(request.SearchText));
            }

            query = query.OrderByDescending(t => t.TransferId).Take(request.Take);

            await foreach (var transfer in query.AsAsyncEnumerable())
            {
                yield return transfer;
            }
        }
    }
}