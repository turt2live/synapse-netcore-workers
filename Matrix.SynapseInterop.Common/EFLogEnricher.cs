using Serilog.Core;
using Serilog.Events;

namespace Matrix.SynapseInterop.Common
{
    public class EfLogEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            string ctx = logEvent.Properties["SourceContext"].ToString();
            
            if (!ctx.StartsWith("\"Microsoft.EntityFrameworkCore."))
            {
                return;
            }

            if (ctx.EndsWith("Database.Command\""))
            {
                //logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("newLine", " "));

                if ((logEvent.Properties["commandText"] as ScalarValue)?.Value is string command)
                    logEvent.AddOrUpdateProperty(property: propertyFactory.CreateProperty("commandText",
                                                                                          command.Replace("\n", " ")));

                logEvent.RemovePropertyIfPresent("EventId");
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("parameters",""));
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("newLine",""));
            }
        }
    }
}
