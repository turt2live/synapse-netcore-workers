using System.Collections.Generic;
using System.Linq;
using Matrix.SynapseInterop.Common.Extensions;
using Matrix.SynapseInterop.Database;
using Matrix.SynapseInterop.Database.SynapseModels;
using Matrix.SynapseInterop.Replication;
using Matrix.SynapseInterop.Replication.DataRows;
using Matrix.SynapseInterop.Worker.AppserviceSender.Transactions;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Matrix.SynapseInterop.Worker.AppserviceSender
{
    internal class AppserviceManager
    {
        private static readonly ILogger Log = Serilog.Log.ForContext<AppserviceManager>();

        private readonly SynapseReplication _replication;

        private readonly Dictionary<string, AppserviceTransactionManager> _txnManagers =
            new Dictionary<string, AppserviceTransactionManager>();

        private string _serverName;

        public AppserviceManager(SynapseReplication replication)
        {
            _replication = replication;
            _replication.ServerName += Replication_ServerName;

            // TODO: Restore stream position from database
            var stream = _replication.ResumeStream<EventStreamRow>(StreamPosition.LATEST);
            stream.DataRow += ReplStream_DataRow;
            stream.PositionUpdate += ReplStream_PositionUpdate;
        }

        private void Replication_ServerName(object sender, string e)
        {
            if (!string.IsNullOrWhiteSpace(_serverName)) return;

            _serverName = e;

            // TODO: When an appservice is modified (added/disabled/etc), alert this class
            BuildTransactionManagers();
        }

        private void ReplStream_PositionUpdate(object sender, string e)
        {
            Log.Information("Event stream now at position {0}", e);
        }

        private void ReplStream_DataRow(object sender, EventStreamRow e)
        {
            Log.Information("Received data row {0}@{1}/{2}", e.RoomId, e.EventId, e.EventType);

            Event evMeta;
            EventJson ev;

            using (var synapseDb = new SynapseDbContext())
            {
                ev = synapseDb.EventsJson.SingleOrDefault(e2 => e2.RoomId == e.RoomId && e2.EventId == e.EventId);
                evMeta = synapseDb.Events.SingleOrDefault(e2 => e2.RoomId == e.RoomId && e2.EventId == e.EventId);
            }

            if (ev == null || evMeta == null) Log.Warning("Received unknown event");
            else _txnManagers.Values.ForEach(m => m.QueueIfInterested(new QueuedEvent(ev, evMeta.Sender, e.StateKey)));
        }

        public void Stop()
        {
            _replication.Disconnect();
        }

        private void BuildTransactionManagers()
        {
            using (var db = new AppserviceDb())
            {
                db.Appservices.Include(a => a.Namespaces).ToArray().ForEach(a =>
                {
                    Log.Information("Creating manager for appservice: {0}", a.Id);
                    _txnManagers.Add(a.Id, new AppserviceTransactionManager(a, _serverName));
                });
            }
        }
    }
}
