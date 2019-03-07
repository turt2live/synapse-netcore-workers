using System;
using System.Net;
using Matrix.SynapseInterop.Common;
using Microsoft.Extensions.Configuration;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    internal class Program
    {
        private static IConfiguration _config;

        private static void Main(string[] args)
        {
            // Because we will be doing a LOT of http requests.
            ServicePointManager.ReusePort = true;

            _config = new ConfigurationBuilder()
                     .AddJsonFile("appsettings.default.json", true, true)
                     .AddJsonFile("appsettings.json", true, true)
                     .AddEnvironmentVariables()
                     .AddCommandLine(args)
                     .Build();

            Logger.Setup(_config.GetSection("Logging"));

            var metricConfig = _config.GetSection("Metrics");

            if (metricConfig != null && metricConfig.GetValue<bool>("enabled"))
                WorkerMetrics.StartMetrics("federation_worker",
                                           metricConfig.GetValue("bindPort", 9150),
                                           metricConfig.GetValue<string>("bindHost"));
            
            ServicePointManager.DefaultConnectionLimit = _config.GetSection("Http").GetValue("connectionLimit", 50);

            new FederationSender(_config).Start().Wait();

            Console.ReadKey(true);
        }
    }
}
