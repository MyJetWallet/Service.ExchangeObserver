using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyJetWallet.Domain.ExternalMarketApi;
using MyJetWallet.Domain.ExternalMarketApi.Dto;
using MyJetWallet.Sdk.Service.Tools;
using MyNoSqlServer.Abstractions;
using Service.ExchangeGateway.Grpc;
using Service.ExchangeGateway.Grpc.Models.Exchange;
using Service.ExchangeObserver.Domain.Models;
using Service.ExchangeObserver.Domain.Models.NoSql;
using Service.ExchangeObserver.Postgres;
using Service.ExchangeObserver.Services;
using Service.IndexPrices.Client;

namespace Service.ExchangeObserver.Jobs
{
    public class ExchangeCheckerJob : IStartable
    {
        private readonly IIndexPricesClient _indexPricesClient;
        private readonly ILogger<ExchangeCheckerJob> _logger;
        private readonly MyTaskTimer _timer;

        private readonly IMyNoSqlServerDataWriter<BinanceExchangeAssetNoSqlEntity> _assetWriter;
        private readonly IMyNoSqlServerDataWriter<BinanceToFireblocksAssetNoSqlEntity> _bToFbWriter;
        private readonly IMyNoSqlServerDataWriter<FbVaultAccountMapNoSqlEntity> _vaultsWriter;
        private readonly IMyNoSqlServerDataWriter<ObserverSettingsNoSqlEntity> _settingWriter;
        private readonly IMyNoSqlServerDataWriter<TransfersMonitorNoSqlEntity> _monitoringWriter;

        private readonly IBalanceExtractor _balanceExtractor;
        private readonly IExchangeGateway _exchangeGateway;
        private readonly DbContextOptionsBuilder<DatabaseContext> _dbContextOptionsBuilder;
        private readonly IExternalMarket _externalMarket;

        public ExchangeCheckerJob(IIndexPricesClient indexPricesClient, ILogger<ExchangeCheckerJob> logger,
            IMyNoSqlServerDataWriter<BinanceExchangeAssetNoSqlEntity> assetWriter, IBalanceExtractor balanceExtractor,
            IMyNoSqlServerDataWriter<BinanceToFireblocksAssetNoSqlEntity> bToFbWriter,
            IMyNoSqlServerDataWriter<FbVaultAccountMapNoSqlEntity> vaultsWriter,
            IMyNoSqlServerDataWriter<ObserverSettingsNoSqlEntity> settingWriter, IExchangeGateway exchangeGateway,
            IMyNoSqlServerDataWriter<TransfersMonitorNoSqlEntity> monitoringWriter,
            DbContextOptionsBuilder<DatabaseContext> dbContextOptionsBuilder, IExternalMarket externalMarket)
        {
            _indexPricesClient = indexPricesClient;
            _logger = logger;
            _assetWriter = assetWriter;
            _balanceExtractor = balanceExtractor;
            _bToFbWriter = bToFbWriter;
            _vaultsWriter = vaultsWriter;
            _settingWriter = settingWriter;
            _exchangeGateway = exchangeGateway;
            _monitoringWriter = monitoringWriter;
            _dbContextOptionsBuilder = dbContextOptionsBuilder;
            _externalMarket = externalMarket;

            _timer = MyTaskTimer.Create<ExchangeCheckerJob>(TimeSpan.FromSeconds(Program.Settings.TimerPeriodInSec),
                logger, DoTime);
        }

        private async Task DoTime()
        {
            await CheckExchangeBorrows();
            await CheckExchangeBalance();
        }

