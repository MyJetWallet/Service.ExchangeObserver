using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using MyJetWallet.Domain.ExternalMarketApi;
using MyJetWallet.Domain.ExternalMarketApi.Dto;
using MyJetWallet.Domain.ExternalMarketApi.Models;
using MyJetWallet.Sdk.Service;
using MyJetWallet.Sdk.Service.Tools;
using MyNoSqlServer.Abstractions;
using Service.ExchangeGateway.Grpc;
using Service.ExchangeGateway.Grpc.Models.Exchange;
using Service.ExchangeObserver.Domain.Models;
using Service.ExchangeObserver.Domain.Models.NoSql;
using Service.ExchangeObserver.Services;
using Service.IndexPrices.Client;

namespace Service.ExchangeObserver.Jobs
{
    public class BorrowCheckerJob : IStartable
    {
        private readonly IIndexPricesClient _indexPricesClient;
        private readonly ILogger<BorrowCheckerJob> _logger;
        private readonly MyTaskTimer _timer;

        private readonly IMyNoSqlServerDataWriter<FbVaultAccountMapNoSqlEntity> _vaultsWriter;
        private readonly IMyNoSqlServerDataWriter<ObserverSettingsNoSqlEntity> _settingWriter;

        private readonly IBalanceExtractor _balanceExtractor;
        private readonly IExchangeGateway _exchangeGateway;
        private readonly IExternalMarket _externalMarket;

        private readonly ObserverJobHelper _helper;

        public BorrowCheckerJob(IIndexPricesClient indexPricesClient, 
            ILogger<BorrowCheckerJob> logger,
            IBalanceExtractor balanceExtractor,
            IMyNoSqlServerDataWriter<FbVaultAccountMapNoSqlEntity> vaultsWriter,
            IMyNoSqlServerDataWriter<ObserverSettingsNoSqlEntity> settingWriter, 
            IExchangeGateway exchangeGateway, 
            IExternalMarket externalMarket, 
            ObserverJobHelper helper)
        {
            _indexPricesClient = indexPricesClient;
            _logger = logger;
            _balanceExtractor = balanceExtractor;
            _vaultsWriter = vaultsWriter;
            _settingWriter = settingWriter;
            _exchangeGateway = exchangeGateway;
            _externalMarket = externalMarket;
            _helper = helper;

            _timer = MyTaskTimer.Create<BorrowCheckerJob>(TimeSpan.FromSeconds(Program.Settings.TimerPeriodInSec),
                logger, DoTime);
        }

        private async Task DoTime()
        {
            await CheckExchangeBorrows();
        }

        private async Task CheckExchangeBorrows()
        {
            var assets = await _helper.GetAssets();
            var marginBalances = await _balanceExtractor.GetBinanceMarginBalancesAsync();
            var mainBalances = await _balanceExtractor.GetBinanceMainBalancesAsync();
            var fbBalances = await _balanceExtractor.GetFireblocksBalancesAsync();
            var vaults = await _vaultsWriter.GetAsync();
            
            var borrowedPositions = marginBalances.Balances?.Where(t => Math.Abs(t.Borrowed) > 0).ToList() ??
                                    new List<ExchangeBalance>();

            foreach (var borrowedPosition in borrowedPositions)
            {
                _logger.LogInformation("Detected borrowed balance at binance. Asset {asset}. Borrowed balance {balance}", borrowedPosition.Symbol, borrowedPosition.Borrowed);
                var borrowedBalance = borrowedPosition.Borrowed;
                try
                {
                    var paidAmount = await PayFromMarginAccount(borrowedPosition);
                    borrowedBalance -= paidAmount;

                    if (borrowedBalance == 0)
                    {
                        await _helper.ResetMonitor(borrowedPosition.Symbol);
                        continue;
                    }

                    paidAmount = await TransferFromMainAccount(borrowedPosition.Symbol, borrowedBalance, assets, mainBalances.Balances ?? new List<ExchangeBalance>());
                    borrowedBalance -= paidAmount;
                    if (borrowedBalance == 0)
                    {
                        await _helper.ResetMonitor(borrowedPosition.Symbol);
                        continue;
                    }

                    bool isProcessed;
                    (paidAmount, isProcessed)= await TransferFromFireblocks(borrowedPosition.Symbol, borrowedBalance, assets, fbBalances.Balances ?? new List<FbBalance>(), vaults);
                    borrowedBalance -= paidAmount;
                    if (borrowedBalance == 0)
                    {
                        await _helper.ResetMonitor(borrowedPosition.Symbol);
                        continue;
                    }
                    if (borrowedBalance != 0)
                    {
                        await _helper.AddToMonitor(borrowedPosition.Symbol, borrowedBalance, isProcessed ?"Not enough balance" : "Asset payment in process", "", "Depth");
                        _logger.LogWarning($"Unable to repay for borrowed asset {borrowedPosition.Symbol}. Unpaid amount is {borrowedBalance}. Asset is processed {isProcessed}");
                    }
                }
                catch (Exception e)
                {
                    await _helper.AddToMonitor(borrowedPosition.Symbol, borrowedBalance, "Error", e.Message, "Depth");
                    _logger.LogError(e, "Unable to repay for borrowed asset {asset}. Unpaid amount is {borrowed}", borrowedPosition.Symbol, borrowedBalance);
                }
            }
        }

