using Matrix.SynapseInterop.Database.SynapseModels;

namespace Matrix.SynapseInterop.Worker.AppserviceSender.Transactions
{
    public class QueuedEvent
    {
        public string EventJson { get; }

        public QueuedEvent(EventJson ev)
        {
            EventJson = ev.Json;
        }
    }
}