        private async Task CheckExchangeBorrows()
        {
            var marginBalances = await _balanceExtractor.GetBinanceMarginBalancesAsync();
            var mainBalances = await _balanceExtractor.GetBinanceMainBalancesAsync();
            var fbBalances = await _balanceExtractor.GetFireblocksBalancesAsync();

            var borrowedPositions = marginBalances.Balances.Where(t => Math.Abs(t.Borrowed) > 0).ToList();

            foreach (var borrowedPosition in borrowedPositions)
            {
                var asset = await _assetWriter.GetAsync(
                    BinanceExchangeAssetNoSqlEntity.GeneratePartitionKey(),
                    BinanceExchangeAssetNoSqlEntity.GenerateRowKey(borrowedPosition.Symbol));

                if (asset == null)
                {
                    asset = BinanceExchangeAssetNoSqlEntity.Create(borrowedPosition.Symbol, 0);
                    await _assetWriter.InsertOrReplaceAsync(asset);
                }

                if (asset.LockedUntil < DateTime.UtcNow)
                {
                    await _monitoringWriter.DeleteAsync(TransfersMonitorNoSqlEntity.GeneratePartitionKey(),
                        TransfersMonitorNoSqlEntity.GenerateRowKey(borrowedPosition.Symbol));
                }


                var borrowedBalance = borrowedPosition.Borrowed;

                var marginBalance = borrowedPosition.Balance;
                var paymentAmount = borrowedBalance > marginBalance
                    ? borrowedBalance - marginBalance
                    : marginBalance;

                try
                {
                    var exResult = await _externalMarket.MakeRepayAsync(new MakeRepayRequest
                    {
                        Symbol = borrowedPosition.Symbol,
                        Volume = paymentAmount,
                        ExchangeName = "Binance"
                    });
                    if (!exResult.IsError)
                    {
                        borrowedBalance -= paymentAmount;
                        await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
                        await context.UpsertAsync(new[]
                        {
                            new ObserverTransfer
                            {
                                Id = Guid.NewGuid().ToString(),
                                From = "BinanceMarginBorrowed",
                                To = "BinanceMargin",
                                Asset = borrowedPosition.Symbol,
                                Amount = paymentAmount,
                                IndexPrice = _indexPricesClient.GetIndexPriceByAssetAsync(borrowedPosition.Symbol)
                                    .UsdPrice,
                                Reason =
                                    $"Borrowed {borrowedBalance} {borrowedPosition.Symbol}. Repayment from Binance Margin. Payment amount {paymentAmount}",
                                TimeStamp = DateTime.UtcNow
                            }
                        });
                    }
                    else
                    {
                        _logger.LogError("Unable to make repay at Binance Margin. Asset {asset}. Error {error}",
                            asset.AssetSymbol, exResult.ErrorMessage);
                        break;
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to make repay at Binance Margin. Asset {asset}. Error {error}",
                        asset.AssetSymbol, e.Message);
                    break;
                }

                if (borrowedBalance == 0)
                {
                    await _monitoringWriter.DeleteAsync(TransfersMonitorNoSqlEntity.GeneratePartitionKey(),
                        TransfersMonitorNoSqlEntity.GenerateRowKey(borrowedPosition.Symbol));
                    continue;
                }

                var mainBalance = mainBalances.Balances.FirstOrDefault(t => t.Symbol == borrowedPosition.Symbol);
                if (mainBalance != null)
                {
                    paymentAmount = borrowedBalance > mainBalance.Balance
                        ? borrowedBalance - mainBalance.Balance
                        : mainBalance.Balance;

                    try
                    {
                        var result = await _exchangeGateway.TransferBinanceMainToMargin(
                            new TransferBinanceMainToMarginRequest
                            {
                                AssetSymbol = borrowedPosition.Symbol,
                                Amount = paymentAmount,
                                RequestId = Guid.NewGuid().ToString()
                            });
                        if (result.Error == null)
                        {
                            borrowedBalance -= paymentAmount;
                            await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
                            await context.UpsertAsync(new[]
                            {
                                new ObserverTransfer
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    From = "BinanceMain",
                                    To = "BinanceMargin",
                                    Asset = borrowedPosition.Symbol,
                                    Amount = paymentAmount,
                                    IndexPrice = _indexPricesClient.GetIndexPriceByAssetAsync(borrowedPosition.Symbol)
                                        .UsdPrice,
                                    Reason =
                                        $"Borrowed {borrowedBalance} {borrowedPosition.Symbol}. Repayment from Binance Main. Payment amount {paymentAmount}",
                                    TimeStamp = DateTime.UtcNow
                                }
                            });
                        }
                        else
                        {
                            _logger.LogError(
                                "Unable to transfer monet from Binance Main to Margin. Asset {asset}. Error {error}",
                                asset.AssetSymbol, result.Error.Message);
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e,
                            "Unable to transfer monet from Binance Main to Margin. Asset {asset}. Error {error}",
                            asset.AssetSymbol, e.Message);
                        break;
                    }
                }

                if (borrowedBalance == 0)
                {
                    await _monitoringWriter.DeleteAsync(TransfersMonitorNoSqlEntity.GeneratePartitionKey(),
                        TransfersMonitorNoSqlEntity.GenerateRowKey(borrowedPosition.Symbol));
                    continue;
                }

                if (asset.LockedUntil >= DateTime.UtcNow)
                    continue;

                var fbAssets =
                    await _bToFbWriter.GetAsync(
                        BinanceToFireblocksAssetNoSqlEntity.GeneratePartitionKey(borrowedPosition.Symbol));
                var vaults = await _vaultsWriter.GetAsync();

                foreach (var fbAsset in fbAssets)
                {
                    if (fbAsset.MinTransferAmount > borrowedBalance)
                        continue;

                    var vaultAccounts = vaults.Where(t =>
                        t.FireblocksAssetsWithBalances.Any(assetAndBalance =>
                            assetAndBalance.Asset == fbAsset.FireblocksAsset)).ToList();

                    foreach (var vaultAccount in vaultAccounts)
                    {
                        var fbBalance = fbBalances.Balances.FirstOrDefault(t =>
                            t.Asset == fbAsset.FireblocksAsset && t.VaultAccount == vaultAccount.VaultAccountId &&
                            t.Network == fbAsset.FireblocksNetwork);
                        if (fbBalance != null)
                        {
                            var minBalance = vaultAccount.FireblocksAssetsWithBalances
                                ?.FirstOrDefault(t => t.Asset == fbAsset.FireblocksAsset)?.MinBalance ?? 0m;

                            var freeFbBalance = fbBalance.Amount - minBalance;

                            paymentAmount = borrowedBalance > freeFbBalance
                                ? borrowedBalance - freeFbBalance
                                : freeFbBalance;

                            try
                            {
                                var result = await _exchangeGateway.TransferFireblocksToBinance(
                                    new TransferFireblocksToBinanceRequest
                                    {
                                        AssetSymbol = fbAsset.FireblocksAsset,
                                        AssetNetwork = fbAsset.FireblocksNetwork,
                                        VaultAccountId = vaultAccount.VaultAccountId,
                                        Amount = paymentAmount,
                                        RequestId = Guid.NewGuid().ToString()
                                    });

                                if (result.Error == null)
                                {
                                    borrowedBalance -= paymentAmount;

                                    asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                                    await _assetWriter.InsertOrReplaceAsync(asset);

                                    await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
                                    await context.UpsertAsync(new[]
                                    {
                                        new ObserverTransfer
                                        {
                                            Id = Guid.NewGuid().ToString(),
                                            From = "Fireblocks",
                                            To = "BinanceMain",
                                            Asset = borrowedPosition.Symbol,
                                            Amount = paymentAmount,
                                            IndexPrice = _indexPricesClient
                                                .GetIndexPriceByAssetAsync(borrowedPosition.Symbol).UsdPrice,
                                            Reason =
                                                $"Borrowed {borrowedBalance} {borrowedPosition.Symbol}. Repayment from Fireblocks. Payment amount {paymentAmount}",
                                            TimeStamp = DateTime.UtcNow
                                        }
                                    });
                                }
                                else
                                {
                                    _logger.LogError(
                                        "Unable to transfer monet from Fireblocks to Binance. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
                                        asset.AssetSymbol, vaultAccount.VaultAccountId, result.Error.Message);
                                    break;
                                }
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e,
                                    "Unable to transfer monet from Fireblocks to Binance. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
                                    asset.AssetSymbol, vaultAccount.VaultAccountId, e.Message);
                                break;
                            }
                        }

                        if (borrowedBalance == 0)
                        {
                            await _monitoringWriter.DeleteAsync(TransfersMonitorNoSqlEntity.GeneratePartitionKey(),
                                TransfersMonitorNoSqlEntity.GenerateRowKey(borrowedPosition.Symbol));
                            break;
                        }
                    }
                }

