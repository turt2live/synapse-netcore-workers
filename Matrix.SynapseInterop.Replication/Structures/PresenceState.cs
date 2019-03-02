namespace Matrix.SynapseInterop.Replication.Structures
{
    public struct PresenceState
    {
        public string state;
        public string user_id;
        public ulong last_user_sync_ts;
        public ulong last_active_ts;
        public ulong last_federation_update_ts;
        public string status_msg;
        public bool currently_active;
    }
}
