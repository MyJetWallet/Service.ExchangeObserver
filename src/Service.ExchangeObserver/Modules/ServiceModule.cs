using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using MyJetWallet.Domain.ExternalMarketApi;
using MyJetWallet.Sdk.NoSql;
using Service.ExchangeObserver.Domain.Models.NoSql;
using Service.ExchangeObserver.Jobs;
using Service.IndexPrices.Client;

namespace Service.ExchangeObserver.Modules
{
    public class ServiceModule: Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterMyNoSqlWriter<ExternalExchangeAssetNoSqlEntity>((() => Program.Settings.MyNoSqlWriterUrl),ExternalExchangeAssetNoSqlEntity.TableName);

            var client = builder.CreateNoSqlClient(Program.Settings.MyNoSqlReaderHostPort, Program.LogFactory);
            builder.RegisterIndexPricesClient(client);

            builder.RegisterExternalMarketClient(Program.Settings.ExternalApiGrpcUrl);
            
            builder.RegisterType<ExchangeCheckerJob>().AsSelf().SingleInstance().AutoActivate();
        }
    }
}