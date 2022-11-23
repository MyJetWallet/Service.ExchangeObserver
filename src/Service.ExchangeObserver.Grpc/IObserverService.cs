using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;
using Service.ExchangeObserver.Domain.Models;
using Service.ExchangeObserver.Grpc.Models;

namespace Service.ExchangeObserver.Grpc
{
    [ServiceContract]
    public interface IObserverService
    {
        [OperationContract]
        Task<AllAssetsResponse> GetAllAssets();
        [OperationContract]
        Task<OperationResponse> AddOrUpdateAsset(UpdateAssetRequest request);
        [OperationContract]
        Task<OperationResponse> RemoveAsset(RemoveAssetRequest request);
        
        [OperationContract]
        IAsyncEnumerable<ObserverTransfer> GetTransfers(GetTransfersRequest request);
    }
}