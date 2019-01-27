using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using System;

namespace Matrix.SynapseInterop.Worker.DevWorker
{
    class Program
    {
        static void Main(string[] args)
        {
            StartReplicationAsync();

            Console.ReadKey(true);
        }

        private static async void StartReplicationAsync()
        {
            var replication = new SynapseReplication();
            replication.ClientName = "NetCoreDevWorker";
            replication.ServerName += Replication_ServerName;
            await replication.Connect("localhost", 9092);

            var stream = replication.BindStream<EventStreamRow>();
            stream.DataRow += Stream_DataRow;
        }

        private static void Stream_DataRow(object sender, EventStreamRow e)
        {
            Console.WriteLine("Received event {0} ({1}) in {2}", e.EventId, e.EventType, e.RoomId);
        }

        private static void Replication_ServerName(object sender, string e)
        {
            Console.WriteLine("Server name: " + e);
        }
    }
}
