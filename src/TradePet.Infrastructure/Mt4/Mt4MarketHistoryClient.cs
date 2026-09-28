using System.Text.Json;
using TradePet.Application.Review;
using TradePet.Core.Domain;
using TradePet.Core.Protocol;
using TradePet.Infrastructure.Mt5;

namespace TradePet.Infrastructure.Mt4;

public sealed class Mt4MarketHistoryClient(string terminalPath, string dataDirectory, int offsetSeconds) : IMarketHistorySource
{
    public async Task<MarketHistoryResult> LoadAsync(MarketHistoryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TerminalId != Mt5TerminalDiscovery.CreateTerminalId(terminalPath) ||
            !request.ExpectedAccountKey.StartsWith("MT4:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(request.RequestId, "N", out _) || request.ToUtc <= request.FromUtc ||
            request.ToUtc - request.FromUtc > TimeSpan.FromDays(366) || request.Timeframe != "M5" ||
            offsetSeconds is < -50400 or > 50400)
            throw new InvalidOperationException("MT4 行情请求身份、服务器时间或范围无效（单次最多一年）。");
        if (request.Precision == MarketDataPrecision.Ticks)
            return await new Mt4TickArchive(dataDirectory).LoadAsync(request, offsetSeconds, cancellationToken);
        var directory = Path.Combine(dataDirectory, "MQL4", "Files", "TradePet");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"market-request-{request.RequestId}.json");
        var responsePath = Path.Combine(directory, $"market-response-{request.RequestId}.json");
        var command = JsonSerializer.Serialize(new
        {
            requestId = request.RequestId, terminalPath = Mt5TerminalDiscovery.GetInstallationDirectory(terminalPath),
            accountKey = request.ExpectedAccountKey, symbol = request.Symbol, timeframe = request.Timeframe,
            serverUtcOffsetSeconds = offsetSeconds, fromEpochUtc = request.FromUtc.ToUnixTimeSeconds(),
            toEpochUtc = request.ToUtc.ToUnixTimeSeconds(), maximumBars = Math.Clamp(request.MaximumBars, 1, 5000),
            createdAtEpochUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        }, ProtocolJson.Options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            await File.WriteAllTextAsync(path + ".tmp", command, timeout.Token);
            File.Move(path + ".tmp", path, overwrite: true);
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                try
                {
                    using var stream = new FileStream(responsePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("MT4 K 线响应过大。");
                    using var reader = new StreamReader(stream);
                    return Parse(request, await reader.ReadToEndAsync(timeout.Token), offsetSeconds);
                }
                catch (FileNotFoundException) { }
                await Task.Delay(250, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Empty(request, "MT4 K 线读取超时；请保持新版桥接插件运行，并在终端加载该品种历史。");
        }
        finally
        {
            foreach (var file in new[] { path, path + ".tmp", responsePath })
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static MarketHistoryResult Parse(MarketHistoryRequest request, string json, int offsetSeconds)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("requestId").GetString() != request.RequestId ||
            root.GetProperty("accountKey").GetString() != request.ExpectedAccountKey ||
            Mt5TerminalDiscovery.CreateTerminalId(root.GetProperty("terminalPath").GetString()!) != request.TerminalId ||
            root.GetProperty("symbol").GetString() != request.Symbol || root.GetProperty("timeframe").GetString() != request.Timeframe ||
            root.GetProperty("serverUtcOffsetSeconds").GetInt32() != offsetSeconds)
            throw new InvalidDataException("MT4 K 线响应身份或时区不匹配。");
        if (root.TryGetProperty("error", out var error) && !string.IsNullOrWhiteSpace(error.GetString()))
            return Empty(request, error.GetString()!);
        var bars = root.GetProperty("bars").EnumerateArray().Select(bar => new MarketBar(
            request.TerminalId, request.ExpectedAccountKey, request.Symbol, request.Timeframe,
            DateTimeOffset.FromUnixTimeSeconds(bar.GetProperty("time").GetInt64() - offsetSeconds),
            bar.GetProperty("open").GetDecimal(), bar.GetProperty("high").GetDecimal(), bar.GetProperty("low").GetDecimal(),
            bar.GetProperty("close").GetDecimal(), bar.GetProperty("tickVolume").GetInt64(), 0, 0)).ToArray();
        if (bars.Length > Math.Clamp(request.MaximumBars, 1, 5000) || bars.Select(b => b.OpenedAtUtc).Distinct().Count() != bars.Length ||
            bars.Any(b => b.OpenedAtUtc < request.FromUtc || b.OpenedAtUtc > request.ToUtc || b.Low <= 0 ||
                b.Low > Math.Min(b.Open, b.Close) || b.High < Math.Max(b.Open, b.Close) || b.TickVolume < 0))
            throw new InvalidDataException("MT4 K 线数据或范围无效。");
        bars = bars.OrderBy(b => b.OpenedAtUtc).ToArray();
        return new(new(request.RequestId, request.TerminalId, request.ExpectedAccountKey, request.Symbol, request.Timeframe,
            request.FromUtc, request.ToUtc, bars.FirstOrDefault()?.OpenedAtUtc, bars.LastOrDefault()?.OpenedAtUtc,
            MarketDataPrecision.Bars, bars.Length == 0 ? MarketCoverageStatus.Empty : MarketCoverageStatus.Partial,
            "mt4-bars-v1", "MT4 终端 K 线，按服务器时间显示；历史 UTC 按当前服务器偏移换算，缺口不补齐。Tick 回放使用本地实际报价存档。", DateTimeOffset.UtcNow), bars, []);
    }

    private static MarketHistoryResult Empty(MarketHistoryRequest request, string message) => new(
        new(request.RequestId, request.TerminalId, request.ExpectedAccountKey, request.Symbol, request.Timeframe,
            request.FromUtc, request.ToUtc, null, null, request.Precision, MarketCoverageStatus.Failed,
            "mt4-bars-v1", message, DateTimeOffset.UtcNow), [], []);
}
