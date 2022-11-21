using System.Collections.Generic;
using System.Threading.Tasks;
using MyJetWallet.Domain.ExternalMarketApi;
using MyJetWallet.Domain.ExternalMarketApi.Dto;
using MyJetWallet.Domain.ExternalMarketApi.Models;

namespace Service.ExchangeObserver.Services
{
    public class BalanceExtractor : IBalanceExtractor
    {
        private readonly IExternalMarket _externalMarket;

        public BalanceExtractor(IExternalMarket externalMarket)
        {
            _externalMarket = externalMarket;
        }

        public async Task<GetBalancesResponse> GetBinanceMainBalancesAsync()
        {
            return await _externalMarket.GetBalancesAsync(new GetBalancesRequest
            {
                ExchangeName = "Binance"
            });
        }

        public async Task<GetBalancesResponse> GetBinanceMarginBalancesAsync()
        {
            return await _externalMarket.GetSecondaryBalancesAsync(new GetBalancesRequest
            {
                ExchangeName = "Binance"
            });
        }

        public async Task<FbBalancesResponse> GetFireblocksBalancesAsync()
        {
            return new FbBalancesResponse
            {
                Balances = new List<FbBalance>()
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
        public int VaultAccount { get; set; }
    }
}