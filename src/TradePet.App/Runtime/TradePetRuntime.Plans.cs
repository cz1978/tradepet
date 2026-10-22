using System.Globalization;
using TradePet.Core.Domain;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private async Task ComposeChartPlanCoreAsync()
    {
        var symbol = _viewModel.NewPlanSymbol.Trim();
        var items = _planItems.Values.Where(item => item.IsActive && item.ServerDate == _serverDate &&
            item.PriceLow is not null && item.PriceHigh is not null &&
            (symbol.Length == 0 || item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))).ToArray();
        var entries = items.Where(item => item.Category is PlanCategory.EntryWatch or
            PlanCategory.LongWatchZone or PlanCategory.ShortWatchZone).ToArray();
        if (entries.Length != 1)
        {
            await OnUiAsync(() => _viewModel.StructuredPlanStatusText =
                "请给一个入场对象分类为入场观察、做多观察区或做空观察区；有多个品种时先填写品种。每次组合只保留一个有效入场对象。");
            return;
        }
        var entry = entries[0];
        var stops = items.Where(item => item.Symbol.Equals(entry.Symbol, StringComparison.OrdinalIgnoreCase) && item.Category == PlanCategory.StopReference).ToArray();
        var targets = items.Where(item => item.Symbol.Equals(entry.Symbol, StringComparison.OrdinalIgnoreCase) && item.Category == PlanCategory.TargetReference).ToArray();
        if (stops.Length != 1 || targets.Length != 1)
        {
            await OnUiAsync(() => _viewModel.StructuredPlanStatusText = "同品种需要各一个有效的止损参考和目标参考对象；多余对象可改为仅作标记。");
            return;
        }
        var reference = (entry.PriceLow!.Value + entry.PriceHigh!.Value) / 2m;
        var stop = (stops[0].PriceLow!.Value + stops[0].PriceHigh!.Value) / 2m;
        var target = (targets[0].PriceLow!.Value + targets[0].PriceHigh!.Value) / 2m;
        var side = entry.Category == PlanCategory.LongWatchZone ? TradeSide.Buy :
            entry.Category == PlanCategory.ShortWatchZone ? TradeSide.Sell : stop < reference ? TradeSide.Buy : TradeSide.Sell;
        await OnUiAsync(() =>
        {
            _viewModel.NewPlanSymbol = entry.Symbol;
            _viewModel.NewPlanSide = side;
            _viewModel.NewPlanReferenceEntry = reference.ToString(CultureInfo.CurrentCulture);
            _viewModel.NewPlanEntryLow = entry.PriceLow.Value.ToString(CultureInfo.CurrentCulture);
            _viewModel.NewPlanEntryHigh = entry.PriceHigh.Value.ToString(CultureInfo.CurrentCulture);
            _viewModel.NewPlanStop = stop.ToString(CultureInfo.CurrentCulture);
            _viewModel.NewPlanTarget = target.ToString(CultureInfo.CurrentCulture);
            _viewModel.NewPlanNotes = string.Join("\n", new[] { entry, stops[0], targets[0] }.Select(item => $"{item.ObjectKey} · {item.Text}"));
            _viewModel.StructuredPlanStatusText = "图表对象已组合为计划草稿；请检查方向、入场、止损和目标，再保存。保存后冻结本次价格，不随图线移动改写历史。";
        });
    }
}