        #region BorrowRepayMethods
        private async Task<decimal> PayFromMarginAccount(ExchangeBalance borrowedPosition)
        {
            var borrowedBalance = borrowedPosition.Borrowed;
            var marginBalance = borrowedPosition.PositiveBalance;
            var paymentAmount = Math.Min(marginBalance, borrowedBalance);
            if (paymentAmount == 0)
                return 0;
            
            _logger.LogInformation("Started executing repay at Binance Margin. Asset {asset}. Borrowed balance {balance}. Payment amount {amount}", borrowedPosition.Symbol, borrowedBalance, paymentAmount);
            try
            {
                var exResult = await _externalMarket.MakeRepayAsync(new MakeRepayRequest
                {
                    Symbol = borrowedPosition.Symbol,
                    Volume = paymentAmount,
                    ExchangeName = "Binance"
                });
                if (exResult.IsError)
                    throw new Exception($"Unable to repay. Error {exResult.ErrorMessage}");

                var transfer = new ObserverTransfer
                {
                    From = "BinanceMargin",
                    To = "BinanceMarginBorrowed",
                    Asset = borrowedPosition.Symbol,
                    Amount = paymentAmount,
                    IndexPrice = _indexPricesClient.GetIndexPriceByAssetAsync(borrowedPosition.Symbol)
                        .UsdPrice,
                    Reason =
                        $"Borrowed {borrowedBalance} {borrowedPosition.Symbol}. Repay from Binance Margin. Amount {paymentAmount}",
                    TimeStamp = DateTime.UtcNow
                };
                await _helper.SaveTransfer(transfer);

                _logger.LogInformation("Executed repay at Binance Margin. Asset {asset}. Borrowed balance {balance}. Payment amount {amount}", borrowedPosition.Symbol, borrowedBalance, paymentAmount);

                return paymentAmount;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to make repay at Binance Margin. Asset {asset}. Error {error}",
                    borrowedPosition.Symbol, e.Message);
                throw;
            }

        }
        private async Task<decimal> TransferFromMainAccount(string symbol, decimal borrowedBalance,
            List<ObserverAsset> assets, List<ExchangeBalance> balances)
        {
            var asset = assets.FirstOrDefault(t => t.BinanceSymbol == symbol && t.IsEnabled);
            if(asset == null)
                return 0;
            
            var mainBalance = balances.FirstOrDefault(t => t.Symbol == symbol);
            if (mainBalance == null)
                return 0;

            var paymentAmount = Math.Min(borrowedBalance, mainBalance.Balance);
            if (paymentAmount == 0)
                return 0;
            
            _logger.LogInformation("Started executing transfer binance main to margin. Asset {asset}. Borrowed balance {balance}. Payment amount {amount}", symbol, borrowedBalance, paymentAmount);

            try
            {
                var result = await _exchangeGateway.TransferBinanceMainToMargin(
                    new TransferBinanceMainToMarginRequest
                    {
                        AssetSymbol = symbol,
                        Amount = paymentAmount,
                        RequestId = Guid.NewGuid().ToString()
                    });
                if (result.Error != null)
                    throw new Exception($"Unable to transfer binance main to margin.  Error {result.Error.ToJson()}");

                var transfer = new ObserverTransfer
                {
                    From = "BinanceMain",
                    To = "BinanceMargin",
                    Asset = symbol,
                    Amount = paymentAmount,
                    IndexPrice = _indexPricesClient.GetIndexPriceByAssetAsync(symbol)
                        .UsdPrice,
                    Reason =
                        $"Borrowed {borrowedBalance} {symbol}. Transfer from Binance Main to Margin. Amount {paymentAmount}",
                    TimeStamp = DateTime.UtcNow
                };
                await _helper.SaveTransfer(transfer);
            
                _logger.LogInformation("Executed transfer binance main to margin. Asset {asset}. Borrowed balance {balance}. Payment amount {amount}", symbol, borrowedBalance, paymentAmount);

                return paymentAmount;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to transfer binance main to margin. Asset {asset}. Borrowed {borrowedBalance}. Balance {mainBalance} Error {error}",
                    symbol, borrowedBalance, mainBalance.Balance, e.Message);
                throw;
            }
        }
        private async Task<(decimal paidAmount, bool isProcessed)> TransferFromFireblocks(string symbol,
            decimal borrowedBalance, List<ObserverAsset> assets, List<FbBalance> balances, List<FbVaultAccountMapNoSqlEntity> vaultAccounts)
        {
            var hasLockedAssets = assets.Any(t => t.BinanceSymbol == symbol && t.LockedUntil > DateTime.UtcNow);
            if (hasLockedAssets)
                return (0, false);

            _logger.LogInformation("Started executing transfer from fireblocks. Asset {asset}. Borrowed balance {balance}.", symbol, borrowedBalance);

            //Try to process full amount
            foreach (var asset in assets.Where(t => t.BinanceSymbol == symbol && t.IsEnabled).OrderByDescending(t => t.Weight))
            {
                foreach (var vaultAccount in vaultAccounts.OrderByDescending(t=>t.Weight))
                {
                    var balance = balances.FirstOrDefault(t =>
                        t.VaultAccount == vaultAccount.VaultAccountId && t.Asset == asset.AssetSymbol &&
                        t.Network == asset.Network)?.Amount ?? 0m;

                    var minBalance = vaultAccount.FireblocksAssetsWithBalances
                        .FirstOrDefault(t => t.Asset == asset.AssetSymbol)?.MinBalance ?? 0m;

                    var minTransfer = asset.MinTransferAmount;
                    var paymentAmount = borrowedBalance + asset.FireblockToBinanceFee;

                    if (balance - minBalance < paymentAmount)
                        continue;
                    if (paymentAmount < minTransfer)
                        continue;

                    paymentAmount = await ExecuteTransferToFireblocks(asset, borrowedBalance, vaultAccount.VaultAccountId, paymentAmount, balance);
                    await _helper.LockAsset(asset.AssetSymbol, asset.LockTimeInMin);
                    
                    _logger.LogInformation("Executed full transfer from fireblocks. Asset {asset}. Borrowed balance {balance}. Payment amount {amount}", symbol, borrowedBalance, paymentAmount);

                    return (paymentAmount, true);
                }
            }

            //Try to process partial amount
            var totalPaidAmount = 0m;
            foreach (var asset in assets.Where(t => t.BinanceSymbol == symbol && t.IsEnabled).OrderByDescending(t => t.Weight))
            {
                if(borrowedBalance == 0)
                    break;
                
                foreach (var vaultAccount in vaultAccounts.OrderByDescending(t=>t.Weight))
                {
                    if(borrowedBalance == 0)
                        break;
                    
                    var balance = balances.FirstOrDefault(t =>
                        t.VaultAccount == vaultAccount.VaultAccountId && t.Asset == asset.AssetSymbol &&
                        t.Network == asset.Network)?.Amount ?? 0m;

                    var minBalance = vaultAccount.FireblocksAssetsWithBalances
                        .FirstOrDefault(t => t.Asset == asset.AssetSymbol)?.MinBalance ?? 0m;

                    var minTransfer = asset.MinTransferAmount;
                    
                    var paymentAmount = Math.Min(borrowedBalance + asset.FireblockToBinanceFee, balance-minBalance);
                    if (paymentAmount < minTransfer)
                        continue;
                    
                    paymentAmount = await ExecuteTransferToFireblocks(asset, borrowedBalance, vaultAccount.VaultAccountId, paymentAmount, balance);
                    await _helper.LockAsset(asset.AssetSymbol, asset.LockTimeInMin);

                    borrowedBalance -= paymentAmount;
                    totalPaidAmount += paymentAmount;
                    
                    _logger.LogInformation("Executed partial transfer from fireblocks. Asset {asset}. Borrowed balance {balance}. Payment amount {amount}", symbol, borrowedBalance, paymentAmount);
                }
            }

            return (totalPaidAmount, true);
        }

        private async Task<decimal> ExecuteTransferToFireblocks(ObserverAsset asset, decimal borrowedBalance, int vaultAccountId, decimal paymentAmount, decimal balance)
        {
            try
            {
                var result = await _exchangeGateway.TransferFireblocksToBinance(
                    new()
                    {
                        AssetSymbol = asset.AssetSymbol,
                        AssetNetwork = asset.Network,
                        VaultAccountId = vaultAccountId,
                        Amount = paymentAmount,
                        RequestId = Guid.NewGuid().ToString(),
                    });
                if (result.Error == null)
                {
                    var transfer = new ObserverTransfer
                    {
                        From = $"Fireblocks. Vault {vaultAccountId}",
                        To = "Binance",
                        Asset = asset.AssetSymbol,
                        Amount = paymentAmount,
                        IndexPrice = _indexPricesClient.GetIndexPriceByAssetAsync(asset.AssetSymbol)
                            .UsdPrice,
                        Reason =
                            $"Borrowed {borrowedBalance} {asset.AssetSymbol}. Transfer from Fireblocks Vault {vaultAccountId} to Binance. Network {asset.Network}. Amount {paymentAmount}",
                        TimeStamp = DateTime.UtcNow
                    };
                    await _helper.SaveTransfer(transfer);
                }
                else
                {
                    throw new Exception($"Unable to transfer from Fireblocks to Binance.  Error {result.Error.ToJson()}");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Unable to transfer from Fireblocks to Binance. Asset {asset}. Borrowed {borrowedBalance}. Balance {balance} Error {error}. VaultAccount {vaultAccount}",
                    asset.AssetSymbol, borrowedBalance, balance, e.Message, vaultAccountId);
                throw;
            }

            return paymentAmount;
        }


        #endregion

        // private async Task CheckExchangeBalance()
        // {
        //     var settings = await _settingWriter.GetAsync(ObserverSettingsNoSqlEntity.GeneratePartitionKey(),
        //         ObserverSettingsNoSqlEntity.GenerateRowKey());
        //
        //     if (settings == null)
        //         return;
        //
        //     var balances = await _balanceExtractor.GetBinanceMarginBalancesAsync();
        //
        //     var totalBalance = balances.Balances.Sum(balance =>
        //         _indexPricesClient.GetIndexPriceByAssetVolumeAsync(balance.Symbol, balance.Balance).Item2);
        //
        //     if (settings.MaximumExchangeBalanceUsd > totalBalance && totalBalance > settings.MinimalExchangeBalanceUsd)
        //         return;
        //
        //     var assets = await GetAssets();
        //     if (assets.Any(t => t.LockedUntil >= DateTime.UtcNow))
        //         return;
        //
        //     var marginBalances = await _balanceExtractor.GetBinanceMarginBalancesAsync();
        //     var mainBalances = await _balanceExtractor.GetBinanceMainBalancesAsync();
        //     var fbBalances = await _balanceExtractor.GetFireblocksBalancesAsync();
        //     var vaults = await _vaultsWriter.GetAsync();
        //
        //     if (totalBalance < settings.MinimalExchangeBalanceUsd)
        //     {
        //         var diff = settings.MinimalExchangeBalanceUsd - totalBalance;
        //
        //         foreach (var asset in assets)
        //         {
        //             var fbAssets =
        //                 await _bToFbWriter.GetAsync(
        //                     BinanceToFireblocksAssetNoSqlEntity.GeneratePartitionKey(asset.AssetSymbol));
        //
        //             foreach (var fbAsset in fbAssets)
        //             {
        //                 if (diff == 0)
        //                     return;
        //
        //                 var vaultAccounts = vaults.Where(t =>
        //                     t.FireblocksAssetsWithBalances.Any(assetAndBalance =>
        //                         assetAndBalance.Asset == fbAsset.FireblocksAsset)).ToList();
        //
        //                 foreach (var vaultAccount in vaultAccounts)
        //                 {
        //                     var minBalance = vaultAccount.FireblocksAssetsWithBalances
        //                         ?.FirstOrDefault(t => t.Asset == fbAsset.FireblocksAsset)?.MinBalance ?? 0m;
        //                     var fbBalance = fbBalances.Balances.FirstOrDefault(t =>
        //                         t.Asset == fbAsset.FireblocksAsset && t.VaultAccount == vaultAccount.VaultAccountId &&
        //                         t.Network == fbAsset.FireblocksNetwork);
        //
        //                     if (fbBalance == null)
        //                         continue;
        //
        //                     var indexPrice = _indexPricesClient
        //                         .GetIndexPriceByAssetAsync(asset.AssetSymbol).UsdPrice;
        //
        //                     var freeUsd = (fbBalance.Amount - minBalance) * indexPrice;
        //
        //                     var paymentAmountUsd = diff > freeUsd
        //                         ? diff - freeUsd
        //                         : freeUsd;
        //
        //                     var paymentAmount = paymentAmountUsd / indexPrice;
        //
        //                     try
        //                     {
        //                         var result = await _exchangeGateway.TransferFireblocksToBinance(
        //                             new TransferFireblocksToBinanceRequest
        //                             {
        //                                 AssetSymbol = fbAsset.FireblocksAsset,
        //                                 AssetNetwork = fbAsset.FireblocksNetwork,
        //                                 VaultAccountId = vaultAccount.VaultAccountId,
        //                                 Amount = paymentAmount,
        //                                 RequestId = Guid.NewGuid().ToString()
        //                             });
        //
        //                         if (result.Error == null)
        //                         {
        //                             asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
        //                             await _assetWriter.InsertOrReplaceAsync(asset);
        //                             diff -= paymentAmountUsd;
        //
        //                             await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
        //                             await context.UpsertAsync(new[]
        //                             {
        //                                 new ObserverTransfer
        //                                 {
        //                                     Id = Guid.NewGuid().ToString(),
        //                                     From = "Fireblocks",
        //                                     To = "BinanceMain",
        //                                     Asset = fbAsset.FireblocksAsset,
        //                                     Amount = paymentAmount,
        //                                     IndexPrice = indexPrice,
        //                                     Reason =
        //                                         $"Total balance is less than min. {diff} USD required. Payment from FB {paymentAmount} {fbAsset.FireblocksAsset}",
        //                                     TimeStamp = DateTime.UtcNow
        //                                 }
        //                             });
        //                         }
        //                         else
        //                         {
        //                             _logger.LogError(
        //                                 "Unable to transfer monet from Fireblocks to Binance. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
        //                                 asset.AssetSymbol, vaultAccount.VaultAccountId, result.Error.Message);
        //                             break;
        //                         }
        //                     }
        //                     catch (Exception e)
        //                     {
        //                         _logger.LogError(e,
        //                             "Unable to transfer monet from Fireblocks to Binance. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
        //                             asset.AssetSymbol, vaultAccount.VaultAccountId, e.Message);
        //                         break;
        //                     }
        //                 }
        //             }
        //         }
        //     }
        //
        //     if (totalBalance > settings.MaximumExchangeBalanceUsd)
        //     {
        //         var diff = totalBalance - settings.MaximumExchangeBalanceUsd;
        //
        //         foreach (var asset in assets)
        //         {
        //             if (diff == 0)
        //                 return;
        //
        //             var balance = marginBalances.Balances.FirstOrDefault(t => t.Symbol == asset.AssetSymbol)?.Balance ??
        //                           0m;
        //
        //             var indexPrice = _indexPricesClient
        //                 .GetIndexPriceByAssetAsync(asset.AssetSymbol).UsdPrice;
        //
        //             var balanceInUsd = balance * indexPrice;
        //
        //             var paymentAmountUsd = diff > balanceInUsd
        //                 ? diff - balanceInUsd
        //                 : balanceInUsd;
        //
        //             var paymentAmount = paymentAmountUsd / indexPrice;
        //
        //             var fbAssets =
        //                 await _bToFbWriter.GetAsync(
        //                     BinanceToFireblocksAssetNoSqlEntity.GeneratePartitionKey(asset.AssetSymbol));
        //             var fbAsset = fbAssets.FirstOrDefault();
        //             var vaultAccount = vaults.FirstOrDefault(t =>
        //                 t.FireblocksAssetsWithBalances.Any(assetAndBalance =>
        //                     assetAndBalance.Asset == fbAsset.FireblocksAsset));
        //             try
        //             {
        //                 var result =
        //                     await _exchangeGateway.TransferFromBinanceMarginToFireblocks(
        //                         new TransferFromBinanceMarginToFireblocksRequest
        //                         {
        //                             AssetSymbol = fbAsset.FireblocksAsset,
        //                             AssetNetwork = fbAsset.FireblocksNetwork,
        //                             VaultAccountId = vaultAccount.VaultAccountId,
        //                             Amount = paymentAmount,
        //                             RequestId = Guid.NewGuid().ToString()
        //                         });
        //
        //                 if (result.Error == null)
        //                 {
        //                     asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
        //                     await _assetWriter.InsertOrReplaceAsync(asset);
        //                     diff -= paymentAmountUsd;
        //
        //                     await using var context = new DatabaseContext(_dbContextOptionsBuilder.Options);
        //                     await context.UpsertAsync(new[]
        //                     {
        //                         new ObserverTransfer
        //                         {
        //                             Id = Guid.NewGuid().ToString(),
        //                             From = "Fireblocks",
        //                             To = "BinanceMain",
        //                             Asset = asset.AssetSymbol,
        //                             Amount = paymentAmount,
        //                             IndexPrice = indexPrice,
        //                             Reason =
        //                                 $"Total balance is more than max. Diff: {diff} USD. Payment from binance to FB: {paymentAmount} {asset.AssetSymbol}",
        //                             TimeStamp = DateTime.UtcNow
        //                         }
        //                     });
        //                 }
        //                 else
        //                 {
        //                     _logger.LogError(
        //                         "Unable to transfer monet from Binance to Fireblocks. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
        //                         asset.AssetSymbol, vaultAccount.VaultAccountId, result.Error.Message);
        //                     break;
        //                 }
        //             }
        //             catch (Exception e)
        //             {
        //                 _logger.LogError(e,
        //                     "Unable to transfer monet from Binance to Fireblocks. Asset {asset}. Vault account Id: {vaultAccount} Error {error}",
        //                     asset.AssetSymbol, vaultAccount.VaultAccountId, e.Message);
        //                 break;
        //             }
        //         }
        //     }
        // }


        public void Start()
        {
            _timer.Start();
        }
    }
}