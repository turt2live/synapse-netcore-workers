using System;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Matrix.SynapseInterop.Common
{
    public static class Logger
    {
        public static void Setup(IConfigurationSection logConfig)
        {
            if (!Enum.TryParse(logConfig.GetValue<string>("level"), out LogEventLevel level))
                level = LogEventLevel.Information;

            Log.Logger = new LoggerConfiguration()
                        .Filter
                        .ByIncludingOnly(e => e.Level >= level)
                        .WriteTo
                        .Console(outputTemplate:
                                 "{Timestamp:yy-MM-dd HH:mm:ss.fff} {Level:u3} {SourceContext:lj} {Message:lj}{NewLine}{Exception}")
                        .CreateLogger();
        }
    }
}