                if (borrowedBalance != 0)
                {
                    await _monitoringWriter.InsertOrReplaceAsync(
                        TransfersMonitorNoSqlEntity.Create(borrowedPosition.Symbol, borrowedBalance, DateTime.UtcNow));
                    //_logger.LogWarning("Unable to repay for borrowed asset {asset}. Unpaid amount is {borrowed}", borrowedPosition.Symbol, borrowedPosition.Borrowed);
                }
            }
        }

        private async Task CheckExchangeBalance()
        {
            var settings = await _settingWriter.GetAsync(ObserverSettingsNoSqlEntity.GeneratePartitionKey(),
                ObserverSettingsNoSqlEntity.GenerateRowKey());

            if (settings == null)
                return;

            var balances = await _balanceExtractor.GetBinanceMarginBalancesAsync();

            var totalBalance = balances.Balances.Sum(balance =>
                _indexPricesClient.GetIndexPriceByAssetVolumeAsync(balance.Symbol, balance.Balance).Item2);

            if (settings.MaximumExchangeBalanceUsd > totalBalance && totalBalance > settings.MinimalExchangeBalanceUsd)
                return;

            var assets = (await _assetWriter.GetAsync()).OrderByDescending(t => t.Weight).ToList();
            if (assets.Any(t => t.LockedUntil >= DateTime.UtcNow))
                return;

            var vaults = await _vaultsWriter.GetAsync();
            var fbBalances = await _balanceExtractor.GetFireblocksBalancesAsync();
            var marginBalances = await _balanceExtractor.GetBinanceMarginBalancesAsync();

            if (totalBalance < settings.MinimalExchangeBalanceUsd)
            {
                var diff = settings.MinimalExchangeBalanceUsd - totalBalance;

                foreach (var asset in assets)
                {
                    var fbAssets =
                        await _bToFbWriter.GetAsync(
                            BinanceToFireblocksAssetNoSqlEntity.GeneratePartitionKey(asset.AssetSymbol));

                    foreach (var fbAsset in fbAssets)
                    {
                        if (diff == 0)
                            return;

                        var vaultAccounts = vaults.Where(t =>
                            t.FireblocksAssetsWithBalances.Any(assetAndBalance =>
                                assetAndBalance.Asset == fbAsset.FireblocksAsset)).ToList();
                        foreach (var vaultAccount in vaultAccounts)
                        {
                            var minBalance = vaultAccount.FireblocksAssetsWithBalances
                                ?.FirstOrDefault(t => t.Asset == fbAsset.FireblocksAsset)?.MinBalance ?? 0m;
                            var fbBalance = fbBalances.Balances.FirstOrDefault(t =>
                                t.Asset == fbAsset.FireblocksAsset && t.VaultAccount == vaultAccount.VaultAccountId &&
                                t.Network == fbAsset.FireblocksNetwork);

                            var indexPrice = _indexPricesClient
                                .GetIndexPriceByAssetAsync(asset.AssetSymbol).UsdPrice;

                            var freeUsd = (fbBalance.Amount - minBalance) * indexPrice;

                            var paymentAmountUsd = diff > freeUsd
                                ? diff - freeUsd
                                : freeUsd;

                            var paymentAmount = paymentAmountUsd / indexPrice;

                            try
                            {
                                var result = await _exchangeGateway.TransferFireblocksToBinance(
                                    new TransferFireblocksToBinanceRequest
                                    {
                                        AssetSymbol = fbAsset.FireblocksAsset,
                                        AssetNetwork = fbAsset.FireblocksNetwork,
                                        VaultAccountId = vaultAccount.VaultAccountId,
                                        Amount = paymentAmount,
                                        RequestId = Guid.NewGuid().ToString()
                                    });

                                if (result.Error == null)
                                {
                                    asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                                    await _assetWriter.InsertOrReplaceAsync(asset);
                                    diff -= paymentAmountUsd;

                                    await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
                                    await context.UpsertAsync(new[]
                                    {
                                        new ObserverTransfer
                                        {
                                            Id = Guid.NewGuid().ToString(),
                                            From = "Fireblocks",
                                            To = "BinanceMain",
                                            Asset = fbAsset.FireblocksAsset,
                                            Amount = paymentAmount,
                                            IndexPrice = indexPrice,
                                            Reason =
                                                $"Total balance is less than min. {diff} USD required. Payment from FB {paymentAmount} {fbAsset.FireblocksAsset}",
                                            TimeStamp = DateTime.UtcNow
                                        }
                                    });
                                }
                                else
                                {
                                    _logger.LogError(
                                        "Unable to transfer monet from Fireblocks to Binance. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
                                        asset.AssetSymbol, vaultAccount.VaultAccountId, result.Error.Message);
                                    break;
                                }
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e,
                                    "Unable to transfer monet from Fireblocks to Binance. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
                                    asset.AssetSymbol, vaultAccount.VaultAccountId, e.Message);
                                break;
                            }
                        }
                    }
                }
            }

            if (totalBalance > settings.MaximumExchangeBalanceUsd)
            {
                var diff = totalBalance - settings.MaximumExchangeBalanceUsd;

                foreach (var asset in assets)
                {
                    if (diff == 0)
                        return;

                    var balance = marginBalances.Balances.FirstOrDefault(t => t.Symbol == asset.AssetSymbol)?.Balance ??
                                  0m;

                    var indexPrice = _indexPricesClient
                        .GetIndexPriceByAssetAsync(asset.AssetSymbol).UsdPrice;

                    var balanceInUsd = balance * indexPrice;

                    var paymentAmountUsd = diff > balanceInUsd
                        ? diff - balanceInUsd
                        : balanceInUsd;

                    var paymentAmount = paymentAmountUsd / indexPrice;

                    var fbAssets =
                        await _bToFbWriter.GetAsync(
                            BinanceToFireblocksAssetNoSqlEntity.GeneratePartitionKey(asset.AssetSymbol));
                    var fbAsset = fbAssets.FirstOrDefault();
                    var vaultAccount = vaults.FirstOrDefault(t =>
                        t.FireblocksAssetsWithBalances.Any(assetAndBalance =>
                            assetAndBalance.Asset == fbAsset.FireblocksAsset));
                    try
                    {
                        var result =
                            await _exchangeGateway.TransferFromBinanceMarginToFireblocks(
                                new TransferFromBinanceMarginToFireblocksRequest
                                {
                                    AssetSymbol = fbAsset.FireblocksAsset,
                                    AssetNetwork = fbAsset.FireblocksNetwork,
                                    VaultAccountId = vaultAccount.VaultAccountId,
                                    Amount = paymentAmount,
                                    RequestId = Guid.NewGuid().ToString()
                                });

                        if (result.Error == null)
                        {
                            asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                            await _assetWriter.InsertOrReplaceAsync(asset);
                            diff -= paymentAmountUsd;

                            await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
                            await context.UpsertAsync(new[]
                            {
                                new ObserverTransfer
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    From = "Fireblocks",
                                    To = "BinanceMain",
                                    Asset = asset.AssetSymbol,
                                    Amount = paymentAmount,
                                    IndexPrice = indexPrice,
                                    Reason =
                                        $"Total balance is more than max. Diff: {diff} USD. Payment from binance to FB: {paymentAmount} {asset.AssetSymbol}",
                                    TimeStamp = DateTime.UtcNow
                                }
                            });
                        }
                        else
                        {
                            _logger.LogError(
                                "Unable to transfer monet from Binance to Fireblocks. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
                                asset.AssetSymbol, vaultAccount.VaultAccountId, result.Error.Message);
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e,
                            "Unable to transfer monet from Binance to Fireblocks. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
                            asset.AssetSymbol, vaultAccount.VaultAccountId, e.Message);
                        break;
                    }
                }
            }
        }

        public void Start()
        {
            _timer.Start();
        }
    }
}