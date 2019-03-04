using System.Collections.Generic;
using System.Threading.Tasks;
using Matrix.SynapseInterop.Common.Transactions;
using Matrix.SynapseInterop.Database.WorkerModels;
using Serilog;
using Serilog.Core.Enrichers;

namespace Matrix.SynapseInterop.Worker.AppserviceSender.Transactions
{
    public class AppserviceTransactionManager : TransactionManager<QueuedEvent>
    {
        private readonly Appservice _appservice;
        private bool _sendingTransactions;
        private Task _sendLoop;

        public AppserviceTransactionManager(Appservice appservice) : base(storeSentTransactionsInMemory: false)
        {
            _logger = Log.ForContext<AppserviceTransactionManager>()
                         .ForContext(new PropertyEnricher("AppserviceId", appservice.Id));

            _appservice = appservice;
            if (_appservice.Enabled) StartLoop();
        }

        private void StartLoop()
        {
            _sendingTransactions = true;

            _sendLoop = Task.Run(() =>
            {
                while (_sendingTransactions)
                {
                    // TODO: Proper notification of pending transactions
                    var txnToSend = GetTransactionToSend();

                    if (txnToSend != null)
                    {
                        _logger.Information("Got txn to send with {0} events", txnToSend.Elements.Count);
                        _logger.Information("Flagging txn as sent");
                        FlagSent(txnToSend);
                    }
                }
            });
        }

        public void StopLoop()
        {
            _sendingTransactions = false;
            _sendLoop?.GetAwaiter().GetResult();
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
