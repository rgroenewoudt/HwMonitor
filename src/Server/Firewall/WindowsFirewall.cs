using System.Net;
using System.Reflection;
using HwMonitor.Sensors;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace HwMonitor.Firewall;

/// <summary>What the UI on the PC itself needs to show the firewall warning.</summary>
/// <param name="BlockedPorts">LAN ports that other devices cannot reach. Empty = ok or not known.</param>
/// <param name="Profiles">Active network profiles that block them (Domain, Private, Public).</param>
/// <param name="CanOpen">Running as admin, so <see cref="WindowsFirewall.Open"/> can add the rule.</param>
public sealed record FirewallStatus(int[] BlockedPorts, string[] Profiles, bool CanOpen)
{
    public static readonly FirewallStatus Ok = new([], [], false);
}

/// <summary>
/// Checks with the Windows Firewall COM API (HNetCfg.FwPolicy2) whether the LAN port of this app is blocked,
/// and adds an allow rule for it. Other firewalls are not seen.
/// </summary>
public sealed class WindowsFirewall(IServer server, ILogger<WindowsFirewall> logger)
{
    private const int Inbound = 1, Allow = 1, Block = 0, Tcp = 6, AnyProtocol = 256;
    private const int AllProfiles = 0x7fffffff;
    private static readonly (int Bit, string Name)[] ProfileNames = [(1, "Domain"), (2, "Private"), (4, "Public")];

    private readonly Lock _lock = new();
    private bool _loggedError;

    public FirewallStatus Check()
    {
        var ports = LanPorts();
        if (ports.Length == 0)
            return FirewallStatus.Ok;
        try
        {
            lock (_lock)
                return CheckPorts(Policy(), ports);
        }
        catch (Exception ex)
        {
            if (!_loggedError)
                logger.LogWarning(ex, "Cannot read the Windows Firewall rules; no firewall warning is shown");
            _loggedError = true;
            return FirewallStatus.Ok;
        }
    }

    /// <summary>
    /// Adds an inbound allow rule for each blocked port and turns off block rules for this exe
    /// (made when someone pressed Cancel in the Windows "allow access" prompt; a block rule wins over an allow rule).
    /// Returns the status checked again afterwards.
    /// </summary>
    public FirewallStatus Open()
    {
        lock (_lock)
        {
            var policy = Policy();
            var before = CheckPorts(policy, LanPorts());
            if (before.BlockedPorts.Length == 0)
                return before;
            if (!before.CanOpen)
                throw new UnauthorizedAccessException("Run HwMonitor as administrator to change the firewall.");

            int profiles = policy.CurrentProfileTypes;
            dynamic rules = policy.Rules;
            var names = new HashSet<string>();
            foreach (dynamic rule in rules)
            {
                names.Add((string)rule.Name);
                if (rule.Direction == Inbound && rule.Action == Block && rule.Enabled && IsThisExe((string?)rule.ApplicationName))
                {
                    logger.LogInformation("Turning off firewall block rule {Name} for this exe", (string)rule.Name);
                    rule.Enabled = false;
                }
            }
            foreach (var port in before.BlockedPorts)
            {
                var name = $"HwMonitor (TCP {port})";
                if (names.Contains(name))
                    rules.Remove(name); // an old rule (other profiles); avoids two rules with the same name
                dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
                rule.Name = name;
                rule.Description = "Lets phones and other PCs on the local network open the HwMonitor dashboard.";
                rule.Direction = Inbound;
                rule.Action = Allow;
                rule.Protocol = Tcp;
                rule.LocalPorts = port.ToString();
                rule.RemoteAddresses = "LocalSubnet";
                rule.Profiles = profiles;
                rule.Enabled = true;
                rules.Add(rule);
                logger.LogInformation("Added firewall rule {Name}", name);
            }
            return CheckPorts(policy, before.BlockedPorts);
        }
    }

    private static dynamic Policy() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;

