using Matrix.SynapseInterop.Replication.DataRows;
using System;
using System.Collections.Generic;

namespace Matrix.SynapseInterop.Replication
{
    public class ReplicationStreamName
    {
        public static readonly string EVENTS = "events";
        public static readonly string BACKFILL = "backfill";
        public static readonly string PRESENCE = "presence";
        public static readonly string TYPING = "typing";
        public static readonly string RECEIPTS = "receipts";
        public static readonly string PUSH_RULES = "push_rules";
        public static readonly string PUSHERS = "pushers";
        public static readonly string CACHES = "caches";
        public static readonly string PUBLIC_ROOMS = "public_rooms";
        public static readonly string DEVICE_LISTS = "device_lists";
        public static readonly string TO_DEVICE = "to_device";
        public static readonly string FEDERATION_OUTBOUND_QUEUE = "federation";
        public static readonly string TAG_ACCOUNT_DATA = "tag_account_data";
        public static readonly string ACCOUNT_DATA = "account_data";
        public static readonly string CURRENT_STATE_DELTAS = "current_state_deltas";
        public static readonly string GROUPS = "groups";
    }

    public class ReplicationStream<T> where T : IReplicationDataRow
    {
        private static Dictionary<Type, string> DATA_ROW_STREAM_NAMES = new Dictionary<Type, string>()
        {
            { typeof(EventStreamRow), ReplicationStreamName.EVENTS },
        };

        private static Dictionary<string, Func<string, IReplicationDataRow>> DATA_ROW_FACTORIES = new Dictionary<string, Func<string, IReplicationDataRow>>()
        {
            {  ReplicationStreamName.EVENTS, (raw) => EventStreamRow.FromRaw(raw) },
        };

        public event EventHandler<T> DataRow;
        public event EventHandler<string> PositionUpdate;

        private string _position;

        public string StreamName { get; private set; }
        public string CurrentPosition
        {
            get { return _position; }
            private set
            {
                _position = value;
                PositionUpdate?.Invoke(this, _position);
            }
        }

        internal ReplicationStream(SynapseReplication replicationHost, string resumeFrom)
        {
            StreamName = DATA_ROW_STREAM_NAMES[typeof(T)];
            if (string.IsNullOrWhiteSpace(StreamName)) throw new ArgumentException("No stream for data row type");

            if (string.IsNullOrWhiteSpace(resumeFrom)) resumeFrom = StreamPosition.LATEST;
            replicationHost.RData += ReplicationHost_RData;
            replicationHost.PositionUpdate += ReplicationHost_PositionUpdate;
            replicationHost.SubscribeStream(StreamName, resumeFrom);
        }

        private void ReplicationHost_PositionUpdate(object sender, StreamPosition e)
        {
            if (e.StreamName == this.StreamName) this.CurrentPosition = e.Position;
        }

        private void ReplicationHost_RData(object sender, ReplicationData e)
        {
            if (e.SteamName != this.StreamName) return;

            this.CurrentPosition = e.Position;
            foreach (var row in e.RawRows)
            {
                var dataRow = DATA_ROW_FACTORIES[this.StreamName](row);
                DataRow?.Invoke(this, (T)dataRow);
            }
        }
    }
}
