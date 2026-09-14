using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Agent;

public sealed partial class SteamQueryProbe(IServerRuntimePathResolver pathResolver) : ISteamQueryProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(4);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTimeOffset At, bool Running, SteamQuerySnapshot Value)? _cache;

    public async Task<SteamQuerySnapshot> ProbeAsync(bool serverRunning, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } cached && cached.Running == serverRunning && DateTimeOffset.UtcNow - cached.At < CacheDuration)
                return cached.Value;

            var result = await ProbeCoreAsync(serverRunning, cancellationToken);
            _cache = (DateTimeOffset.UtcNow, serverRunning, result);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SteamQuerySnapshot> ProbeCoreAsync(bool serverRunning, CancellationToken cancellationToken)
    {
        if (!serverRunning)
            return Empty(ConnectionState.Disconnected, "SERVER_NOT_RUNNING");

        var log = FindLatestServerLog();
        if (log is null)
            return Empty(ConnectionState.Unknown, "SERVER_PORT_LOG_UNAVAILABLE");

        var portEvidence = ReadPortEvidence(log);
        if (portEvidence.QueryPort is null)
            return new SteamQuerySnapshot(
                new ConnectionProbe(ConnectionState.Unknown, null, "STEAM_QUERY_PORT_UNKNOWN"),
                null, null, null, null, portEvidence.GamePort, log);

        bool listenerPresent;
        try
        {
            listenerPresent = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners()
                .Any(endpoint => endpoint.Port == portEvidence.QueryPort.Value);
        }
        catch (NetworkInformationException)
        {
            listenerPresent = false;
        }
        if (!listenerPresent)
            return new SteamQuerySnapshot(
                new ConnectionProbe(ConnectionState.Disconnected, null, "STEAM_QUERY_UDP_NOT_LISTENING"),
                null, null, null, portEvidence.QueryPort, portEvidence.GamePort, log);

        var timer = Stopwatch.StartNew();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Connect(IPAddress.Loopback, portEvidence.QueryPort.Value);
            var request = CreateInfoRequest();
            await udp.SendAsync(request, request.Length);
            var response = await udp.ReceiveAsync(cancellationToken).AsTask().WaitAsync(ProbeTimeout, cancellationToken);
            var payload = response.Buffer;
            if (IsChallenge(payload, out var challenge))
            {
                request = [.. request, .. challenge];
                await udp.SendAsync(request, request.Length);
                response = await udp.ReceiveAsync(cancellationToken).AsTask().WaitAsync(ProbeTimeout, cancellationToken);
                payload = response.Buffer;
            }

            var info = ParseInfo(payload);
            timer.Stop();
            return new SteamQuerySnapshot(
                new ConnectionProbe(ConnectionState.Connected, timer.Elapsed.TotalMilliseconds),
                info.Players, info.MaxPlayers, info.Name,
                portEvidence.QueryPort, portEvidence.GamePort, log);
        }
        catch (Exception exception) when (exception is TimeoutException or SocketException or InvalidDataException)
        {
            return new SteamQuerySnapshot(
                new ConnectionProbe(ConnectionState.Degraded, null,
                    exception is InvalidDataException ? "STEAM_QUERY_RESPONSE_INVALID" : "STEAM_QUERY_TIMEOUT"),
                null, null, null, portEvidence.QueryPort, portEvidence.GamePort, log);
        }
    }

    private string? FindLatestServerLog()
    {
        var source = pathResolver.ResolveLogSource();
        if (source is null) return null;
        if (File.Exists(source.Path)) return source.Path;
        if (!Directory.Exists(source.Path)) return null;
        try
        {
            return Directory.EnumerateFiles(source.Path, "serverlog*.txt", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (int? QueryPort, int? GamePort) ReadPortEvidence(string path)
    {
        try
        {
            var info = new FileInfo(path);
            const int maximum = 256 * 1024;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (info.Length > maximum) stream.Seek(-maximum, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
            var text = reader.ReadToEnd();
            return (LastPort(SteamQueryPortRegex(), text), LastPort(GamePortRegex(), text));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private static int? LastPort(Regex regex, string text)
    {
        var match = regex.Matches(text).Cast<Match>().LastOrDefault();
        return match is not null && int.TryParse(match.Groups[1].Value, out var port) && port is > 0 and <= 65535
            ? port
            : null;
    }

    private static byte[] CreateInfoRequest() =>
        [0xff, 0xff, 0xff, 0xff, 0x54, .. Encoding.ASCII.GetBytes("Source Engine Query\0")];

    private static bool IsChallenge(byte[] payload, out byte[] challenge)
    {
        challenge = [];
        if (payload.Length < 9 || payload[0] != 0xff || payload[1] != 0xff || payload[2] != 0xff || payload[3] != 0xff || payload[4] != 0x41)
            return false;
        challenge = payload[5..9];
        return true;
    }

    private static (string Name, int Players, int MaxPlayers) ParseInfo(byte[] payload)
    {
        if (payload.Length < 12 || payload[0] != 0xff || payload[1] != 0xff || payload[2] != 0xff || payload[3] != 0xff || payload[4] != 0x49)
            throw new InvalidDataException("unexpected A2S_INFO response");
        var offset = 6;
        var name = ReadNullTerminated(payload, ref offset);
        _ = ReadNullTerminated(payload, ref offset);
        _ = ReadNullTerminated(payload, ref offset);
        _ = ReadNullTerminated(payload, ref offset);
        if (offset + 4 > payload.Length) throw new InvalidDataException("truncated A2S_INFO response");
        offset += 2;
        return (name, payload[offset], payload[offset + 1]);
    }

    private static string ReadNullTerminated(byte[] payload, ref int offset)
    {
        var start = offset;
        while (offset < payload.Length && payload[offset] != 0) offset++;
        if (offset >= payload.Length) throw new InvalidDataException("unterminated A2S_INFO string");
        var value = Encoding.UTF8.GetString(payload, start, offset - start);
        offset++;
        return value;
    }

    private static SteamQuerySnapshot Empty(ConnectionState state, string reason) =>
        new(new ConnectionProbe(state, null, reason), null, null, null, null, null, null);

    [GeneratedRegex(@"Steam Query Port:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SteamQueryPortRegex();

    [GeneratedRegex(@"Game Port:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GamePortRegex();
}
