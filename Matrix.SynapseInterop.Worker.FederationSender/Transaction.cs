using Matrix.SynapseInterop.Replication.DataRows;

namespace Matrix.SynapseInterop.Worker.FederationSender
{
    public struct Transaction
    {
        public string transaction_id;
        public string origin;
        public string destination;
        public long origin_server_ts;
        public string[] previous_ids; // Not required.
        public EduEvent[] edus; 
        public PduEvent[] pdus; // Not required.
    }
}