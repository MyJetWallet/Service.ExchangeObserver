using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using MyJetWallet.Domain.ExternalMarketApi.Dto;
using MyJetWallet.Domain.ExternalMarketApi.Models;
using MyJetWallet.Sdk.Service;
using MyJetWallet.Sdk.Service.Tools;
using MyNoSqlServer.Abstractions;
using Service.ExchangeGateway.Grpc.Models.Exchange;
using Service.ExchangeObserver.Domain.Models;
using Service.ExchangeObserver.Domain.Models.NoSql;
using Service.ExchangeObserver.Services;
using Service.IndexPrices.Client;

namespace Service.ExchangeObserver.Jobs
{
    public class EquityCheckerJob : IStartable
    {
        private readonly IIndexPricesClient _indexPricesClient;
        private readonly ILogger<EquityCheckerJob> _logger;
        private readonly MyTaskTimer _timer;

        private readonly IMyNoSqlServerDataWriter<ObserverSettingsNoSqlEntity> _settingWriter;

        private readonly IBalanceExtractor _balanceExtractor;

        private readonly ObserverJobHelper _helper;

        public EquityCheckerJob(IIndexPricesClient indexPricesClient,
            ILogger<EquityCheckerJob> logger,
            IBalanceExtractor balanceExtractor,
            IMyNoSqlServerDataWriter<ObserverSettingsNoSqlEntity> settingWriter,
            ObserverJobHelper helper)
        {
            _indexPricesClient = indexPricesClient;
            _logger = logger;
            _balanceExtractor = balanceExtractor;
            _settingWriter = settingWriter;
            _helper = helper;

            _timer = MyTaskTimer.Create<EquityCheckerJob>(TimeSpan.FromSeconds(Program.Settings.TimerPeriodInSec),
                logger, DoTime);
        }

        private async Task DoTime()
        {
            await CheckEquity();
        }

        private async Task CheckEquity()
        {
            try
            {
                const string totalEquitySymbol = "USD Total";

                var settings = await _settingWriter.GetAsync(ObserverSettingsNoSqlEntity.GeneratePartitionKey(),
                    ObserverSettingsNoSqlEntity.GenerateRowKey());

                if (settings == null)
                    return;

                var balances = await _balanceExtractor.GetBinanceMarginBalancesAsync();
                var totalBalance = balances.Balances.Sum(balance =>
                    _indexPricesClient.GetIndexPriceByAssetVolumeAsync(balance.Symbol, balance.PositiveBalance).Item2);

                if (settings.MaximumExchangeBalanceUsd > totalBalance &&
                    totalBalance > settings.MinimalExchangeBalanceUsd)
                {
                    await _helper.ResetMonitor(totalEquitySymbol);
                    return;
                }

                if (totalBalance < settings.MinimalExchangeBalanceUsd)
                {
                    var diff = settings.MinimalExchangeBalanceUsd - totalBalance;
                    await _helper.AddToMonitor(totalEquitySymbol, diff, "Total balance too low",
                        $"Total balance is {totalBalance} USD. Min balance set at {settings.MinimalExchangeBalanceUsd}",
                        "Equity");
                }

                if (totalBalance > settings.MaximumExchangeBalanceUsd)
                {
                    var diff = totalBalance - settings.MaximumExchangeBalanceUsd;
                    await _helper.AddToMonitor(totalEquitySymbol, diff, "Total balance too high",
                        $"Total balance is {totalBalance} USD. Max balance set at {settings.MaximumExchangeBalanceUsd}",
                        "Equity");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to check binance equity");
            }
        }


        public void Start()
        {
            _timer.Start();
        }
    }
}