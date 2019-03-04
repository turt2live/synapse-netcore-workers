using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common.Transactions;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.WorkerModels;
using Serilog;
using Serilog.Core.Enrichers;

namespace Matrix.SynapseInterop.Worker.AppserviceSender.Transactions
{
    public class AppserviceTransactionManager : TransactionManager<QueuedEvent>
    {
        private readonly IEnumerable<Regex> _aliasNamespaces;
        private readonly Appservice _appservice;
        private readonly string _asSender;
        private readonly IEnumerable<Regex> _roomNamespaces;
        private readonly IEnumerable<Regex> _userNamespaces;
        private readonly AppserviceHttpSender _httpSender;
        private bool _sendingTransactions;
        private Task _sendLoop;

        public AppserviceTransactionManager(Appservice appservice, string serverName) :
            base(storeSentTransactionsInMemory: false)
        {
            _logger = Log.ForContext<AppserviceTransactionManager>()
                         .ForContext(new PropertyEnricher("AppserviceId", appservice.Id));

            _appservice = appservice;
            if (_appservice.Enabled) StartLoop();

            var namespaces = _appservice.Namespaces ?? new AppserviceNamespace[0];

            _userNamespaces = namespaces.Where(ns => ns.Kind == AppserviceNamespace.NS_USERS).ToArray()
                                        .Select(ns => new Regex(FixRegex(ns.Regex)));

            _roomNamespaces = namespaces.Where(ns => ns.Kind == AppserviceNamespace.NS_ROOMS).ToArray()
                                        .Select(ns => new Regex(FixRegex(ns.Regex)));

            _aliasNamespaces = namespaces.Where(ns => ns.Kind == AppserviceNamespace.NS_ALIASES).ToArray()
                                         .Select(ns => new Regex(FixRegex(ns.Regex)));

            _asSender = $@"{appservice.SenderLocalpart}:${serverName}";
            _httpSender = new AppserviceHttpSender(appservice);
        }

        private string FixRegex(string input)
        {
            if (!input.StartsWith("^")) return $"^{input}";
            return input;
        }

        private void StartLoop()
        {
            _sendingTransactions = true;

            _sendLoop = Task.Run(async () =>
            {
                while (_sendingTransactions)
                {
                    lock (this)
                    {
                        Monitor.Wait(this);
                    }

                    var txnToSend = GetTransactionToSend();

                    if (txnToSend != null)
                    {
                        _logger.Information("Got txn to send with {0} events", txnToSend.Elements.Count);

                        var success = await _httpSender.SendTransaction(txnToSend);

                        if (success)
                        {
                            _logger.Information("Flagging txn as sent");
                            FlagSent(txnToSend);
                        }
                        else
                        {
                            _logger.Warning("Transaction failed to send");
                        }
                    }
                }
            });
        }

        public void StopLoop()
        {
            _sendingTransactions = false;

            lock (this)
            {
                Monitor.Pulse(this);
            }

            _sendLoop?.GetAwaiter().GetResult();
        }

        public void QueueIfInterested(QueuedEvent ev)
        {
            // TODO: Handle appservice residing in room (and therefore interested)
            // TODO: Smarter checks with caching
            //         - Once a room is known to be interesting, wait for it to become uninteresting
            //         - Eg: wait for all appservice users to leave

            // More often than not we'll be matching against the user namespace, so burn
            // the CPU on that first
            if (ev.StateKey != null)
                if (ev.StateKey == _asSender || _userNamespaces.Any(r => r.IsMatch(ev.StateKey)))
                {
                    QueueElement(ev);
                    return;
                }

            if (ev.Sender == _asSender || _userNamespaces.Any(r => r.IsMatch(ev.Sender)))
            {
                QueueElement(ev);
                return;
            }

            if (_roomNamespaces.Any(r => r.IsMatch(ev.Event.RoomId)))
            {
                QueueElement(ev);
                return;
            }

            // Aliases are the hardest to calculate: We'll want to see if the room has an alias
            // that is interesting to the appservice.
            using (var db = new SynapseDbContext())
            {
                var aliases = db.RoomAliases.Where(a => a.RoomId == ev.Event.RoomId).ToArray()
                                .Select(a => a.Alias);

                if (_aliasNamespaces.Any(r => aliases.Any(r.IsMatch)))
                {
                    QueueElement(ev);
                    return;
                }
            }

            _logger.Information("Received uninteresting event");
        }

        protected override void PersistTransaction(Transaction<QueuedEvent> transaction)
        {
            // TODO: Real implementation
        }

        protected override void PersistTransactionQueue(Transaction<QueuedEvent>[] queue)
        {
            // TODO: Real implementation
        }

        protected override Transaction<QueuedEvent>[] LoadTransactionQueue()
        {
            return new Transaction<QueuedEvent>[0]; // TODO: Real implementation
        }

        protected override ICollection<Transaction<QueuedEvent>> LoadTransactionsWithStatus(TransactionStatus status)
        {
            return new Transaction<QueuedEvent>[0]; // TODO: Real implementation
        }

        protected override Transaction<QueuedEvent> LoadTransaction(string transactionId)
        {
            return null; // TODO: Real implementation
        }
    }
}
