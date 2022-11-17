using Autofac;
using Service.ExchangeObserver.Grpc;

// ReSharper disable UnusedMember.Global

namespace Service.ExchangeObserver.Client
{
    public static class AutofacHelper
    {
        public static void RegisterExchangeObserverClient(this ContainerBuilder builder, string grpcServiceUrl)
        {
            var factory = new ExchangeObserverClientFactory(grpcServiceUrl);

            builder.RegisterInstance(factory.GetHelloService()).As<IHelloService>().SingleInstance();
        }
    }
}
