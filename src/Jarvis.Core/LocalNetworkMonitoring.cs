using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Jarvis.Core;

internal static class LocalNetworkMonitoring
{
    private const int PingTimeoutMilliseconds = 325;
    private const int PortProbeTimeoutMilliseconds = 180;
    private const int MaxConcurrentPings = 48;
    private const int MaxDevicesWithServiceProbe = 12;

    private static readonly IReadOnlyList<PortProbe> CommonPortProbes =
    [
        new(22, "ssh"),
        new(53, "dns"),
        new(80, "http"),
        new(443, "https"),
        new(445, "smb"),
        new(515, "lpd"),
        new(631, "ipp"),
        new(3389, "rdp"),
        new(9100, "jetdirect")
    ];

    private static readonly Regex ArpEntryRegex = new(
        @"^\s*(?<ip>(?:\d{1,3}\.){3}\d{1,3})\s+(?<mac>(?:[0-9a-f]{2}-){5}[0-9a-f]{2})\s+(?<type>\w+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task<string> GetStatusAsync(string target, CancellationToken cancellationToken)
    {
        var request = ParseRequest(target);

        if (request.Mode == NetworkRequestMode.Inspect && !string.IsNullOrWhiteSpace(request.Target))
        {
            return await InspectTargetAsync(request.Target, cancellationToken);
        }

        var adapters = GetActiveAdapters();

        if (adapters.Count == 0)
        {
            return "No active IPv4 local-network adapters were detected.";
        }

        var snapshot = await BuildSnapshotAsync(adapters, request.Mode, cancellationToken);
        return RenderSnapshot(snapshot, request.Mode);
    }

    private static NetworkRequest ParseRequest(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return new NetworkRequest(NetworkRequestMode.Overview, string.Empty);
        }

        var trimmed = target.Trim();
        var normalized = trimmed.ToLowerInvariant();

        if (normalized is "scan" or "devices" or "discover" or "discovery")
        {
            return new NetworkRequest(NetworkRequestMode.Scan, string.Empty);
        }

        if (normalized is "health" or "status")
        {
            return new NetworkRequest(NetworkRequestMode.Health, string.Empty);
        }

        return new NetworkRequest(NetworkRequestMode.Inspect, trimmed);
    }

    private static async Task<NetworkSnapshot> BuildSnapshotAsync(
        IReadOnlyList<ActiveAdapter> adapters,
        NetworkRequestMode mode,
        CancellationToken cancellationToken)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var candidates = BuildScanCandidates(adapters);
        var discovered = await ProbeCandidatesAsync(candidates, adapters, mode, cancellationToken);
        var elapsed = DateTimeOffset.UtcNow - startedUtc;

        foreach (var adapter in adapters)
        {
            foreach (var gateway in adapter.Gateways)
            {
                adapter.GatewayResponded = discovered.Any(device => device.Address.Equals(gateway));
            }
        }

        return new NetworkSnapshot(
            adapters.ToArray(),
            discovered
                .OrderByDescending(device => device.IsGateway)
                .ThenBy(device => device.RoundtripMilliseconds ?? long.MaxValue)
                .ThenBy(device => device.Address.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            candidates.Count,
            elapsed);
    }

    private static IReadOnlyList<ActiveAdapter> GetActiveAdapters()
    {
        var adapters = new List<ActiveAdapter>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            if (!TryCreateAdapter(networkInterface, out var adapter))
            {
                continue;
            }

            adapters.Add(adapter);
        }

