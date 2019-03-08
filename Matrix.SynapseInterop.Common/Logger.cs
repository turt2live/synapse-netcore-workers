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
            var sLevel = logConfig.GetValue<string>("level");
            sLevel = $"{char.ToUpper(sLevel[0])}{sLevel.Substring(1)}";

            if (!Enum.TryParse(sLevel, out LogEventLevel level))
            {
                Log.Information("Logging level not configured or understood. Setting to Information");
                level = LogEventLevel.Information;
            }

            Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .Filter.ByIncludingOnly(e => e.Level >= level)
                        .WriteTo
                        .Console(outputTemplate:
                                 "{Timestamp:yy-MM-dd HH:mm:ss.fff} {Level:u3} {SourceContext:lj} {@Properties} {Message:lj}{NewLine}{Exception}")
                        .CreateLogger();
        }
    }
}
