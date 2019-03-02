using Microsoft.Extensions.Configuration;
using System;
using System.Threading;
using Matrix.SynapseInterop.Common;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    class Program
    {
        private static IConfiguration _config;

        static void Main(string[] args)
        {
            _config = new ConfigurationBuilder()
                     .AddJsonFile("appsettings.json", true, true)
                     .AddEnvironmentVariables()
                     .AddCommandLine(args)
                     .Build();

            var logConfig = _config.GetSection("Logging");

            if (!Enum.TryParse(logConfig.GetValue<string>("level"), out LogEventLevel level))
            {
                level = LogEventLevel.Information;
            }

            Log.Logger = new LoggerConfiguration().Filter
                                                  .ByIncludingOnly(e => e.Level >= level)
                                                  .WriteTo.Console(outputTemplate: 
                                                                   "{Timestamp:yy-MM-dd HH:mm:ss.fff} {Level:u3} {SourceContext:lj} {Message:lj}{NewLine}{Exception}")
                                                  .CreateLogger();
            
            var metricConfig = _config.GetSection("Metrics");

            if (metricConfig != null && metricConfig.GetValue<bool>("enabled"))
            {
                WorkerMetrics.StartMetrics("federation_worker",
                                           metricConfig.GetValue("bindPort", 9150),
                                           metricConfig.GetValue<string>("bindHost"));
            }

            new FederationSender(_config).Start().Wait();
            Thread.Sleep(-1);
        }
    }
}
