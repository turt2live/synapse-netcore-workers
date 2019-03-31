using System;
using System.Net;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Common.MatrixUtils;
using Matrix.SynapseInterop.Common.WebResponses;
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
        private static CachedMatrixRoomSet _roomSet;
        private static Synchrotron _sync;
        private static MessagesHandler _messages;

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

            _roomSet = new CachedMatrixRoomSet();
            _messages = new MessagesHandler(_roomSet);
            _sync = new Synchrotron(_messages, _roomSet);
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
                      .ConfigureServices(s =>
                       {
                           s.AddCors(options =>
                           {
                               options.AddPolicy("CorsPolicyAllowAll",
                                                 builder => builder.AllowAnyOrigin()
                                                                   .AllowAnyMethod()
                                                                   .AllowAnyHeader());
                           });
                       })
                      .Configure(builder => builder.UseCors("CorsPolicyAllowAll").UseRoutable(options =>
                       {
                           options
                              .WithJsonSupport()
                              .UseLogger(new RoutableSerilogLogger(_log))
                              .AddRouting(new SyncController(options, _sync))
                              .AddRouting(new RoomController(options, _messages))
                              .OnError(new KestrelRouting(options)
                               {
                                   _ => _.Do((context, request, response) =>
                                   {
                                       var err = context.Error is ErrorException e ? e.Response : ErrorResponse.DefaultResponse;
                                       response.Status = err.HttpStatus;
                                       response.ContentType = "application/json";
                                       response.Write(JObject.FromObject(err));
                                   })
                               });
                       }))
                      .Build();

            _log.Information("Running Kestrel...");
            host.Run();
        }
    }
}