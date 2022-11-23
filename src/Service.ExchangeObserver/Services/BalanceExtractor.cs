using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyJetWallet.Domain.ExternalMarketApi;
using MyJetWallet.Domain.ExternalMarketApi.Dto;
using MyJetWallet.Domain.ExternalMarketApi.Models;
using MyNoSqlServer.Abstractions;
using Service.Blockchain.Wallets.MyNoSql.Addresses;

namespace Service.ExchangeObserver.Services
{
    public class BalanceExtractor : IBalanceExtractor
    {
        private readonly IExternalMarket _externalMarket;
        private readonly IMyNoSqlServerDataReader<VaultAssetNoSql> _fbReader;

        public BalanceExtractor(IExternalMarket externalMarket, IMyNoSqlServerDataReader<VaultAssetNoSql> fbReader)
        {
            _externalMarket = externalMarket;
            _fbReader = fbReader;
        }

        public async Task<GetBalancesResponse> GetBinanceMainBalancesAsync()
        {
            return await _externalMarket.GetSecondaryBalancesAsync(new GetBalancesRequest
            {
                ExchangeName = "Binance"
            });
        }

        public async Task<GetBalancesResponse> GetBinanceMarginBalancesAsync()
        {
            return await _externalMarket.GetBalancesAsync(new GetBalancesRequest
            {
                ExchangeName = "Binance"
            });
        }

        public async Task<FbBalancesResponse> GetFireblocksBalancesAsync()
        {
            var assets=  _fbReader.Get();

            var balances = assets.Select(t => new FbBalance
            {
                Amount = t.VaultAsset.Available,
                Asset = t.AssetSymbol,
                Network = t.AssetNetwork,
                VaultAccount = int.Parse(t.VaultAccountId)
            }).ToList();
            
            return new FbBalancesResponse
            {
                Balances = balances
            };
        }
    }
    
    public interface IBalanceExtractor
    {
        Task<GetBalancesResponse> GetBinanceMainBalancesAsync();
        Task<GetBalancesResponse> GetBinanceMarginBalancesAsync();
        Task<FbBalancesResponse> GetFireblocksBalancesAsync();
    }

    public class FbBalancesResponse
    {
        public List<FbBalance> Balances;
    }

    public class FbBalance
    {
        public decimal Amount { get; set; }
        public string Asset { get; set; }
        public string Network { get; set; }
        public int VaultAccount { get; set; }
    }
}