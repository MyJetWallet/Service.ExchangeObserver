using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using MyJetWallet.Domain.ExternalMarketApi;
using MyJetWallet.Sdk.NoSql;
using Service.ExchangeObserver.Domain.Models.NoSql;
using Service.ExchangeObserver.Jobs;
using Service.ExchangeObserver.Services;
using Service.IndexPrices.Client;

namespace Service.ExchangeObserver.Modules
{
    public class ServiceModule: Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterMyNoSqlWriter<BinanceExchangeAssetNoSqlEntity>((() => Program.Settings.MyNoSqlWriterUrl),BinanceExchangeAssetNoSqlEntity.TableName);
            builder.RegisterMyNoSqlWriter<BinanceToFireblocksAssetNoSqlEntity>((() => Program.Settings.MyNoSqlWriterUrl),BinanceToFireblocksAssetNoSqlEntity.TableName);
            builder.RegisterMyNoSqlWriter<FbVaultAccountMapNoSqlEntity>((() => Program.Settings.MyNoSqlWriterUrl),FbVaultAccountMapNoSqlEntity.TableName);
            builder.RegisterMyNoSqlWriter<ObserverSettingsNoSqlEntity>((() => Program.Settings.MyNoSqlWriterUrl),ObserverSettingsNoSqlEntity.TableName);

            var client = builder.CreateNoSqlClient(Program.Settings.MyNoSqlReaderHostPort, Program.LogFactory);
            builder.RegisterIndexPricesClient(client);

            builder.RegisterExternalMarketClient(Program.Settings.ExternalApiGrpcUrl);
            
            builder.RegisterType<BalanceExtractor>().As<IBalanceExtractor>().SingleInstance().AutoActivate();
            builder.RegisterType<ExchangeGatewayMock>().As<IExchangeGateway>().SingleInstance().AutoActivate();
            builder.RegisterType<ExchangeCheckerJob>().AsSelf().SingleInstance().AutoActivate();
        }
    }
}