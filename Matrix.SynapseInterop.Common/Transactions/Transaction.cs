using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Matrix.SynapseInterop.Common.Extensions;

namespace Matrix.SynapseInterop.Common.Transactions
{
    public class Transaction<T> where T : class
    {
        private readonly ConcurrentBag<T> _elements = new ConcurrentBag<T>();

        public ICollection<T> Elements => _elements.ToArray();

        public string Id { get; }

        public TransactionStatus Status { get; internal set; }

        public Transaction(string id) : this(id, new T[0]) { }

        public Transaction(string id, ICollection<T> items)
        {
            Id = id;
            AddItems(items);
        }

        internal void AddItems(ICollection<T> items)
        {
            if (Status != TransactionStatus.NEW)
                throw new InvalidOperationException("Cannot modify a transaction which is not new");

            items.ForEach(i => _elements.Add(i));
        }
    }
}
