using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using Matrix.SynapseInterop.Common;
using Microsoft.EntityFrameworkCore;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    class Program
    {
        private static IConfiguration _config;

        static void Main(string[] args)
        {
            _config = new ConfigurationBuilder()
                     .AddJsonFile("appsettings.default.json", true, true)
                     .AddEnvironmentVariables()
                     .AddCommandLine(args)
                     .Build();

            var metricConfig = _config.GetSection("Metrics");

            if (metricConfig != null && metricConfig.GetValue<bool>("enabled"))
            {
                WorkerMetrics.StartMetrics("federation_worker",
                                           metricConfig.GetValue("bindPort", 9150),
                                           metricConfig.GetValue<string>("bindHost"));
            }

            new FederationSender(_config).Start().Wait();

            Console.ReadKey(true);
        }
    }
}
