using Matrix.SynapseInterop.Database.SynapseModels;

namespace Matrix.SynapseInterop.Worker.AppserviceSender.Transactions
{
    public class QueuedEvent
    {
        public EventJson Event { get; }
        public string Sender { get; }
        public string StateKey { get; }
        public string EventJson => Event.Json;

        public QueuedEvent(EventJson ev, string sender, string stateKey)
        {
            Event = ev;
            Sender = sender;
            StateKey = stateKey;
        }
    }
}