        return adapters
            .OrderByDescending(adapter => adapter.HasGateway)
            .ThenByDescending(adapter => adapter.SpeedBitsPerSecond)
            .ThenBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryCreateAdapter(NetworkInterface networkInterface, out ActiveAdapter adapter)
    {
        var properties = networkInterface.GetIPProperties();
        var unicast = properties.UnicastAddresses
            .FirstOrDefault(candidate =>
                candidate.Address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(candidate.Address)
                && !IsLinkLocal(candidate.Address)
                && IsPrivateIPv4(candidate.Address));

        if (unicast is null)
        {
            adapter = default!;
            return false;
        }

        var mask = unicast.IPv4Mask;

        if (mask is null || mask.Equals(IPAddress.Any))
        {
            adapter = default!;
            return false;
        }

        var prefixLength = CountMaskBits(mask);

        if (prefixLength <= 0 || prefixLength > 30)
        {
            adapter = default!;
            return false;
        }

        var gateways = properties.GatewayAddresses
            .Select(candidate => candidate.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToArray();
        var dnsServers = properties.DnsAddresses
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Distinct()
            .ToArray();
        var networkAddress = GetNetworkAddress(unicast.Address, mask);

        adapter = new ActiveAdapter(
            networkInterface.Name.Trim(),
            networkInterface.NetworkInterfaceType.ToString(),
            unicast.Address,
            prefixLength,
            networkAddress,
            gateways,
            dnsServers,
            networkInterface.Speed,
            networkInterface.Description.Trim());
        return true;
    }

    private static IReadOnlyList<ScanCandidate> BuildScanCandidates(IReadOnlyList<ActiveAdapter> adapters)
    {
        var candidates = new Dictionary<string, ScanCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in adapters)
        {
            foreach (var candidateAddress in EnumerateCandidatesForAdapter(adapter))
            {
                var key = candidateAddress.ToString();

                if (candidates.TryGetValue(key, out var existing))
                {
                    candidates[key] = existing with
                    {
                        AdapterNames = existing.AdapterNames
                            .Concat([adapter.Name])
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        IsGateway = existing.IsGateway || adapter.Gateways.Any(gateway => gateway.Equals(candidateAddress))
                    };
                    continue;
                }

                candidates[key] = new ScanCandidate(
                    candidateAddress,
                    [adapter.Name],
                    adapter.Gateways.Any(gateway => gateway.Equals(candidateAddress)));
            }
        }

        return candidates.Values.ToArray();
    }

    private static IEnumerable<IPAddress> EnumerateCandidatesForAdapter(ActiveAdapter adapter)
    {
        var addresses = new HashSet<IPAddress>();
        var scanUsesWindow = adapter.PrefixLength < 24;

        if (scanUsesWindow)
        {
            foreach (var candidate in EnumerateLocalWindow(adapter.Address))
            {
                addresses.Add(candidate);
            }

            foreach (var gateway in adapter.Gateways)
            {
                foreach (var candidate in EnumerateLocalWindow(gateway))
                {
                    addresses.Add(candidate);
                }
            }
        }
        else
        {
            var network = ToUInt32(adapter.NetworkAddress);
            var hostCount = Math.Min((1u << (32 - adapter.PrefixLength)) - 2u, 254u);

            for (var offset = 1u; offset <= hostCount; offset++)
            {
                addresses.Add(FromUInt32(network + offset));
            }
        }

        addresses.Remove(adapter.Address);

        foreach (var gateway in adapter.Gateways)
        {
            addresses.Add(gateway);
        }

        return addresses;
    }

    private static IEnumerable<IPAddress> EnumerateLocalWindow(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        if (bytes.Length != 4)
        {
            yield break;
        }

        for (var host = 1; host <= 254; host++)
        {
            yield return new IPAddress([bytes[0], bytes[1], bytes[2], (byte)host]);
        }
    }

