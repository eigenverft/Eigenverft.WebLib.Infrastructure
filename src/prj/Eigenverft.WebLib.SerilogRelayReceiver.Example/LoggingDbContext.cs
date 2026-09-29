using Eigenverft.WebLib.SerilogRelayReceiver;

using Microsoft.EntityFrameworkCore;

namespace Eigenverft.WebLib.SerilogRelayReceiver.Example
{
    internal sealed class LoggingDbContext : DbContext
    {
        public LoggingDbContext(DbContextOptions<LoggingDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigureSerilogRelayReceiver();
        }
    }
}
