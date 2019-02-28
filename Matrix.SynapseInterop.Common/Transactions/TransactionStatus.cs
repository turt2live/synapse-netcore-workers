namespace Matrix.SynapseInterop.Common.Transactions
{
    public enum TransactionStatus
    {
        // The transaction hasn't been queued for sending yet and can be added to.
        NEW,

        // The transaction is queued for delivery to a destination. It should not be modified.
        QUEUED,

        // The transaction has been sent to the destination. It should not be modified.
        SENT
    }
}
