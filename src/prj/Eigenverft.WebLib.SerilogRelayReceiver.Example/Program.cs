using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Eigenverft.WebLib.SerilogRelayReceiver;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.SerilogRelayReceiver.Example
{
    internal static class Program
    {
        private const string Address = "http://127.0.0.1:5217";

        public static async Task<int> Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(Address);

            string dataDirectory = Path.Combine(AppContext.BaseDirectory, "demo-data");
            Directory.CreateDirectory(dataDirectory);
            string databasePath = Path.Combine(dataDirectory, "received.db");

            builder.Services.AddDbContextFactory<LoggingDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddSerilogRelayReceiverEntityFrameworkCore<LoggingDbContext>();

            await using WebApplication app = builder.Build();

            // Demo database; applications should use migrations.
            IDbContextFactory<LoggingDbContext> databaseFactory = app.Services.GetRequiredService<IDbContextFactory<LoggingDbContext>>();
            await using (LoggingDbContext database = await databaseFactory.CreateDbContextAsync())
            {
                await database.Database.EnsureCreatedAsync();
            }

            // The pinned receiver package predates the compatible 256-event default.
            // Remove this override when updating to the receiver release with that default.
            app.MapSerilogRelayReceiverEntityFrameworkCore<LoggingDbContext>(
                "/api/v1/logs",
                options => options.MaximumBatchEvents = 256);

            // Local-only inspection endpoint for the demo.
            app.MapGet("/demo/events", async (IDbContextFactory<LoggingDbContext> factory) =>
            {
                await using LoggingDbContext database = await factory.CreateDbContextAsync();
                return await database.Set<SerilogRelayReceivedEvent>()
                    .AsNoTracking()
                    .OrderByDescending(row => row.ReceiveId)
                    .Take(20)
                    .Select(row => new { row.ApplicationId, row.Level, row.RenderMessage })
                    .ToListAsync();
            });

            Console.WriteLine($"Receiver: {Address}/api/v1/logs");
            Console.WriteLine($"Inspect:  {Address}/demo/events");
            Console.WriteLine($"SQLite:   {databasePath}");

            await app.RunAsync();
            return 0;
        }
    }
}
