using System.Text.Json.Nodes;

namespace TradePet.Infrastructure.Mt4;

public static class Mt4RiskCalculator
{
    public static decimal? Calculate(JsonNode position, string depositCurrency)
    {
        decimal Value(string key) => position[key]?.GetValue<decimal>() ?? 0;
        var entry = Value("entryPrice");
        var stop = Value("stopLoss");
        var volume = Value("volume");
        if (entry <= 0 || stop <= 0 || volume <= 0) return null;
        var distance = Math.Abs(entry - stop);
        if (position["calculationMode"] is not null)
        {
            var mode = position["calculationMode"]!.GetValue<int>();
            if (mode is 0 or 1)
            {
                var contract = Value("contractSize");
                if (contract <= 0) return null;
                var profitCurrency = position["profitCurrency"]?.GetValue<string>();
                var baseCurrency = position["baseCurrency"]?.GetValue<string>();
                if (profitCurrency == depositCurrency) return distance * volume * contract;
                if (mode == 0 && baseCurrency == depositCurrency) return distance * volume * contract / stop;
                var conversion = Value("conversionRate");
                return conversion > 0 ? distance * volume * contract * conversion : null;
            }
            if (mode != 2) return null;
        }
        // Futures use the broker's deposit-currency tick value. Also retain compatibility
        // with rc.4 bridges that do not export contract/currency metadata.
        var tickSize = Value("tickSize");
        var tickValue = Value("tickValue");
        return tickSize > 0 && tickValue > 0 ? distance / tickSize * tickValue * volume : null;
    }
}
