using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using MyJetWallet.Domain.ExternalMarketApi;
using MyJetWallet.Domain.ExternalMarketApi.Dto;
using MyJetWallet.Sdk.Service.Tools;
using MyNoSqlServer.Abstractions;
using Service.ExchangeObserver.Domain.Models;
using Service.ExchangeObserver.Domain.Models.NoSql;
using Service.IndexPrices.Client;

namespace Service.ExchangeObserver.Jobs
{
    public class ExchangeCheckerJob : IStartable
    {
        private readonly IExternalMarket _externalMarket;
        private readonly IIndexPricesClient _indexPricesClient;
        private readonly ILogger<ExchangeCheckerJob> _logger;
        private readonly MyTaskTimer _timer;
        private readonly IMyNoSqlServerDataWriter<ExternalExchangeAssetNoSqlEntity> _writer;

        public ExchangeCheckerJob(IExternalMarket externalMarket, IIndexPricesClient indexPricesClient, ILogger<ExchangeCheckerJob> logger, IMyNoSqlServerDataWriter<ExternalExchangeAssetNoSqlEntity> writer)
        {
            _externalMarket = externalMarket;
            _indexPricesClient = indexPricesClient;
            _logger = logger;
            _writer = writer;

            _timer = MyTaskTimer.Create<ExchangeCheckerJob>(TimeSpan.FromSeconds(Program.Settings.TimerPeriodInSec), logger, DoTime);
        }

        private async Task DoTime()
        {
            foreach (var exchange in Program.Settings.ExternalExchanges.Keys)
            {
                await CheckExchangeBorrows(exchange);
                await CheckExchangeBalance(exchange);
            }
        }

        private async Task CheckExchangeBorrows(string exchangeName)
        {
            var assetsWithBalance = new List<ExternalExchangeAssetWithBalance>();
            var settings = Program.Settings.ExternalExchanges[exchangeName];

            var balances = await _externalMarket.GetBalancesAsync(new GetBalancesRequest()
            {
                ExchangeName = exchangeName
            });
            
            var assets = await _writer.GetAsync(ExternalExchangeAssetNoSqlEntity.GeneratePartitionKey(exchangeName));

            foreach (var balance in balances.Balances)
            {
                var asset = assets.FirstOrDefault(t => t.AssetSymbol == balance.Symbol);
                if (asset == null)
                {
                    asset = ExternalExchangeAssetNoSqlEntity.Create(balance.Symbol, exchangeName, 0);
                    await _writer.InsertOrReplaceAsync(asset);
                }

                var assetWithBalance = new ExternalExchangeAssetWithBalance
                {
                    AssetSymbol = asset.AssetSymbol,
                    Exchange = asset.Exchange,
                    Weight = asset.Weight,
                    Balance = balance.Balance,
                    BalanceUsd = _indexPricesClient.GetIndexPriceByAssetVolumeAsync(asset.AssetSymbol, balance.Balance).Item2
                };

                assetsWithBalance.Add(assetWithBalance);
            }

            var borrowedAssets = balances.Balances.Where(t => Math.Abs(t.Borrowed) > 0)
                .Select(t => (t.Symbol, t.Borrowed)).ToList();

            foreach (var borrowedPosition in borrowedAssets)
            {
                var borrowedInUsd = _indexPricesClient.GetIndexPriceByAssetVolumeAsync(borrowedPosition.Symbol, Math.Abs(borrowedPosition.Borrowed)).Item2;
                
                if(borrowedInUsd < settings.BorrowedThresholdUsd)
                    continue;
                
                var repayAsset = assetsWithBalance.Where(t => t.BalanceUsd > borrowedInUsd).MaxBy(t => t.Weight);
                
                //TODO: make trade
                Console.WriteLine($"Borrowed {borrowedPosition.Borrowed} {borrowedPosition.Symbol} ({borrowedInUsd} USD). Repay {repayAsset}");
                
                //TODO: adjust balance
            }
        }

        private async Task CheckExchangeBalance(string exchangeName)
        {
            var settings = Program.Settings.ExternalExchanges[exchangeName];
            
            var balances = await _externalMarket.GetBalancesAsync(new GetBalancesRequest()
            {
                ExchangeName = exchangeName
            });

            var totalBalance = balances.Balances.Sum(balance => _indexPricesClient.GetIndexPriceByAssetVolumeAsync(balance.Symbol, balance.Balance).Item2);

            if (totalBalance < settings.MinimalExchangeBalanceUsd)
            {
                //TODO: 
                Console.WriteLine($"Total balance on exchange {exchangeName} is {totalBalance}. Minimal balance {settings.MinimalExchangeBalanceUsd}. Transferring {settings.MinimalExchangeBalanceUsd - totalBalance} to {exchangeName}");
            }
            if (totalBalance > settings.MaximumExchangeBalanceUsd)
            {
                //TODO: 
                Console.WriteLine($"Total balance on exchange {exchangeName} is {totalBalance}. Maximum balance {settings.MaximumExchangeBalanceUsd}. Transferring {totalBalance - settings.MaximumExchangeBalanceUsd} from {exchangeName}");
            }
        }

        public void Start()
        {
            _timer.Start();
        }
    }
}