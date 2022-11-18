using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using MyJetWallet.Domain.ExternalMarketApi.Dto;
using MyJetWallet.Sdk.Service.Tools;
using MyNoSqlServer.Abstractions;
using Service.ExchangeObserver.Domain.Models;
using Service.ExchangeObserver.Domain.Models.NoSql;
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

        private readonly IBalanceExtractor _balanceExtractor;
        private readonly IExchangeGateway _exchangeGateway;

        public ExchangeCheckerJob(IIndexPricesClient indexPricesClient, ILogger<ExchangeCheckerJob> logger, IMyNoSqlServerDataWriter<BinanceExchangeAssetNoSqlEntity> assetWriter, IBalanceExtractor balanceExtractor, IMyNoSqlServerDataWriter<BinanceToFireblocksAssetNoSqlEntity> bToFbWriter, IMyNoSqlServerDataWriter<FbVaultAccountMapNoSqlEntity> vaultsWriter, IMyNoSqlServerDataWriter<ObserverSettingsNoSqlEntity> settingWriter, IExchangeGateway exchangeGateway)
        {
            _indexPricesClient = indexPricesClient;
            _logger = logger;
            _assetWriter = assetWriter;
            _balanceExtractor = balanceExtractor;
            _bToFbWriter = bToFbWriter;
            _vaultsWriter = vaultsWriter;
            _settingWriter = settingWriter;
            _exchangeGateway = exchangeGateway;

            _timer = MyTaskTimer.Create<ExchangeCheckerJob>(TimeSpan.FromSeconds(Program.Settings.TimerPeriodInSec), logger, DoTime);
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
            
            var borrowedPositions = marginBalances.Balances.Where(t => Math.Abs(t.Borrowed) > 0)
                .Select(t => (t.Symbol, t.Borrowed)).ToList();

            foreach (var borrowedPosition in borrowedPositions)
            {
                var asset = await _assetWriter.GetAsync(
                    BinanceExchangeAssetNoSqlEntity.GeneratePartitionKey(),
                    BinanceExchangeAssetNoSqlEntity.GenerateRowKey(borrowedPosition.Symbol));
                
                if(asset.LockedUntil >= DateTime.UtcNow)
                    continue;

                var mainBalance = mainBalances.Balances.FirstOrDefault(t => t.Symbol == borrowedPosition.Symbol);

                var borrowedBalance = borrowedPosition.Borrowed;
                if(mainBalance != null)
                {
                    if (mainBalance.Balance >= borrowedBalance)
                    {
                        var transferResult =
                            await _exchangeGateway.TransferBinanceMainToMargin(borrowedPosition.Symbol, borrowedBalance);
                        if(transferResult.IsSuccess)
                        {
                            asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(5);
                            await _assetWriter.InsertOrReplaceAsync(asset);
                            borrowedBalance = 0;
                            continue;
                        }
                    }
                    else
                    {
                        var transferResult =
                            await _exchangeGateway.TransferBinanceMainToMargin(borrowedPosition.Symbol, mainBalance.Balance);
                        if (transferResult.IsSuccess)
                        {
                            borrowedBalance -= mainBalance.Balance;
                            asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(5);
                            await _assetWriter.InsertOrReplaceAsync(asset);
                        }
                    }
                }

                var fbAssets =
                    await _bToFbWriter.GetAsync(
                        BinanceToFireblocksAssetNoSqlEntity.GeneratePartitionKey(borrowedPosition.Symbol));
                var vaults = await _vaultsWriter.GetAsync();

                foreach (var fbAsset in fbAssets)
                {
                    if(borrowedBalance == 0)
                        break;
                    
                    if(fbAsset.MinTransferAmount < borrowedBalance)
                        continue;
                    
                    var vaultAccounts = vaults.Where(t =>
                        t.FireblocksAssetsWithBalances.ContainsKey(fbAsset.FireblocksAsset)).ToList();
                    
                    foreach (var vaultAccount in vaultAccounts)
                    {
                        var minBalance = vaultAccount.FireblocksAssetsWithBalances[fbAsset.FireblocksAsset];
                        if(minBalance < borrowedBalance)
                            continue;
                        
                        var fbBalance = fbBalances.Balances.FirstOrDefault(t => t.Asset == fbAsset.FireblocksAsset && t.VaultAccount == vaultAccount.VaultAccountId);
                        
                        if(fbBalance != null)
                        {
                            if (fbBalance.Amount + borrowedBalance >= minBalance)
                            {
                                var transferResult =
                                    await _exchangeGateway.TransferFireblocksToBinance(fbAsset.FireblocksAsset,
                                        fbAsset.FireblocksNetwork, vaultAccount.VaultAccountId,
                                        fbAsset.BinanceAssetSymbol, borrowedBalance);
                                if(transferResult.IsSuccess)
                                {
                                    asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                                    await _assetWriter.InsertOrReplaceAsync(asset);
                                    borrowedBalance = 0;
                                    break;
                                }
                            }
                            else
                            {
                                var transferAmount = fbBalance.Amount - minBalance;

                                var transferResult =
                                    await _exchangeGateway.TransferFireblocksToBinance(fbAsset.FireblocksAsset,
                                        fbAsset.FireblocksNetwork, vaultAccount.VaultAccountId,
                                        fbAsset.BinanceAssetSymbol, transferAmount);
                                if (transferResult.IsSuccess)
                                {
                                    borrowedBalance -= transferAmount;
                                    asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                                    await _assetWriter.InsertOrReplaceAsync(asset);
                                }
                            }
                        }
                    } 
                }

                if (borrowedBalance != 0)
                {
                    _logger.LogWarning("Unable to repay for borrowed asset {asset}. Unpaid amount is {borrowed}", borrowedPosition.Symbol, borrowedPosition.Borrowed);
                }
            }
        }

        private async Task CheckExchangeBalance()
        {
            var settings = await _settingWriter.GetAsync(ObserverSettingsNoSqlEntity.GeneratePartitionKey(), ObserverSettingsNoSqlEntity.GenerateRowKey());
            
            if(settings == null)
                return;

            var balances = await _balanceExtractor.GetBinanceMarginBalancesAsync();

            var totalBalance = balances.Balances.Sum(balance =>
                _indexPricesClient.GetIndexPriceByAssetVolumeAsync(balance.Symbol, balance.Balance).Item2);

            if(settings.MaximumExchangeBalanceUsd > totalBalance && totalBalance > settings.MinimalExchangeBalanceUsd)
                return;
            
            var assets = (await _assetWriter.GetAsync()).OrderByDescending(t=>t.Weight).ToList();
            if(assets.Any(t=>t.LockedUntil >= DateTime.UtcNow))
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
                        if(diff == 0)
                            return;
                        
                        var vaultAccounts = vaults.Where(t =>
                            t.FireblocksAssetsWithBalances.ContainsKey(fbAsset.FireblocksAsset)).ToList();
                        foreach (var vaultAccount in vaultAccounts)
                        {

                            var minBalance = vaultAccount.FireblocksAssetsWithBalances[fbAsset.FireblocksAsset];
                            var fbBalance = fbBalances.Balances.FirstOrDefault(t =>
                                t.Asset == fbAsset.FireblocksAsset && t.VaultAccount == vaultAccount.VaultAccountId);

                            var freeUsd = _indexPricesClient
                                .GetIndexPriceByAssetVolumeAsync(asset.AssetSymbol, fbBalance.Amount - minBalance)
                                .Item2;

                            if (freeUsd >= diff)
                            {
                                var amount = diff;
                                var result = await _exchangeGateway.TransferFireblocksToBinance(fbAsset.FireblocksAsset,
                                    fbAsset.FireblocksNetwork, vaultAccount.VaultAccountId, fbAsset.BinanceAssetSymbol,
                                    amount);
                                
                                if(result.IsSuccess)
                                {
                                    asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                                    await _assetWriter.InsertOrReplaceAsync(asset);
                                    diff = 0;
                                    break;
                                }
                            }
                            else
                            {
                                var amount = (diff - freeUsd);
                                var result = await _exchangeGateway.TransferFireblocksToBinance(fbAsset.FireblocksAsset,
                                    fbAsset.FireblocksNetwork, vaultAccount.VaultAccountId, fbAsset.BinanceAssetSymbol,
                                    amount);
                                if(result.IsSuccess)
                                {
                                    asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                                    await _assetWriter.InsertOrReplaceAsync(asset);
                                    diff -= amount;
                                }
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
                    if(diff == 0)
                        return;
                    
                    var balance = marginBalances.Balances.FirstOrDefault(t => t.Symbol == asset.AssetSymbol)?.Balance ?? 0m;
                    var balanceInUsd = _indexPricesClient
                        .GetIndexPriceByAssetVolumeAsync(asset.AssetSymbol, balance)
                        .Item2;
                    
                    if (balanceInUsd >= diff)
                    {
                        var amount = diff;
                        var result =
                            await _exchangeGateway.TransferFromBinanceMarginToFireblocks(asset.AssetSymbol, amount);
                        if(result.IsSuccess)
                        {
                            asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                            await _assetWriter.InsertOrReplaceAsync(asset);
                            diff = 0;
                            break;
                        }
                    }
                    else
                    {
                        var amount = balanceInUsd;
                        var result =
                            await _exchangeGateway.TransferFromBinanceMarginToFireblocks(asset.AssetSymbol, amount);
                        if (result.IsSuccess)
                        {
                            asset.LockedUntil = DateTime.UtcNow + TimeSpan.FromMinutes(30);
                            await _assetWriter.InsertOrReplaceAsync(asset);
                            diff -= amount;
                        }
                    }
                }
            }

        }
        
                    
        //проверить маржин аккаунт
        //проверить основной аккаунт
        //отправить с фаерблока
        //если нет других трансферов с фаерблока
            
        // fb asset network
        // exchange asset
        // min transfer 
            
        // weight
        // exchange asset 
            
        // fb vault account 
        // asset
        // min balance 
        
        //gateway 
        // transfer binance - fireblock
        // binance margin - main 
        // main - margin 
        // transfer fireblocks - binance

        public void Start()
        {
            _timer.Start();
        }
    }


}