using System;
using System.Collections.Generic;

namespace Matrix.SynapseInterop.Common.Transactions
{
    public class Transaction<T> where T : class
    {
        private List<T> _elements = new List<T>();

        public ICollection<T> Elements { get => _elements.AsReadOnly(); }

        public string Id { get; private set; }

        public TransactionStatus Status { get; internal set; }

        public Transaction(string id) : this(id, new T[0]) { }

        public Transaction(string id, ICollection<T> items)
        {
            this.Id = id;
            this.AddItems(items);
        }

        internal void AddItems(ICollection<T> items)
        {
            if (Status != TransactionStatus.NEW)
                throw new InvalidOperationException("Cannot modify a transaction which is not new");
            
            _elements.AddRange(items);
        }
    }
}
