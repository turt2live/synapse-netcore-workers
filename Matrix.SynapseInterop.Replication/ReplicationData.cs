using System.Collections.Generic;

namespace Matrix.SynapseInterop.Replication
{
    public class ReplicationData
    {
        public string SteamName { get; private set; }
        public string Position { get; internal set; }

        private List<string> _rawRows = new List<string>();
        public ICollection<string> RawRows { get { return _rawRows.AsReadOnly(); } }

        internal ReplicationData(string streamName)
        {
            this.SteamName = streamName;
        }

        internal void AppendRow(string rawRow)
        {
            _rawRows.Add(rawRow);
        }
    }
}