    private static async Task<IReadOnlyList<DiscoveredDevice>> ProbeCandidatesAsync(
        IReadOnlyList<ScanCandidate> candidates,
        IReadOnlyList<ActiveAdapter> adapters,
        NetworkRequestMode mode,
        CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(MaxConcurrentPings, MaxConcurrentPings);
        var results = new ConcurrentBag<PingResult>();
        var pingTasks = candidates.Select(async candidate =>
        {
            await semaphore.WaitAsync(cancellationToken);

            try
            {
                var probe = await PingAsync(candidate.Address, cancellationToken);

                if (probe is not null)
                {
                    results.Add(new PingResult(candidate, probe.Value));
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(pingTasks);
        var arpEntries = TryReadArpCache();
        var responsive = results
            .OrderByDescending(result => result.Candidate.IsGateway)
            .ThenBy(result => result.RoundtripMilliseconds)
            .ToArray();
        var probeServices = mode == NetworkRequestMode.Health || mode == NetworkRequestMode.Inspect;
        var maxServiceProbes = probeServices ? MaxDevicesWithServiceProbe : Math.Min(MaxDevicesWithServiceProbe / 2, responsive.Length);
        var devices = new List<DiscoveredDevice>(responsive.Length);

        for (var index = 0; index < responsive.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = responsive[index];
            var hostName = await TryResolveHostNameAsync(result.Candidate.Address, cancellationToken);
            var services = index < maxServiceProbes
                ? await ProbeServicesAsync(result.Candidate.Address, cancellationToken)
                : Array.Empty<string>();
            var macAddress = arpEntries.TryGetValue(result.Candidate.Address.ToString(), out var mac) ? mac : string.Empty;
            var adaptersForDevice = result.Candidate.AdapterNames
                .Select(name => adapters.FirstOrDefault(adapter => string.Equals(adapter.Name, name, StringComparison.OrdinalIgnoreCase)))
                .Where(adapter => adapter is not null)
                .Cast<ActiveAdapter>()
                .ToArray();

            devices.Add(new DiscoveredDevice(
                result.Candidate.Address,
                hostName,
                macAddress,
                result.RoundtripMilliseconds,
                services,
                result.Candidate.IsGateway,
                adaptersForDevice.Select(adapter => adapter.Name).ToArray(),
                ClassifyDevice(services, result.Candidate.IsGateway)));
        }

        return devices;
    }

    private static async Task<string> InspectTargetAsync(string target, CancellationToken cancellationToken)
    {
        var resolved = await ResolveTargetAsync(target, cancellationToken);

        if (resolved is null)
        {
            return $"No IPv4 network target could be resolved for \"{target}\".";
        }

        var ping = await PingAsync(resolved.Address, cancellationToken);
        var services = await ProbeServicesAsync(resolved.Address, cancellationToken);
        var reverseDns = string.IsNullOrWhiteSpace(resolved.HostName)
            ? await TryResolveHostNameAsync(resolved.Address, cancellationToken)
            : resolved.HostName;
        var arpEntries = TryReadArpCache();
        var macAddress = arpEntries.TryGetValue(resolved.Address.ToString(), out var mac) ? mac : string.Empty;
        var lines = new List<string>
        {
            $"Network health for {target.Trim()}:",
            $"- Address: {resolved.Address}",
            $"- Reachable: {(ping is null ? "no response" : "yes")}",
            $"- Latency: {(ping is null ? "n/a" : ping.Value + " ms")}",
            $"- Reverse DNS: {(string.IsNullOrWhiteSpace(reverseDns) ? "none" : reverseDns)}",
            $"- MAC address: {(string.IsNullOrWhiteSpace(macAddress) ? "unavailable" : macAddress)}",
            $"- Services: {(services.Length == 0 ? "none detected on common ports" : string.Join(" | ", services))}",
            $"- Classification: {ClassifyDevice(services, false)}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<ResolvedTarget?> ResolveTargetAsync(string target, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(target.Trim(), out var ipAddress) && ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            return new ResolvedTarget(ipAddress, string.Empty);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(target.Trim(), cancellationToken);
            var ipv4 = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            return ipv4 is null ? null : new ResolvedTarget(ipv4, target.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<long?> PingAsync(IPAddress address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, PingTimeoutMilliseconds);

            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string[]> ProbeServicesAsync(IPAddress address, CancellationToken cancellationToken)
    {
        var services = new List<string>();

        foreach (var probe in CommonPortProbes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsPortOpenAsync(address, probe.Port, cancellationToken))
            {
                services.Add($"{probe.Name}:{probe.Port}");
            }
        }

        return services.ToArray();
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();

        try
        {
            var connectTask = client.ConnectAsync(address, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(PortProbeTimeoutMilliseconds, cancellationToken));

            if (completedTask != connectTask)
            {
                return false;
            }

            await connectTask;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> TryResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            var lookupTask = Dns.GetHostEntryAsync(address);
            var completedTask = await Task.WhenAny(lookupTask, Task.Delay(250, cancellationToken));

            if (completedTask != lookupTask)
            {
                return string.Empty;
            }

            var entry = await lookupTask;
            return entry.HostName?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, string> TryReadArpCache()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);
            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = ArpEntryRegex.Match(line);

                if (!match.Success)
                {
                    continue;
                }

                dictionary[match.Groups["ip"].Value] = match.Groups["mac"].Value.ToLowerInvariant();
            }

            return dictionary;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string RenderSnapshot(NetworkSnapshot snapshot, NetworkRequestMode mode)
    {
        var lines = new List<string>
        {
            mode == NetworkRequestMode.Health ? "Local network health:" : "Local network overview:"
        };

        foreach (var adapter in snapshot.Adapters)
        {
            var gatewayText = adapter.Gateways.Length == 0
                ? "no gateway"
                : string.Join(" | ", adapter.Gateways.Select(gateway =>
                    $"{gateway}{(adapter.GatewayResponded ? " reachable" : " no-response")}"));
            var dnsText = adapter.DnsServers.Length == 0
                ? "no DNS"
                : string.Join(" | ", adapter.DnsServers.Select(address => address.ToString()));
            var health = adapter.HasGateway && adapter.GatewayResponded ? "healthy" : adapter.HasGateway ? "degraded" : "limited";

            lines.Add(
                $"- {adapter.Name} [{adapter.Type}] | {adapter.Address}/{adapter.PrefixLength} | gateway {gatewayText} | dns {dnsText} | speed {FormatBitsPerSecond(adapter.SpeedBitsPerSecond)} | {health}");
        }

        lines.Add($"- Scan scope: {snapshot.CandidateCount} host target(s) across {snapshot.Adapters.Length} adapter(s)");
        lines.Add($"- Responding devices: {snapshot.Devices.Length} | scan time {snapshot.Elapsed.TotalSeconds:0.0}s");

        if (snapshot.Devices.Length == 0)
        {
            lines.Add("No local devices responded to the current sweep.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add("Discovered devices:");

        var maxDevices = mode switch
        {
            NetworkRequestMode.Scan => 20,
            NetworkRequestMode.Health => 16,
            _ => 12
        };

        foreach (var device in snapshot.Devices.Take(maxDevices))
        {
            var deviceParts = new List<string>
            {
                $"- {device.Address}"
            };

            if (!string.IsNullOrWhiteSpace(device.HostName))
            {
                deviceParts.Add(device.HostName);
            }

            if (device.IsGateway)
            {
                deviceParts.Add("gateway");
            }

            if (device.RoundtripMilliseconds is not null)
            {
                deviceParts.Add(device.RoundtripMilliseconds.Value + " ms");
            }

            if (device.AdapterNames.Length > 0)
            {
                deviceParts.Add("via " + string.Join("/", device.AdapterNames));
            }

            if (!string.IsNullOrWhiteSpace(device.MacAddress))
            {
                deviceParts.Add(device.MacAddress);
            }

            if (device.Services.Length > 0)
            {
                deviceParts.Add("services " + string.Join(", ", device.Services));
            }

            deviceParts.Add(device.Classification);
            lines.Add(string.Join(" | ", deviceParts));
        }

        if (snapshot.Devices.Length > maxDevices)
        {
            lines.Add($"- {snapshot.Devices.Length - maxDevices} additional device(s) omitted from the preview.");
        }

        if (snapshot.Adapters.Any(adapter => adapter.PrefixLength < 24))
        {
            lines.Add("Large private subnets are sampled within the local /24 neighborhood to keep discovery responsive.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ClassifyDevice(IReadOnlyList<string> services, bool isGateway)
    {
        if (isGateway)
        {
            return "likely router or gateway";
        }

        if (services.Any(service => service.StartsWith("jetdirect:", StringComparison.OrdinalIgnoreCase))
            || services.Any(service => service.StartsWith("ipp:", StringComparison.OrdinalIgnoreCase))
            || services.Any(service => service.StartsWith("lpd:", StringComparison.OrdinalIgnoreCase)))
        {
            return "likely printer";
        }

        if (services.Any(service => service.StartsWith("smb:", StringComparison.OrdinalIgnoreCase))
            && services.Any(service => service.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || service.StartsWith("ssh:", StringComparison.OrdinalIgnoreCase)))
        {
            return "likely NAS or file server";
        }

        if (services.Any(service => service.StartsWith("rdp:", StringComparison.OrdinalIgnoreCase)))
        {
            return "likely Windows workstation or server";
        }

        if (services.Any(service => service.StartsWith("dns:", StringComparison.OrdinalIgnoreCase))
            && services.Any(service => service.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || service.StartsWith("https:", StringComparison.OrdinalIgnoreCase)))
        {
            return "likely router, firewall, or DNS appliance";
        }

        if (services.Any(service => service.StartsWith("ssh:", StringComparison.OrdinalIgnoreCase)))
        {
            return "likely Linux, macOS, or embedded host";
        }

        return services.Count == 0 ? "online host" : "online service host";
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        return bytes[0] switch
        {
            10 => true,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
            192 when bytes[1] == 168 => true,
            _ => false
        };
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static int CountMaskBits(IPAddress mask)
    {
        var count = 0;

        foreach (var value in mask.GetAddressBytes())
        {
            var current = value;

            for (var bit = 0; bit < 8; bit++)
            {
                if ((current & 0x80) == 0x80)
                {
                    count++;
                    current <<= 1;
                    continue;
                }

                return count;
            }
        }

        return count;
    }

    private static IPAddress GetNetworkAddress(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var networkBytes = new byte[4];

        for (var index = 0; index < 4; index++)
        {
            networkBytes[index] = (byte)(addressBytes[index] & maskBytes[index]);
        }

        return new IPAddress(networkBytes);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value)
    {
        return new IPAddress(
        [
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF)
        ]);
    }

    private static string FormatBitsPerSecond(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "unknown";
        }

        double value = bitsPerSecond;
        string[] units = { "bps", "Kbps", "Mbps", "Gbps" };
        var unitIndex = 0;

        while (value >= 1000 && unitIndex < units.Length - 1)
        {
            value /= 1000;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private sealed record NetworkRequest(NetworkRequestMode Mode, string Target);

    private enum NetworkRequestMode
    {
        Overview,
        Scan,
        Health,
        Inspect
    }

    private sealed record ActiveAdapter(
        string Name,
        string Type,
        IPAddress Address,
        int PrefixLength,
        IPAddress NetworkAddress,
        IPAddress[] Gateways,
        IPAddress[] DnsServers,
        long SpeedBitsPerSecond,
        string Description)
    {
        public bool HasGateway => Gateways.Length > 0;

        public bool GatewayResponded { get; set; }
    }

    private sealed record ScanCandidate(IPAddress Address, string[] AdapterNames, bool IsGateway);

    private sealed record PingResult(ScanCandidate Candidate, long RoundtripMilliseconds);

    private sealed record NetworkSnapshot(
        ActiveAdapter[] Adapters,
        DiscoveredDevice[] Devices,
        int CandidateCount,
        TimeSpan Elapsed);

    private sealed record DiscoveredDevice(
        IPAddress Address,
        string HostName,
        string MacAddress,
        long? RoundtripMilliseconds,
        string[] Services,
        bool IsGateway,
        string[] AdapterNames,
        string Classification);

    private sealed record ResolvedTarget(IPAddress Address, string HostName);

    private sealed record PortProbe(int Port, string Name);
}
