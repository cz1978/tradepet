using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using TradePet.Core.Domain;
using TradePet.Core.Protocol;
using TradePet.Infrastructure.Mt5;

namespace TradePet.Infrastructure.Mt4;

public sealed record Mt4Frame(string InstanceId, long Sequence, DateTimeOffset CapturedAtUtc,
    bool Connected, string AccountKey, JsonElement Payload);

public sealed class Mt4FileClient(string terminalPath, string dataDirectory) : ITradingWorkerClient
{
    public const string SnapshotFileName = "TradePet\\snapshot.json";
    public const string HistoryFileName = "TradePet\\history.json";
    private int _forceHistoryRefresh;
    private long _commandSequence;
    private readonly Channel<ProtocolEnvelope> _events = Channel.CreateBounded<ProtocolEnvelope>(32);
    private readonly string _source = $"mt4-{Guid.NewGuid():N}";
    private long _sequence;
    public ChannelReader<ProtocolEnvelope> Events => _events.Reader;

    public static Mt4Frame ReadFrame(string json, string expectedTerminalPath, DateTimeOffset now)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Empty MT4 snapshot.");
        var version = root["version"]?.GetValue<int>();
        if (root["platform"]?.GetValue<string>() != "mt4" || version is not (1 or 2))
            throw new InvalidDataException("Unsupported MT4 snapshot format.");
        var installation = root["terminalPath"]?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(Mt5TerminalDiscovery.GetInstallationDirectory(installation),
                Mt5TerminalDiscovery.GetInstallationDirectory(expectedTerminalPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MT4 snapshot belongs to another terminal.");
        var captured = root["capturedAtUtc"]!.GetValue<DateTimeOffset>();
        if (now - captured > TimeSpan.FromSeconds(10) || captured - now > TimeSpan.FromSeconds(5))
            throw new InvalidDataException("MT4 snapshot has expired.");
        var instance = root["sourceInstanceId"]!.GetValue<string>();
        var sequence = root["sequence"]!.GetValue<long>();
        if (string.IsNullOrWhiteSpace(instance) || sequence < 1) throw new InvalidDataException("Invalid MT4 sequence.");
        var connected = root["connected"]!.GetValue<bool>();
        var account = root["account"]!.AsObject();
        var server = account["server"]!.GetValue<string>();
        var login = account["login"]!.GetValue<long>();
        if (connected && (string.IsNullOrWhiteSpace(server) || login <= 0)) throw new InvalidDataException("Invalid MT4 account.");
        // Keep MT4 records isolated even when the same server name and login also exist in MT5.
        account["server"] = "MT4:" + server.Trim();
        account["marginMode"] = -1; // MT4 capability is explicit; it is not an MT5 hedging account.
        root["historyAvailable"] = version == 2 && root["serverUtcOffsetSeconds"] is not null;
        if (version == 2 && root["positions"] is JsonArray positions)
        {
            foreach (var position in positions)
            {
                var entry = position?["entryPrice"]?.GetValue<decimal>() ?? 0;
                var stop = position?["stopLoss"]?.GetValue<decimal>() ?? 0;
                var volume = position?["volume"]?.GetValue<decimal>() ?? 0;
                var tickSize = position?["tickSize"]?.GetValue<decimal>() ?? 0;
                var tickValue = position?["tickValue"]?.GetValue<decimal>() ?? 0;
                if (position is not null)
                    position["initialRiskAmount"] = entry > 0 && stop > 0 && volume > 0 && tickSize > 0 && tickValue > 0
                        ? JsonValue.Create(Math.Abs(entry - stop) / tickSize * tickValue * volume) : null;
            }
        }
        var payload = JsonSerializer.SerializeToElement(root, ProtocolJson.Options);
        var accountKey = $"MT4:{server.Trim()}|{login.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        if (connected)
        {
            var batch = Mt5PayloadMapper.MapSnapshot(ProtocolEnvelope.Create(instance, sequence, "snapshot", payload, accountKey));
            if (batch.ServerUtcOffsetSeconds is < -50400 or > 50400 ||
                batch.Positions.Select(item => item.Ticket).Distinct().Count() != batch.Positions.Count ||
                batch.Orders.Select(item => item.Ticket).Distinct().Count() != batch.Orders.Count)
                throw new InvalidDataException("Inconsistent MT4 snapshot.");
            if (version == 2 && payload.TryGetProperty("charts", out var charts))
            {
                var chartEnvelope = ProtocolEnvelope.Create(instance, sequence, "chart_snapshot", charts, accountKey);
                var chartSnapshot = BridgePayloadMapper.MapChartSnapshot(chartEnvelope);
                var terminalId = Mt5TerminalDiscovery.CreateTerminalId(expectedTerminalPath);
                if (chartSnapshot.TerminalId != terminalId || chartSnapshot.Objects.Any(item => item.TerminalId != terminalId) ||
                    chartSnapshot.Objects.Select(item => item.ObjectKey).Distinct().Count() != chartSnapshot.Objects.Count)
                    throw new InvalidDataException("MT4 chart snapshot belongs to another terminal or contains duplicates.");
            }
        }
        return new Mt4Frame(instance, sequence, captured, connected, accountKey, payload);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string? instance = null;
        long lastSequence = -1;
        string? connectedAccount = null;
        string? lastCharts = null;
        string? lastHistoryStatus = null;
        var knownDeals = new Dictionary<long, DealRecord>();
        var knownCashFlows = new Dictionary<long, AccountCashFlow>();
        var historyReceived = false;
        int? historyOffset = null;
        var nextHistoryRead = DateTimeOffset.MinValue;
        var nextFullHistory = DateTimeOffset.MinValue;
        var path = Path.Combine(dataDirectory, "MQL4", "Files", SnapshotFileName);
        while (!cancellationToken.IsCancellationRequested)
        {
            Mt4Frame? frame = null;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("MT4 snapshot exceeds size limit.");
                using var reader = new StreamReader(stream);
                frame = ReadFrame(await reader.ReadToEndAsync(cancellationToken), terminalPath, DateTimeOffset.UtcNow);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                or InvalidOperationException or ArgumentException or FormatException or NullReferenceException or KeyNotFoundException or OverflowException)
            {
                // Incomplete writes, removed EAs and stale files must never masquerade as live data.
            }

            if (frame is not { Connected: true })
            {
                if (connectedAccount is not null)
                    await EmitAsync("connection", new { connected = false }, connectedAccount, cancellationToken);
                connectedAccount = null;
            }
            else if (frame.InstanceId != instance || frame.Sequence > lastSequence)
            {
                if (connectedAccount != frame.AccountKey || instance != frame.InstanceId)
                {
                    await EmitAsync("connection", new { connected = true }, frame.AccountKey, cancellationToken);
                    knownDeals.Clear();
                    knownCashFlows.Clear();
                    historyReceived = false;
                    nextHistoryRead = DateTimeOffset.MinValue;
                    lastCharts = null;
                    lastHistoryStatus = null;
                }
                connectedAccount = frame.AccountKey;
                instance = frame.InstanceId;
                lastSequence = frame.Sequence;
                await EmitAsync("snapshot", frame.Payload, frame.AccountKey, cancellationToken, frame.CapturedAtUtc);
                if (frame.Payload.TryGetProperty("charts", out var charts) && charts.GetRawText() != lastCharts)
                {
                    await EmitAsync("chart_snapshot", charts, frame.AccountKey, cancellationToken, frame.CapturedAtUtc);
                    lastCharts = charts.GetRawText();
                }
                var force = Interlocked.Exchange(ref _forceHistoryRefresh, 0) != 0;
                if (frame.Payload.GetProperty("historyAvailable").GetBoolean() && (force || DateTimeOffset.UtcNow >= nextHistoryRead))
                {
                    var now = DateTimeOffset.UtcNow;
                    nextHistoryRead = now.AddSeconds(5);
                    string status;
                    var ready = false;
                    try
                    {
                        using var historyStream = new FileStream(Path.Combine(dataDirectory, "MQL4", "Files", HistoryFileName),
                            FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (historyStream.Length > 64 * 1024 * 1024) throw new InvalidDataException("MT4 history exceeds size limit.");
                        using var historyReader = new StreamReader(historyStream);
                        var batch = Mt4HistoryMapper.Read(await historyReader.ReadToEndAsync(cancellationToken), frame, terminalPath, now);
                        var offset = frame.Payload.GetProperty("serverUtcOffsetSeconds").GetInt32();
                        var recovery = !historyReceived || force || offset != historyOffset || now >= nextFullHistory;
                        var deals = batch.Deals.Where(item => recovery || !knownDeals.TryGetValue(item.Ticket, out var old) || old != item).ToArray();
                        var cashFlows = batch.CashFlows.Where(item => recovery || !knownCashFlows.TryGetValue(item.Ticket, out var old) || old != item).ToArray();
                        if (recovery || deals.Length > 0 || cashFlows.Length > 0)
                            await EmitAsync("deals", new { deals, cashFlows, symbolSpecifications = batch.SymbolSpecifications, isRecovery = recovery }, frame.AccountKey, cancellationToken);
                        foreach (var deal in batch.Deals) knownDeals[deal.Ticket] = deal;
                        foreach (var cashFlow in batch.CashFlows) knownCashFlows[cashFlow.Ticket] = cashFlow;
                        if (recovery) nextFullHistory = now.AddMinutes(1);
                        historyReceived = true;
                        ready = true;
                        historyOffset = offset;
                        status = $"MT4 · 已读取 {batch.Deals.Count(item => item.EntryKind == DealEntryKind.Out)} 条已平仓订单；历史范围以终端设置为准";
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                        or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException)
                    {
                        status = "MT4 历史尚未就绪或已过期；请更新桥接插件，并在终端账户历史中选择全部历史";
                    }
                    if (status != lastHistoryStatus)
                    {
                        await EmitAsync("history_status", new { message = status, ready }, frame.AccountKey, cancellationToken);
                        lastHistoryStatus = status;
                    }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private ValueTask EmitAsync<T>(string kind, T payload, string account, CancellationToken token, DateTimeOffset? captured = null) =>
        _events.Writer.WriteAsync(ProtocolEnvelope.Create(_source, ++_sequence, kind, payload, account,
            occurredAtUtc: captured), token);

    public Task RefreshAsync(int serverUtcOffsetSeconds, DateOnly serverDate, CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _forceHistoryRefresh, 1);
        return Task.CompletedTask;
    }
    public Task RefreshHistoryAsync(IReadOnlyCollection<int> historyYears, int serverUtcOffsetSeconds,
        DateOnly serverDate, CancellationToken cancellationToken = default) => RefreshAsync(serverUtcOffsetSeconds, serverDate, cancellationToken);

    public async Task SetLossZonesAsync(string accountKey, DateOnly date, IReadOnlyCollection<BridgeLossZone> zones,
        CancellationToken cancellationToken)
    {
        var revision = Interlocked.Increment(ref _commandSequence);
        var envelope = ProtocolEnvelope.Create(_source, revision, "loss_zone_snapshot",
            new LossZoneChartSnapshot(revision, Mt5TerminalDiscovery.GetInstallationDirectory(terminalPath), zones.ToArray()), accountKey, date);
        var path = Path.Combine(dataDirectory, "MQL4", "Files", "TradePet", "loss-zones.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(envelope, ProtocolJson.Options), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }
    public ValueTask DisposeAsync() { _events.Writer.TryComplete(); return ValueTask.CompletedTask; }
}
