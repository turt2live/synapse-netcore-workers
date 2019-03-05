using Prometheus;

namespace Matrix.SynapseInterop.Common
{
    public static class WorkerMetrics
    {
        private const string PREFIX = "synapse_netcore_worker";

        private static readonly Counter TransactionsSent =
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

        private static readonly Counter TransactionEventsSent =
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

        private static readonly Histogram TransactionDuration =
            Metrics.CreateHistogram($"{PREFIX}_txns_duration",
                                    "Time taken to complete a transaction",
                                    new HistogramConfiguration
                                    {
                                        LabelNames =
                                            new[]
                                            {
                                                "instance"
                                            }
                                    });

        private static readonly Histogram HostLookupDuration =
            Metrics.CreateHistogram($"{PREFIX}_hostlookup_duration",
                                    "Time taken to complete a host lookup",
                                    new HistogramConfiguration
                                    {
                                        LabelNames =
                                            new[]
                                            {
                                                "instance"
                                            }
                                    });

        private static readonly Histogram DbCallDuration =
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

        private static readonly Gauge OngoingTransactions =
            Metrics.CreateGauge($"{PREFIX}_txn_ongoing",
                                "How many transactions are currently ongoing",
                                new GaugeConfiguration
                                {
                                    LabelNames =
                                        new[]
                                        {
                                            "instance"
                                        }
                                });

        private static readonly Gauge CacheSize =
            Metrics.CreateGauge($"{PREFIX}_cache_size",
                                "The size of a given named cache",
                                new GaugeConfiguration
                                {
                                    LabelNames =
                                        new[]
                                        {
                                            "instance",
                                            "cache_name"
                                        }
                                });

        private static readonly Counter CacheMiss =
            Metrics.CreateCounter($"{PREFIX}_cache_miss",
                                  "Number of requested records that were missed by a named cache",
                                  new CounterConfiguration
                                  {
                                      LabelNames =
                                          new[]
                                          {
                                              "instance",
                                              "cache_name"
                                          }
                                  });
        
        private static readonly Counter CacheHit =
            Metrics.CreateCounter($"{PREFIX}_cache_hit",
                                  "Number of requested records that were missed by a named cache",
                                  new CounterConfiguration
                                  {
                                      LabelNames =
                                          new[]
                                          {
                                              "instance",
                                              "cache_name"
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

        public static ITimer TransactionDurationTimer()
        {
            return TransactionDuration.WithLabels(_name).NewTimer();
        }

        public static ITimer HostLookupDurationTimer()
        {
            return HostLookupDuration.WithLabels(_name).NewTimer();
        }

        public static ITimer DbCallTimer(string callName)
        {
            return DbCallDuration.WithLabels(_name, callName).NewTimer();
        }

        public static void ReportCacheSize(string cacheName, int size)
        {
            CacheSize.WithLabels(_name, cacheName).Set(size);
        }

        public static void ReportCacheMiss(string cacheName)
        {
            CacheMiss.WithLabels(_name, cacheName).Inc();
        }

        public static void ReportCacheHit(string cacheName)
        {
            CacheHit.WithLabels(_name, cacheName).Inc();
        }
    }
}
