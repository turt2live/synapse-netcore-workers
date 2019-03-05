using System;
using System.Net;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Worker.AppserviceSender.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Routable;
using Routable.Kestrel;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Matrix.SynapseInterop.Worker.AppserviceSender
{
    internal class Program
    {
        private static ILogger _log;
        private static IConfiguration _config;
        private static AppserviceManager _manager;

        private static void Main(string[] args)
        {
            Console.WriteLine("Starting appservice sender...");

            _config = new ConfigurationBuilder()
                     .AddJsonFile("appsettings.default.json", true, true)
                     .AddJsonFile("appsettings.json", true, true)
                     .AddEnvironmentVariables()
                     .AddCommandLine(args)
                     .Build();

            Logger.Setup(_config.GetSection("Logging"));
            _log = Log.ForContext<Program>();

            SetupDatabase();
            SetupManager();
            RunKestrel(); // Blocking

            // Shutdown operations
            _manager?.Stop();
        }

        private static async Task<SynapseReplication> StartReplicationAsync()
        {
            var replication = new SynapseReplication();
            replication.ClientName = "NetCoreAppserviceSender";
            replication.ServerName += Replication_ServerName;

            var synapseConfig = _config.GetSection("Synapse");

            await replication.Connect(synapseConfig.GetValue<string>("replicationHost"),
                                      synapseConfig.GetValue<int>("replicationPort"));

            return replication;
        }

        private static void Replication_ServerName(object sender, string e)
        {
            _log.Information("Connected to server: {0}", e);
        }

        private static void SetupDatabase()
        {
            _log.Information("Connecting to database and running migrations...");
            AppserviceDb.ConnectionString = _config.GetConnectionString("appserviceWorker");
            SynapseDbContext.DefaultConnectionString = _config.GetConnectionString("synapse");

            using (var context = new AppserviceDb())
            {
                context.Database.Migrate();
            }
        }

        private static async void SetupManager()
        {
            _log.Information("Connecting to replication...");
            var replication = await StartReplicationAsync();

            _log.Information("Setting up appservice manager...");
            _manager = new AppserviceManager(replication);
        }

        private static void RunKestrel()
        {
            _log.Information("Setting up Kestrel...");
            var kestrelConfig = _config.GetSection("Kestrel");

            var host = new WebHostBuilder()
                      .UseSerilog()
                      .UseKestrel(options => options.Listen(IPAddress.Parse(kestrelConfig.GetValue<string>("bindHost")),
                                                            kestrelConfig.GetValue<int>("bindPort")))
                      .Configure(builder => builder.UseRoutable(options =>
                       {
                           options
                              .WithJsonSupport()
                              .UseLogger(new RoutableSerilogLogger(_log))
                              .AddRouting(new AppserviceAdminRouting(options))
                              .OnError(new KestrelRouting(options)
                               {
                                   _ => _.Do((context, request, response) =>
                                   {
                                       response.Status = 500;
                                       response.ContentType = "application/json";

                                       response.Write(JObject.FromObject(new
                                       {
                                           error = "Internal server error",
                                           errcode = "M_UNKNOWN"
                                       }));
                                   })
                               });
                       }))
                      .Build();

            _log.Information("Running Kestrel...");
            host.Run();
        }
    }
}
