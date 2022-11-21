using Microsoft.EntityFrameworkCore;
using MyJetWallet.Sdk.Postgres;
using Service.ExchangeObserver.Domain.Models;

namespace Service.ExchangeObserver.Postgres
{
    public class DatabaseContext : MyDbContext
    {
        public DbSet<ObserverTransfer> Transfers { get; set; }

        public const string TransfersTableName = "transfers";

        public const string Schema = "exchangeobserver";

        public DatabaseContext(DbContextOptions options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(Schema);
            
            modelBuilder.Entity<ObserverTransfer>().ToTable(TransfersTableName);
            modelBuilder.Entity<ObserverTransfer>().HasKey(e => e.Id);
            
            base.OnModelCreating(modelBuilder);
        }
        
        public async Task<int> UpsertAsync(IEnumerable<ObserverTransfer> entities)
        {
            var result = await Transfers.UpsertRange(entities).AllowIdentityMatch().RunAsync();
            return result;
        }
    }
}