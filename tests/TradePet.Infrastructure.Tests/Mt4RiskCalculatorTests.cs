using System.Text.Json.Nodes;
using TradePet.Infrastructure.Mt4;
using Xunit;

namespace TradePet.Infrastructure.Tests;

public sealed class Mt4RiskCalculatorTests
{
    [Theory]
    [InlineData(0, "EUR", "USD", 1.1, 1.09, 100000, 0, 1000)]
    [InlineData(0, "USD", "JPY", 150, 125, 100000, 0, 20000)]
    [InlineData(0, "EUR", "GBP", 1.1, 1.09, 100000, 1.25, 1250)]
    [InlineData(1, "XAU", "USD", 2500, 2490, 100, 0, 1000)]
    public void ContractRisk_UsesProfitCurrencyAndStopPriceInsteadOfCurrentTickValue(int mode, string basis,
        string profit, decimal entry, decimal stop, decimal contract, decimal rate, decimal expected)
    {
        var position = new JsonObject { ["entryPrice"] = entry, ["stopLoss"] = stop, ["volume"] = 1m,
            ["calculationMode"] = mode, ["baseCurrency"] = basis, ["profitCurrency"] = profit,
            ["contractSize"] = contract, ["conversionRate"] = rate, ["tickValue"] = 999m };
        Assert.Equal(expected, Mt4RiskCalculator.Calculate(position, "USD"));
    }

    [Fact]
    public void MissingConversionOrStopIsUnknownAndFuturesUseTickValue()
    {
        var position = JsonNode.Parse("""{"entryPrice":100,"stopLoss":99,"volume":2,"calculationMode":0,"contractSize":100,"profitCurrency":"CHF","baseCurrency":"EUR","tickSize":0.1,"tickValue":5}""")!;
        Assert.Null(Mt4RiskCalculator.Calculate(position, "USD"));
        position["calculationMode"] = 2;
        Assert.Equal(100m, Mt4RiskCalculator.Calculate(position, "USD"));
        position["stopLoss"] = 0m;
        Assert.Null(Mt4RiskCalculator.Calculate(position, "USD"));
    }
}
