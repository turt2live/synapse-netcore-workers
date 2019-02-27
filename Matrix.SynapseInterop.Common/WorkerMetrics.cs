using System;
using System.Collections.Generic;
using Prometheus;

namespace Matrix.SynapseInterop.Common
{
    public static class WorkerMetrics
    {
        private const string PREFIX = "synapse_netcore_worker";

        static readonly Counter TransactionsSent = Metrics.CreateCounter(
            $"{PREFIX}_txns_sent",
            "Number of transactions sent",
            new CounterConfiguration
            {
                LabelNames = new[] { "instance", "outcome", "destination" }
            }
        );

        static readonly Histogram TransactionDuration = Metrics.CreateHistogram(
            $"{PREFIX}_txns_duration",
            "Time taken to complete a transaction",
            new HistogramConfiguration
            {
                LabelNames = new[] { "instance", "destination" }
                
            }
        );
        private static MetricServer _srv;
        private static string _name;
        public static void StartMetrics (string bindHost, int bindPort, string instanceName)
        {
            _name = instanceName; 
            _srv = new MetricServer(bindHost, bindPort);
            _srv.Start();
        }

        public static void IncTransactionsSent(bool successful, string destination)
        {
            TransactionsSent.WithLabels(_name, successful ? "success" : "fail", destination).Inc();
        }

        public static ITimer TransactionDurationTimer(string destination)
        {
            return TransactionDuration.WithLabels(_name, destination).NewTimer();
        }
    }
}