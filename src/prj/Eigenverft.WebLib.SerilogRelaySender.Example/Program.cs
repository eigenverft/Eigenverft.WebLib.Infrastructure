using System;
using System.IO;

using Eigenverft.NetLib.SerilogRelay;

using Serilog;

namespace Eigenverft.WebLib.SerilogRelaySender.Example
{
    internal static class Program
    {
        private const string Endpoint = "http://127.0.0.1:5217/api/v1/logs";

        public static int Main(string[] args)
        {
            string message = args.Length == 0 ? "Hello from the SerilogRelay sender example" : string.Join(" ", args);
            string spoolDirectory = Path.Combine(AppContext.BaseDirectory, "demo-data", "spool");

            // Dispose attempts delivery; unsent events remain in the spool.
            using (var logger = new LoggerConfiguration()
                .WriteTo.SerilogRelay(
                    endpoint: Endpoint,
                    spoolDirectory: spoolDirectory,
                    applicationId: "Eigenverft.WebLib.SerilogRelaySender.Example")
                .CreateLogger())
            {
                logger.Information("Relay example: {Message}", message);
            }

            Console.WriteLine($"Logged: {message}");
            Console.WriteLine("Inspect received events at http://127.0.0.1:5217/demo/events");
            Console.WriteLine($"Local spool: {spoolDirectory}");
            return 0;
        }
    }
}
