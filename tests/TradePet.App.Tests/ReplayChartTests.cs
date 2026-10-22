using System.Windows;
using System.Windows.Media.Imaging;
using TradePet.App.Controls;
using TradePet.Core.Domain;
using Xunit;

namespace TradePet.App.Tests;

[Collection("Desktop window tests")]
public sealed class ReplayChartTests
{
    [Fact]
    public async Task PriceChart_RendersCandlesExecutionsAndProtectionWithoutOpeningAWindow()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var bar = new MarketBar("t", "a", "TEST", "M5", now.AddMinutes(-5), 100m, 105m, 98m, 103m, 1, 0, 1);
                var deal = new DealRecord(1, 1, 1, "TEST", TradeSide.Buy, DealEntryKind.In, 1m, 100m, 0m, 0m, 0m, 0m, now.AddMinutes(-4));
                var sample = new PositionPnlSample(new("a", 1), now, 0m, 3m, 3m, 1m, 98m, 105m, 0, "test");
                var chart = new TradeReplayChart { Width = 640, Height = 280, ServerOffset = 7200,
                    Frame = new(now, [bar], [], [deal], [sample], [], [], MarketCoverageStatus.Partial, "") };
                chart.Measure(new Size(640, 280)); chart.Arrange(new Rect(0, 0, 640, 280));
                var bitmap = new RenderTargetBitmap(640, 280, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(chart);
                var pixels = new int[640 * 280]; bitmap.CopyPixels(pixels, 640 * 4, 0);
                Assert.True(pixels.Distinct().Count() > 10);
                completion.TrySetResult();
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
