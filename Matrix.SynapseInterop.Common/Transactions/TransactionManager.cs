using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace Matrix.SynapseInterop.Common.Transactions
{
    public abstract class TransactionManager<T> where T : class
    {
        private static readonly Random RANDOM = new Random();

        private readonly ConcurrentQueue<Transaction<T>> _queuedTransactions = new ConcurrentQueue<Transaction<T>>();
        private readonly bool _storeSent;

        private readonly ConcurrentDictionary<string, Transaction<T>> _transactions =
            new ConcurrentDictionary<string, Transaction<T>>();

        private Transaction<T> _inFlightTxn;

        protected ILogger _logger = Log.ForContext<TransactionManager<T>>();

        private readonly int _maxElements;

        public TransactionManager(int maxElementsPerTransaction = 50, bool storeSentTransactionsInMemory = true)
        {
            _maxElements = maxElementsPerTransaction;
            _storeSent = storeSentTransactionsInMemory;

            var pending = LoadTransactionQueue();

            foreach (var txn in pending)
            {
                _queuedTransactions.Enqueue(txn);
                _transactions[txn.Id] = txn;
            }

            var buffers = LoadTransactionsWithStatus(TransactionStatus.NEW);
            foreach (var txn in buffers) _transactions[txn.Id] = txn;
        }

        /// <summary>
        ///     Persists a transaction to a data store. This can happen when a transaction's elements
        ///     change, or its status changes. New transactions will also be run through this.
        /// </summary>
        /// <param name="transaction">The transaction to persist</param>
        protected abstract void PersistTransaction(Transaction<T> transaction);

        /// <summary>
        ///     Persists the queue of transactions to send
        /// </summary>
        /// <param name="queue">The transactions to persist</param>
        protected abstract void PersistTransactionQueue(Transaction<T>[] queue);

        /// <summary>
        ///     Loads the queue of transactions to send to the destination. If no transactions are queued,
        ///     this should return an empty queue.
        /// </summary>
        /// <returns>The queue of transactions to send. May be empty.</returns>
        protected abstract Transaction<T>[] LoadTransactionQueue();

        /// <summary>
        ///     Loads the transactions with a given status, returning an empty collection if none match the criteria.
        /// </summary>
        /// <param name="status">The status to look for</param>
        /// <returns>The transactions matching the criteria. May be empty.</returns>
        protected abstract ICollection<Transaction<T>> LoadTransactionsWithStatus(TransactionStatus status);

        /// <summary>
        ///     Loads a transaction by ID. If the transaction is not found, this should return null.
        /// </summary>
        /// <param name="transactionId">The transaction ID to load</param>
        /// <returns>The transaction, or null if it is not found</returns>
        protected abstract Transaction<T> LoadTransaction(string transactionId);

        protected string GetNextId()
        {
            string proposedId = null;

            do
            {
                // Source: https://stackoverflow.com/a/1344242
                var length = 12;
                const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";

                var randomBits =
                    new string(Enumerable.Repeat(chars, length).Select(s => s[RANDOM.Next(s.Length)]).ToArray());

                proposedId = DateTime.Now.ToBinary() + "_" + randomBits;
            } while (GetTransaction(proposedId) != null);

            return proposedId;
        }

        /// <summary>
        ///     Retrieves a transaction.
        /// </summary>
        /// <param name="id">The transaction ID to find</param>
        /// <returns>The transaction, or null if not found</returns>
        public Transaction<T> GetTransaction(string id)
        {
            if (!_transactions.ContainsKey(id))
            {
                var txn = LoadTransaction(id);
                if (txn == null) return null;

                if (txn.Id != id)
                    throw new InvalidProgramException("Loaded transaction that didn't match the one requested");

                if (txn.Status != TransactionStatus.SENT || _storeSent) _transactions[id] = txn;
                return txn;
            }

            return _transactions[id];
        }

        /// <summary>
        ///     Gets the next transaction that should be sent to the destination.
        /// </summary>
        /// <returns>The next transaction that should be sent, or null if none ready</returns>
        public Transaction<T> GetTransactionToSend()
        {
            if (_inFlightTxn != null) return _inFlightTxn;

            Transaction<T> txn;

            if (_queuedTransactions.TryDequeue(out txn))
            {
                _inFlightTxn = txn;
                return txn;
            }

            RotatePendingTransaction();

            if (_queuedTransactions.TryDequeue(out txn))
            {
                _inFlightTxn = txn;
                return txn;
            }

            return null;
        }

        public void FlagSent(Transaction<T> transaction)
        {
            if (transaction != _inFlightTxn)
                throw new InvalidOperationException("The transaction that was sent is not the in flight transaction");

            transaction.Status = TransactionStatus.SENT;
            PersistTransaction(transaction);

            _inFlightTxn = null;

            if (!_storeSent && _transactions.ContainsKey(transaction.Id))
            {
                Transaction<T> discard;

                if (!_transactions.TryRemove(transaction.Id, out discard))
                    _logger.Warning("Failed to remove {0} from the transaction dictionary", transaction.Id);
            }
        }

        public void QueueElement(T element)
        {
            QueueElements(new[] {element});
        }

        public void QueueElements(ICollection<T> elements)
        {
            var buffer = GetBufferedTransaction();

            if (buffer.Elements.Count + elements.Count > _maxElements)
            {
                RotatePendingTransaction();
                buffer = GetBufferedTransaction();
            }

            buffer.AddItems(elements);
        }

        private Transaction<T> GetBufferedTransaction()
        {
            var pendingTxn = _transactions.Values.FirstOrDefault(t => t.Status == TransactionStatus.NEW);
            if (pendingTxn == null) return CreateTransaction();
            return pendingTxn;
        }

        private void RotatePendingTransaction()
        {
            var pendingTxn = _transactions.Values.FirstOrDefault(t => t.Status == TransactionStatus.NEW);
            if (pendingTxn == null) return;
            if (!pendingTxn.Elements.Any()) return;

            pendingTxn.Status = TransactionStatus.QUEUED;
            PersistTransaction(pendingTxn);
            _queuedTransactions.Enqueue(pendingTxn);

            var fullQueue = new List<Transaction<T>>();
            if (_inFlightTxn != null) fullQueue.Add(_inFlightTxn);
            fullQueue.AddRange(_queuedTransactions.ToArray());
            PersistTransactionQueue(fullQueue.ToArray());

            CreateTransaction();
        }

        private Transaction<T> CreateTransaction()
        {
            var nextId = GetNextId();
            var txn = new Transaction<T>(nextId);
            PersistTransaction(txn);
            _transactions.AddOrUpdate(nextId, txn, (k, v) => txn);

            return txn;
        }
    }
}
