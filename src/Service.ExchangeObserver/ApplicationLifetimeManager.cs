using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyJetWallet.Sdk.NoSql;
using MyJetWallet.Sdk.Service;
using Service.ExchangeObserver.Jobs;

namespace Service.ExchangeObserver
{
    public class ApplicationLifetimeManager : ApplicationLifetimeManagerBase
    {
        private readonly ILogger<ApplicationLifetimeManager> _logger;
        private readonly MyNoSqlClientLifeTime _myNoSqlClientLifeTime;
        private readonly BorrowCheckerJob _borrowCheckerJob;
        private readonly EquityCheckerJob _equityCheckerJob;

        public ApplicationLifetimeManager(IHostApplicationLifetime appLifetime, ILogger<ApplicationLifetimeManager> logger, MyNoSqlClientLifeTime myNoSqlClientLifeTime, BorrowCheckerJob borrowCheckerJob, EquityCheckerJob equityCheckerJob)
            : base(appLifetime)
        {
            _logger = logger;
            _myNoSqlClientLifeTime = myNoSqlClientLifeTime;
            _borrowCheckerJob = borrowCheckerJob;
            _equityCheckerJob = equityCheckerJob;
        }

        protected override void OnStarted()
        {
            _logger.LogInformation("OnStarted has been called.");
            _myNoSqlClientLifeTime.Start();
            _borrowCheckerJob.Start();
            _equityCheckerJob.Start();
        }

        protected override void OnStopping()
        {
            _logger.LogInformation("OnStopping has been called.");
            _myNoSqlClientLifeTime.Stop();
        }

        protected override void OnStopped()
        {
            _logger.LogInformation("OnStopped has been called.");
        }
    }
}
