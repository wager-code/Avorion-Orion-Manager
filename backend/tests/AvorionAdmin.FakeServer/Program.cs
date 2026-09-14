using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

var values = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 0; index + 1 < args.Length; index += 2)
{
    if (!args[index].StartsWith("--", StringComparison.Ordinal)) continue;
    values[args[index]] = args[index + 1];
}

if (!values.TryGetValue("--datapath", out var dataPath) ||
    !values.TryGetValue("--galaxy-name", out var galaxyName))
    return 2;

var galaxyDirectory = Path.Combine(dataPath, galaxyName);
Directory.CreateDirectory(galaxyDirectory);
var serverIniPath = Path.Combine(galaxyDirectory, "server.ini");
if (!File.Exists(serverIniPath))
{
    var port = values.GetValueOrDefault("--port", "27000");
    var maxPlayers = values.GetValueOrDefault("--max-players", "10");
    var serverName = values.GetValueOrDefault("--server-name", "Fixture Server");
    await File.WriteAllTextAsync(serverIniPath,
        $"[Networking]\r\nport={port}\r\nrconIp=\r\nrconPassword=\r\nrconPort=27015\r\n" +
        $"[Administration]\r\nmaxPlayers={maxPlayers}\r\nname={serverName}\r\n",
        new UTF8Encoding(false));
}

var configuration = ParseIni(await File.ReadAllLinesAsync(serverIniPath));
using var stop = new CancellationTokenSource();
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var serverLogPath = Path.Combine(galaxyDirectory, $"serverlog {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt");
await File.WriteAllTextAsync(serverLogPath, "Fixture server starting.\r\n", new UTF8Encoding(false));
var rconTask = StartRconAsync(configuration, galaxyDirectory, stop);
_ = Task.Run(async () =>
{
    await Task.Delay(750, stop.Token);
    await File.AppendAllTextAsync(serverLogPath, "Server startup complete.\r\n", new UTF8Encoding(false), stop.Token);
    ready.TrySetResult();
}, stop.Token);
_ = ReadConsoleAsync(galaxyDirectory, stop, ready.Task);

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token);
}
catch (OperationCanceledException) { }
try { await rconTask; } catch (OperationCanceledException) { }
return 0;

static async Task ReadConsoleAsync(string galaxyDirectory, CancellationTokenSource stop, Task ready)
{
    while (!stop.IsCancellationRequested && await Console.In.ReadLineAsync() is { } command)
    {
        // Real Avorion does not accept console commands until its input thread is ready.
        if (!ready.IsCompleted) continue;
        if (command.Equals("/save", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(Path.Combine(galaxyDirectory, "save-confirmed.marker"), DateTimeOffset.UtcNow.ToString("O"));
        }
        else if (command.Equals("/stop", StringComparison.Ordinal))
        {
            stop.Cancel();
            return;
        }
    }
}

static Dictionary<string, string> ParseIni(IEnumerable<string> lines)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in lines)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0) continue;
        values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
    }
    return values;
}

static async Task StartRconAsync(
    IReadOnlyDictionary<string, string> configuration,
    string galaxyDirectory,
    CancellationTokenSource stop)
{
    if (!configuration.TryGetValue("rconPassword", out var password) || string.IsNullOrEmpty(password) ||
        !configuration.TryGetValue("rconPort", out var portText) || !int.TryParse(portText, out var port))
        return;

    var cancellationToken = stop.Token;
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            (int Id, int Type, string Body) request;
            try { request = await ReadPacketAsync(stream, cancellationToken); }
            catch (EndOfStreamException) { continue; }
            var responseId = request.Type == 3 && request.Body == password ? request.Id : -1;
            await WritePacketAsync(stream, responseId, 2, string.Empty, cancellationToken);
            if (responseId == -1) continue;

            (int Id, int Type, string Body) command;
            try { command = await ReadPacketAsync(stream, cancellationToken); }
            catch (EndOfStreamException) { continue; }
            if (command.Type != 2) continue;
            if (command.Body.Equals("/save", StringComparison.Ordinal))
                await File.WriteAllTextAsync(Path.Combine(galaxyDirectory, "save-confirmed.marker"), DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
            await WritePacketAsync(stream, command.Id, 0, string.Empty, cancellationToken);
            if (command.Body.Equals("/stop", StringComparison.Ordinal))
            {
                stop.Cancel();
                Environment.Exit(0);
            }
        }
    }
    finally
    {
        listener.Stop();
    }
}

static async Task<(int Id, int Type, string Body)> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
{
    var lengthBytes = new byte[4];
    await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
    var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
    if (length is < 10 or > 64 * 1024) throw new InvalidDataException("invalid RCON packet length");
    var packet = new byte[length];
    await stream.ReadExactlyAsync(packet, cancellationToken);
    return (
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(0, 4)),
        BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(4, 4)),
        Encoding.UTF8.GetString(packet, 8, length - 10));
}

static async Task WritePacketAsync(Stream stream, int id, int type, string body, CancellationToken cancellationToken)
{
    var bodyBytes = Encoding.UTF8.GetBytes(body);
    var length = 10 + bodyBytes.Length;
    var packet = new byte[4 + length];
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), length);
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), id);
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), type);
    bodyBytes.CopyTo(packet.AsSpan(12));
    await stream.WriteAsync(packet, cancellationToken);
    await stream.FlushAsync(cancellationToken);
}
