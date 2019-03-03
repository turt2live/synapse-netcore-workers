using System;
using System.Collections.Generic;
using System.Linq;
using Routable;

namespace Matrix.SynapseInterop.Common
{
    public class RoutableSerilogLogger : ILogger
    {
        private readonly Serilog.ILogger _logger;

        public RoutableSerilogLogger(Serilog.ILogger logger)
        {
            _logger = logger;
        }

        public void Write(LogClass logClass,
                          string message,
                          Exception exception = null,
                          IReadOnlyDictionary<string, string> data = null
        )
        {
            Action<string> writeFn = _logger.Information;

            if (logClass == LogClass.Debug) writeFn = _logger.Debug;
            else if (logClass == LogClass.Warning) writeFn = _logger.Warning;
            else if (logClass == LogClass.Error) writeFn = _logger.Error;
            else if (logClass == LogClass.Security) writeFn = _logger.Warning;

            writeFn(message);

            if (exception != null)
            {
                Action<Exception, string> exceptionWriteFn = _logger.Information;

                if (logClass == LogClass.Debug) exceptionWriteFn = _logger.Debug;
                else if (logClass == LogClass.Warning) exceptionWriteFn = _logger.Warning;
                else if (logClass == LogClass.Error) exceptionWriteFn = _logger.Error;
                else if (logClass == LogClass.Security) exceptionWriteFn = _logger.Warning;

                exceptionWriteFn(exception, "Exception in Routable caught");
            }

            if (data != null && data.Any())
            {
                writeFn("Additional data:");
                foreach (var pair in data) writeFn($"\t{pair.Key ?? "<No Key>"}: {pair.Value ?? "<No Value>"}");
            }
        }
    }
}
