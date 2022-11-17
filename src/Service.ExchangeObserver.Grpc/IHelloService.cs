using System.ServiceModel;
using System.Threading.Tasks;
using Service.ExchangeObserver.Grpc.Models;

namespace Service.ExchangeObserver.Grpc
{
    [ServiceContract]
    public interface IHelloService
    {
        [OperationContract]
        Task<HelloMessage> SayHelloAsync(HelloRequest request);
    }
}