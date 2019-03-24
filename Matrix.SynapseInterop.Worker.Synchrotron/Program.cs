using System;
using System.Net;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Worker.Synchrotron.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Routable;
using Routable.Kestrel;
using Serilog;
using ILogger = Serilog.ILogger;

namespace Matrix.SynapseInterop.Worker.Synchrotron
{
    internal class Program
    {
        private static IConfiguration _config;
        private static readonly ILogger _log = Log.ForContext<Program>();
        private static Synchrotron _sync;

        private static void Main(string[] args)
        {
            _config = new ConfigurationBuilder()
                     .AddJsonFile("appsettings.default.json", true, true)
                     .AddJsonFile("appsettings.json", true, true)
                     .AddEnvironmentVariables()
                     .AddCommandLine(args)
                     .Build();

            Logger.Setup(_config.GetSection("Logging"));

            var metricConfig = _config.GetSection("Metrics");
            SynapseDbContext.DefaultConnectionString = _config.GetConnectionString("synapse");

            if (metricConfig != null && metricConfig.GetValue<bool>("enabled"))
                WorkerMetrics.StartMetrics("synchrotron",
                                           metricConfig.GetValue("bindPort", 9150),
                                           metricConfig.GetValue<string>("bindHost"));

            _sync = new Synchrotron();
            _sync.Start(_config);
            RunKestrel();
            Console.ReadKey(true);
        }
        
        private static void RunKestrel()
        {
            _log.Information("Setting up Kestrel...");
            var kestrelConfig = _config.GetSection("Kestrel");

            var host = new WebHostBuilder()
                      .UseSerilog()
                      .UseKestrel(options => options.Listen(IPAddress.Parse(kestrelConfig.GetValue<string>("bindHost")),
                                                            kestrelConfig.GetValue<int>("bindPort")))
                      .ConfigureServices(s => { s.AddCors(); })
                      .Configure(app =>
                       {
                           app.UseCors(builder => builder
                                                 .AllowAnyOrigin()
                                                 .AllowAnyMethod()
                                                 .AllowAnyHeader());
                       })
                      .Configure(builder => builder.UseRoutable(options =>
                       {
                           options
                              .WithJsonSupport()
                              .UseLogger(new RoutableSerilogLogger(_log))
                              .AddRouting(new SyncController(options, _sync))
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
