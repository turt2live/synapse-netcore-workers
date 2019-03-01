using System.Collections.Generic;

namespace Matrix.SynapseInterop.Replication
{
    public class ReplicationData
    {
        private readonly List<string> _rawRows = new List<string>();

        public string SteamName { get; }
        public string Position { get; internal set; }
        public ICollection<string> RawRows => _rawRows.AsReadOnly();

        internal ReplicationData(string streamName)
        {
            SteamName = streamName;
        }

        internal void AppendRow(string rawRow)
        {
            _rawRows.Add(rawRow);
        }
    }
}
