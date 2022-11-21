using System;
using System.Threading.Tasks;

namespace Service.ExchangeObserver.Services
{
    public class ExchangeGatewayMock : IExchangeGateway
    {
        public async Task<ExchangeResponse> TransferMarginToBorrowed(string assetSymbol, decimal amount)
        {
            Console.WriteLine($"Transfer Binance Margin to Borrow: {amount} {assetSymbol}");
            return new ExchangeResponse() {IsSuccess = true};
        }

        public async Task<ExchangeResponse> TransferBinanceMainToMargin(string assetSymbol, decimal amount)
        {
            Console.WriteLine($"Transfer Binance Main to Margin: {amount} {assetSymbol}");
            return new ExchangeResponse() {IsSuccess = true};

        }

        public async Task<ExchangeResponse> TransferFireblocksToBinance(string fireblocksAsset, string fireblocksNetwork, int vaultAccountId,
            string binanceAssetSymbol, decimal amount)
        {
            Console.WriteLine($"Transfer Fireblocks to Binance: {amount} {binanceAssetSymbol}, Vault: {vaultAccountId}. FB: {fireblocksAsset} {fireblocksNetwork}");
            return new ExchangeResponse() {IsSuccess = true};
        }

        public async Task<ExchangeResponse> TransferFromBinanceMarginToFireblocks(string assetSymbol, decimal amount)
        {
            Console.WriteLine($"Transfer Binance to Fireblocks:  {amount} {assetSymbol}");
            return new ExchangeResponse() {IsSuccess = true};
        }
    }

    public interface IExchangeGateway
    {
        Task<ExchangeResponse> TransferMarginToBorrowed(string assetSymbol, decimal amount);
        Task<ExchangeResponse> TransferBinanceMainToMargin(string assetSymbol, decimal amount);
        Task<ExchangeResponse> TransferFireblocksToBinance(string fireblocksAsset, string fireblocksNetwork, int vaultAccountId, string binanceAssetSymbol, decimal amount);
        Task<ExchangeResponse> TransferFromBinanceMarginToFireblocks(string assetSymbol, decimal amount);
    }

    public class ExchangeResponse
    {
        public bool IsSuccess { get; set; }
    }
}