using JetBrains.Annotations;
using MyJetWallet.Sdk.Grpc;
using Service.ExchangeObserver.Grpc;

namespace Service.ExchangeObserver.Client
{
    [UsedImplicitly]
    public class ExchangeObserverClientFactory: MyGrpcClientFactory
    {
        public ExchangeObserverClientFactory(string grpcServiceUrl) : base(grpcServiceUrl)
        {
        }

        public IHelloService GetHelloService() => CreateGrpcService<IHelloService>();
    }
}
