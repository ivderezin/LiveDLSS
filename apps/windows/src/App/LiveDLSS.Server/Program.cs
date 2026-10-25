using System.Globalization;
using LiveDLSS.Core.Logging;
using LiveDLSS.Protocol;
using LiveDLSS.Server;

var options = ServerOptions.Parse(args);
var version = typeof(ServerOptions).Assembly.GetName().Version?.ToString(3);
Log.Info($"LiveDLSS Server {version} starting (port {options.Port}, debug layer: {options.DebugLayer})");

try
{
    await using var app = new Application(options);
    app.Run();
    return 0;
}
catch (Exception e)
{
    Log.Error(e.ToString());
    return 1;
}

namespace LiveDLSS.Server
{
    internal sealed record ServerOptions(int Port, bool DebugLayer, uint NvidiaAppId, string NeuralRuntimeDirectory)
    {
        public static ServerOptions Parse(string[] args)
        {
            var port = (int)MessageHeader.DefaultPort;
            var debug = false;
            uint appId = 0;
            var neuralRuntimeDirectory = AppContext.BaseDirectory;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port" when i + 1 < args.Length:
                        port = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--debug-layer":
                        debug = true;
                        break;
                    case "--nr-runtime" when i + 1 < args.Length:
                        neuralRuntimeDirectory = Path.GetFullPath(args[++i]);
                        break;
                    case "--nv-app-id" when i + 1 < args.Length:
                        appId = uint.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                }
            }
            return new ServerOptions(port, debug, appId, neuralRuntimeDirectory);
        }
    }
}
