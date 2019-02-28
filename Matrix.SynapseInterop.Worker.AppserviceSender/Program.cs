using Matrix.SynapseInterop.Worker.AppserviceSender.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Routable;
using Routable.Kestrel;
using System;
using System.Net;

namespace Matrix.SynapseInterop.Worker.AppserviceSender
{
    internal class Program
    {
        private static IConfiguration _config;

        static void Main(string[] args)
        {
            Console.WriteLine("Starting appservice sender...");


            _config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.default.json", true, true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            AppserviceDb.ConnectionString = _config.GetConnectionString("appserviceWorker");

            var kestrelConfig = _config.GetSection("Kestrel");
            var host = new WebHostBuilder()
                .UseKestrel(options => options.Listen(IPAddress.Parse(kestrelConfig.GetValue<string>("bindHost")), kestrelConfig.GetValue<int>("bindPort")))
                .Configure(builder => builder.UseRoutable(options => options
                    .WithJsonSupport()
                    .AddRouting(new AppserviceAdminRouting(options))
                    .OnError(new KestrelRouting(options)
                    {
                        _ => _.Do((context, request, response) =>
                        {
                            response.Status = 500;
                            response.ContentType = "application/json";
                            response.Write(JObject.FromObject(new {error="Internal server error", errcode="M_UNKNOWN" }));
                        })
                    })
                ))
                .Build();

            host.Run();
        }
    }
}
