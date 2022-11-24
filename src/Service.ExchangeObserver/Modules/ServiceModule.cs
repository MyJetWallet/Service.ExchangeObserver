using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using MyJetWallet.Domain.ExternalMarketApi;
using MyJetWallet.Sdk.NoSql;
using Service.Blockchain.Wallets.MyNoSql.Addresses;
using Service.ExchangeGateway.Client;
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
            builder.RegisterMyNoSqlWriter<FbVaultAccountMapNoSqlEntity>((() => Program.Settings.MyNoSqlWriterUrl),FbVaultAccountMapNoSqlEntity.TableName);
            builder.RegisterMyNoSqlWriter<ObserverSettingsNoSqlEntity>((() => Program.Settings.MyNoSqlWriterUrl),ObserverSettingsNoSqlEntity.TableName);
            builder.RegisterMyNoSqlWriter<TransfersMonitorNoSqlEntity>((() => Program.Settings.MyNoSqlWriterUrl),TransfersMonitorNoSqlEntity.TableName);

            var client = builder.CreateNoSqlClient(Program.Settings.MyNoSqlReaderHostPort, Program.LogFactory);
            builder.RegisterIndexPricesClient(client);
            builder.RegisterMyNoSqlReader<VaultAssetNoSql>(client, VaultAssetNoSql.TableName);

            builder.RegisterExternalMarketClient(Program.Settings.ExternalApiGrpcUrl);
            
            builder.RegisterExchangeGatewayClient(Program.Settings.ExternalGatewayGrpcUrl);
            builder.RegisterType<ObserverJobHelper>().AsSelf().SingleInstance().AutoActivate();
            builder.RegisterType<BalanceExtractor>().As<IBalanceExtractor>().SingleInstance().AutoActivate();
            //builder.RegisterType<ExchangeGatewayMock>().As<IExchangeGateway>().SingleInstance().AutoActivate();
            builder.RegisterType<BorrowCheckerJob>().AsSelf().SingleInstance().AutoActivate();
            builder.RegisterType<EquityCheckerJob>().AsSelf().SingleInstance().AutoActivate();

        }
    }
}