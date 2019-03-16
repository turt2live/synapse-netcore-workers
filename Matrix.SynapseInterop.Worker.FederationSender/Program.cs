using System;
using System.Net;
using System.Threading;
using Matrix.SynapseInterop.Common;
using Matrix.SynapseInterop.Database;
using Microsoft.Extensions.Configuration;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    internal class Program
    {
        private static IConfiguration _config;

        private static readonly AutoResetEvent _closing = new AutoResetEvent(false);

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

            Console.CancelKeyPress += new ConsoleCancelEventHandler(OnExit);
            SynapseDbContext.DefaultConnectionString = _config.GetConnectionString("synapse");

            new FederationSender(_config).Start().Wait();

            _closing.WaitOne();
        }

        protected static void OnExit(object sender, ConsoleCancelEventArgs args)
        {
            Console.WriteLine("Exit");
            _closing.Set();
        }
    }
}
