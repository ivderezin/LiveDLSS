using LiveDLSS.Core.Logging;
using Makaretu.Dns;

namespace LiveDLSS.Features.Discovery;

public sealed class ServiceAdvertiser : IDisposable
{
    public const string ServiceType = "_livedlss._tcp";

    private readonly ServiceDiscovery _discovery;
    private readonly ServiceProfile _profile;

    public ServiceAdvertiser(string instanceName, ushort port)
    {
        _profile = new ServiceProfile(instanceName, ServiceType, port);
        _discovery = new ServiceDiscovery();
        _discovery.Advertise(_profile);
        _discovery.Announce(_profile);
        Log.Info($"mDNS: advertising '{instanceName}' as {ServiceType} on port {port}");
    }

    public void Dispose()
    {
        _discovery.Unadvertise(_profile);
        _discovery.Dispose();
    }
}
