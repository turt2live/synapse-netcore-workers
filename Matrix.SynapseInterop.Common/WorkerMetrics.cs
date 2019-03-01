using System;
using System.Collections.Generic;
using Prometheus;

namespace Matrix.SynapseInterop.Common
{
    public static class WorkerMetrics
    {
        private const string PREFIX = "synapse_netcore_worker";

        static readonly Counter TransactionsSent =
            Metrics.CreateCounter($"{PREFIX}_txns_sent",
                                  "Number of transactions sent",
                                  new CounterConfiguration
                                  {
                                      LabelNames = new[]
                                      {
                                          "instance",
                                          "outcome",
                                          "destination"
                                      }
                                  });

        static readonly Counter TransactionEventsSent =
            Metrics.CreateCounter($"{PREFIX}_txn_events_sent",
                                  "Number of transactions sent",
                                  new CounterConfiguration
                                  {
                                      LabelNames = new[]
                                      {
                                          "instance",
                                          "type",
                                          "destination"
                                      }
                                  });
 
        static readonly Histogram TransactionDuration = 
            Metrics.CreateHistogram($"{PREFIX}_txns_duration",
                                    "Time taken to complete a transaction",
                                    new HistogramConfiguration
                                    {
                                        LabelNames =
                                            new[]
                                            {
                                                "instance",
                                                "destination"
                                            }
                                    });

        static readonly Histogram DbCallDuration = 
            Metrics.CreateHistogram($"{PREFIX}_db_call_duration",
                                    "Time taken to complete a DB call",
                                    new HistogramConfiguration
                                    {
                                        LabelNames =
                                            new[]
                                            {
                                                "instance",
                                                "name"
                                            }
                                    });

        static readonly Gauge OngoingTransactions = 
            Metrics.CreateGauge($"{PREFIX}_txn_ongoing",
                                "How many transactions are currently ongoing",
                                new GaugeConfiguration
                                {
                                    LabelNames =
                                        new[]
                                        {
                                            "instance",
                                        }
                                });

        private static MetricServer _srv;
        private static string _name;

        public static void StartMetrics(string instanceName, int bindPort, string bindHost = null)
        {
            _name = instanceName;

            _srv = bindHost != null ? new MetricServer(bindHost, bindPort) : new MetricServer(bindPort);

            _srv.Start();
        }

        public static void IncOngoingTransactions()
        {
            OngoingTransactions.Inc();
        }
        
        public static void DecOngoingTransactions()
        {
            OngoingTransactions.Dec();
        }
        
        public static void IncTransactionsSent(bool successful, string destination)
        {
            TransactionsSent.WithLabels(_name, successful ? "success" : "fail", destination).Inc();
        }
        
        public static void IncTransactionEventsSent(string type, string destination, int count = 1)
        {
            TransactionEventsSent.WithLabels(_name, type, destination).Inc(count);
        }

        public static ITimer TransactionDurationTimer(string destination)
        {
            return TransactionDuration.WithLabels(_name, destination).NewTimer();
        }
        
        public static ITimer DbCallTimer(string callName)
        {
            return DbCallDuration.WithLabels(_name, callName).NewTimer();
        }
    }
}
