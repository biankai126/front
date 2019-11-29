using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Slzhly.Core.Utils.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Slzhly.BaseApi
{
    public static class Program
    {
        /// <summary>
        /// Gets ��������
        /// </summary>
        public const string AppName = "baseapi";

        /// <summary>
        /// Gets or sets ip
        /// </summary>
        public static string IP { get; set; }

        /// <summary>
        /// Gets or sets �˿�
        /// </summary>
        public static int Port { get; set; }

        /// <summary>
        /// Gets �����ռ�
        /// </summary>
        public static string Namespace { get; } = typeof(Program).Namespace;

        /// <summary>
        /// Gets or sets ����·��
        /// </summary>
        public static string BasePath { get; set; }
        public static async Task<int> Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // var isService = !(Debugger.IsAttached || args.Contains("--console"));
            var isService = false;
            BasePath = AppContext.BaseDirectory;
            if (isService)
            {
                var pathToExe = Process.GetCurrentProcess().MainModule.FileName;
                BasePath = Path.GetDirectoryName(pathToExe);
            }
            var config = GetConfiguration(BasePath);
            IP = config["IP"];
            Port = Convert.ToInt32(config["Port"]);
            Log.Logger = CreateSerilogLogger(config);
            try
            {
                Log.Debug("Configuring host ({Application})...", AppName);
                if (string.IsNullOrEmpty(IP)) IP = NetworkHelper.LocalIPAddress;
                if (Port == 0) Port = NetworkHelper.GetRandomAvaliablePort();
                var host = CreateHostBuilder(args).Build();
                Log.Logger.Information("Starting {Application}({version}) {Service} {url} ", AppName, config["Version"], isService ? "win service" : " host", $"http://{IP}:{Port}/");
                await host.RunAsync().ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Program terminated unexpectedly ({ApplicationContext})!", AppName);
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .UseWindowsService()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                    .UseContentRoot(BasePath)
                    .UseSerilog()
                    .UseStartup<Startup>()
                    .UseUrls($"http://{IP}:{Port}");
                });
        private static Serilog.ILogger CreateSerilogLogger(IConfiguration configuration)
        {
            return new LoggerConfiguration()
                .Enrich.WithProperty("Application", AppName)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
        }
        private static IConfiguration GetConfiguration(string basepath)
        {
            var builder = new ConfigurationBuilder();
            builder.SetBasePath(basepath);
            builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            return builder.Build();
        }
    }
}
