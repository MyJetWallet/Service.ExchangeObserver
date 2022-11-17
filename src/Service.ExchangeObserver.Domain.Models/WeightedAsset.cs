namespace Service.ExchangeObserver.Domain.Models
{
    public class WeightedAsset
    {
        public string AssetSymbol { get; set; }
        public int Weight { get; set; }
    }
    
    public class WeightedAssetWithBalance
    {
        public string AssetSymbol { get; set; }
        public int Weight { get; set; }
        public decimal Balance { get; set; }
        public decimal BalanceUsd { get; set; }

    }
}