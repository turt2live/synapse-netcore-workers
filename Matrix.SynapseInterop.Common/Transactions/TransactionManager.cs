using System;
using System.Collections.Generic;
using System.Linq;
using Matrix.SynapseInterop.Common.Extensions;

namespace Matrix.SynapseInterop.Common.Transactions
{
    public abstract class TransactionManager<T> where T : class
    {
        private static readonly Random RANDOM = new Random();
        private readonly List<Transaction<T>> _queuedTransactions = new List<Transaction<T>>();
        private readonly bool _storeSent;

        private readonly Dictionary<string, Transaction<T>> _transactions = new Dictionary<string, Transaction<T>>();

        private int _maxElements;

        public TransactionManager(int maxElementsPerTransaction = 50, bool storeSentTransactionsInMemory = true)
        {
            _maxElements = maxElementsPerTransaction;
            _storeSent = storeSentTransactionsInMemory;

            var pending = LoadTransactionQueue();

            foreach (var txn in pending)
            {
                _queuedTransactions.Add(txn);
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
        /// <param name="queue">The transactions to send</param>
        protected abstract void PersistTransactionQueue(List<Transaction<T>> queue);

        /// <summary>
        ///     Loads the queue of transactions to send to the destination. If no transactions are queued,
        ///     this should return an empty queue.
        /// </summary>
        /// <returns>The queue of transactions to send. May be empty.</returns>
        protected abstract List<Transaction<T>> LoadTransactionQueue();

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

                if (txn.Id != id)
                    throw new InvalidProgramException("Loaded transaction that didn't match the one requested");

                if (txn.Status != TransactionStatus.SENT || _storeSent) _transactions[id] = txn;
            }

            return _transactions[id];
        }

        /// <summary>
        ///     Gets the next transaction that should be sent to the destination.
        /// </summary>
        /// <returns>The next transaction that should be sent, or null if none ready</returns>
        public Transaction<T> GetTransactionToSend()
        {
            if (_queuedTransactions.Any())
                return _queuedTransactions[0];

            RotatePendingTransaction();

            if (_queuedTransactions.Any())
                return _queuedTransactions[0];

            return null;
        }

        public void FlagSent(Transaction<T> transaction)
        {
            transaction.Status = TransactionStatus.SENT;
            PersistTransaction(transaction);

            if (_queuedTransactions.Contains(transaction))
            {
                _queuedTransactions.Remove(transaction);
                PersistTransactionQueue(_queuedTransactions.Clone());
            }

            if (!_storeSent && _transactions.ContainsKey(transaction.Id))
                _transactions.Remove(transaction.Id);
        }

        public void QueueElements(ICollection<T> elements)
        {
            var buffer = GetBufferedTransaction();
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
            _queuedTransactions.Add(pendingTxn);
            PersistTransactionQueue(_queuedTransactions.Clone());

            CreateTransaction();
        }

        private Transaction<T> CreateTransaction()
        {
            var nextId = GetNextId();
            var txn = new Transaction<T>(nextId);
            PersistTransaction(txn);
            _transactions[nextId] = txn;

            return txn;
        }
    }
}