    private static FirewallStatus CheckPorts(dynamic policy, int[] ports)
    {
        int current = policy.CurrentProfileTypes;
        var active = ProfileNames
            .Where(p => (current & p.Bit) != 0 && (bool)GetIndexed(policy, "FirewallEnabled", p.Bit))
            .ToArray();
        if (active.Length == 0 || ports.Length == 0)
            return FirewallStatus.Ok;

        var rules = new List<(int Action, int Profiles, string Ports)>();
        foreach (dynamic rule in policy.Rules)
        {
            if (rule.Direction != Inbound || !rule.Enabled || rule.Protocol is not (Tcp or AnyProtocol))
                continue;
            // Rules for another program, a service, a store app (they have an owner user), some users or an interface
            // do not apply to this exe.
            string? app = rule.ApplicationName, service = rule.ServiceName, package = rule.LocalAppPackageId,
                owner = rule.LocalUserOwner, users = rule.LocalUserAuthorizedList;
            if ((!string.IsNullOrEmpty(app) && !IsThisExe(app)) || service is { Length: > 0 } and not "*"
                || !string.IsNullOrEmpty(package) || !string.IsNullOrEmpty(owner) || !string.IsNullOrEmpty(users)
                || rule.Interfaces is not null)
                continue;
            // Only rules for all local addresses (VPNs like Tailscale allow everything on their own address).
            // A block rule only for some remote addresses still lets the LAN in.
            if (rule.LocalAddresses != "*" || (rule.Action == Block && rule.RemoteAddresses != "*"))
                continue;
            // No ports (protocol "any") = all ports.
            string? localPorts = rule.LocalPorts;
            rules.Add((rule.Action, rule.Profiles, string.IsNullOrEmpty(localPorts) ? "*" : localPorts));
        }

        var blockedPorts = new HashSet<int>();
        var blockedProfiles = new List<string>();
        foreach (var (bit, name) in active)
        {
            var blockAll = (bool)GetIndexed(policy, "BlockAllInboundTraffic", bit);
            var defaultAllow = (int)GetIndexed(policy, "DefaultInboundAction", bit) == Allow;
            var inProfile = rules.Where(r => r.Profiles == AllProfiles || (r.Profiles & bit) != 0).ToList();
            var blockedHere = ports.Where(port =>
            {
                var matching = inProfile.Where(r => PortMatches(r.Ports, port)).ToList();
                if (blockAll || matching.Any(r => r.Action == Block))
                    return true;
                return !defaultAllow && !matching.Any(r => r.Action == Allow);
            }).ToList();
            if (blockedHere.Count > 0)
            {
                blockedPorts.UnionWith(blockedHere);
                blockedProfiles.Add(name);
            }
        }
        return blockedPorts.Count == 0
            ? FirewallStatus.Ok
            : new FirewallStatus([.. blockedPorts.Order()], [.. blockedProfiles], SensorRules.IsAdmin());
    }

    /// <summary>INetFwPolicy2 has properties with a profile parameter, which C# dynamic cannot call.</summary>
    private static object GetIndexed(object policy, string name, int profile) =>
        policy.GetType().InvokeMember(name, BindingFlags.GetProperty, null, policy, [profile])!;

    /// <summary>LocalPorts is "*", or a list like "80,443,5000-5100" (or keywords like "RPC", which never match).</summary>
    private static bool PortMatches(string ports, int port) =>
        ports == "*" || ports.Split(',', StringSplitOptions.TrimEntries).Any(part =>
        {
            var range = part.Split('-');
            return range.Length switch
            {
                1 => int.TryParse(range[0], out var single) && single == port,
                2 => int.TryParse(range[0], out var from) && int.TryParse(range[1], out var to) && port >= from && port <= to,
                _ => false,
            };
        });

    private static bool IsThisExe(string? path) =>
        !string.IsNullOrEmpty(path) && Environment.ProcessPath is { } exe
        && string.Equals(Environment.ExpandEnvironmentVariables(path), exe, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ports Kestrel listens on for other devices; only loopback (like the tests' localhost) = nothing to check.</summary>
    private int[] LanPorts() =>
        server.Features.Get<IServerAddressesFeature>()?.Addresses
            .Select(BindingAddress.Parse)
            .Where(a => a.Host != "localhost" && !(IPAddress.TryParse(a.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)))
            .Select(a => a.Port)
            .Distinct()
            .ToArray() ?? [];
}
