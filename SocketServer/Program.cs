using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using log4net;
using log4net.Config;
using PowerArgs;

namespace SocketServer
{
    internal static class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);

        private static async Task Main(string[] args)
        {
            var loggerRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(loggerRepository, new FileInfo("log4net.config"));

            try
            {
                Log.Info($"Args: {string.Join(' ', args)}");
                var serverArgs = await Args.ParseAsync<ServerArgs>(args);
                if (serverArgs == null)
                    return;

                var ipEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), serverArgs.Port);
                var server = new SocketServer(ipEndPoint);
                server.Start();

                Console.WriteLine(
                    $"Server listening on port {serverArgs.Port}. Press any key to terminate the server...");
                Console.ReadLine();

                server.Stop();

                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            catch (ArgException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ArgUsage.GenerateUsageFromTemplate<ServerArgs>());

                Log.Error(ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message, ex);
            }
        }
    }
}