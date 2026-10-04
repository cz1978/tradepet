using System.Collections.ObjectModel;
using System.Windows;
using TradePet.Core.Domain;

namespace TradePet.App.Views;

public partial class MacroCalendarWindow : Window
{
    private TimeSpan _serverOffset;
    private readonly bool _weeklyCalendar;

    public MacroCalendarWindow(IReadOnlyList<EconomicCalendarEvent> events, int serverUtcOffsetSeconds, bool weeklyCalendar = false)
    {
        InitializeComponent();
        _weeklyCalendar = weeklyCalendar;
        if (weeklyCalendar)
        {
            SourceText.Text = TradePet.Core.Localization.UiText.Translate("Forex Factory 公开周历 · 时间按当前服务器偏移显示 · 不提供实时公布值");
            ReminderText.Text = TradePet.Core.Localization.UiText.Translate("高重要度事件在 30 分钟和 5 分钟前提醒；数值缺失显示 —，周历约每 15 分钟更新。");
        }
        EventList.ItemsSource = Rows;
        ApplyEvents(events, serverUtcOffsetSeconds);
    }

    public ObservableCollection<MacroCalendarRow> Rows { get; } = [];

    public void SetLoadStatus(string status) => SummaryText.Text = TradePet.Core.Localization.UiText.Translate(status);

    public void ApplyEvents(IReadOnlyList<EconomicCalendarEvent> events, int serverUtcOffsetSeconds)
    {
        _serverOffset = TimeSpan.FromSeconds(serverUtcOffsetSeconds);
        Rows.Clear();
        foreach (var item in events.OrderBy(item => item.ScheduledAtUtc).ThenByDescending(item => item.Importance))
        {
            Rows.Add(ToRow(item));
        }
        var highCount = events.Count(item => item.Importance == EconomicEventImportance.High);
        SummaryText.Text = TradePet.Core.Localization.UiText.Translate(events.Count == 0 ? "等待日历数据" : $"{events.Count} 项 · 高重要度 {highCount} 项");
    }

    private MacroCalendarRow ToRow(EconomicCalendarEvent item)
    {
        var serverTime = item.ScheduledAtUtc.ToOffset(_serverOffset);
        var now = DateTimeOffset.UtcNow;
        var status = item.ActualValue is not null
            ? "已公布"
            : item.ScheduledAtUtc <= now
                ? _weeklyCalendar ? "周历未提供公布值" : "等待公布值"
                : $"还有 {FormatRemaining(item.ScheduledAtUtc - now)}";
        return new MacroCalendarRow(
            serverTime.ToString("MM-dd HH:mm"),
            string.Join(" · ", new[] { item.CountryCode, item.Currency }.Where(value => !string.IsNullOrWhiteSpace(value))),
            item.Name,
            item.Importance switch
            {
                EconomicEventImportance.High => "★★★ 高",
                EconomicEventImportance.Moderate => "★★ 中",
                EconomicEventImportance.Low => "★ 低",
                _ => "未评级",
            },
            item.Importance == EconomicEventImportance.High ? "#F06B78" : "#E7B84B",
            status,
            item.ActualValue is not null ? "#65D9BE" : "#91A3B5",
            FormatValue(item.RevisedPreviousValue ?? item.PreviousValue, item),
            FormatValue(item.ForecastValue, item),
            FormatValue(item.ActualValue, item));
    }

    private static string FormatRemaining(TimeSpan remaining) => remaining.TotalHours >= 1
        ? $"{(int)remaining.TotalHours} 小时 {remaining.Minutes} 分"
        : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} 分钟";

    private static string FormatValue(decimal? value, EconomicCalendarEvent item)
    {
        if (value is null)
        {
            return "—";
        }
        var digits = Math.Clamp(item.Digits, 0, 6);
        var number = value.Value.ToString(digits == 0 ? "0" : $"0.{new string('#', digits)}");
        return item.Unit.EndsWith("PERCENT", StringComparison.OrdinalIgnoreCase) ? number + "%" : number;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record MacroCalendarRow(
    string TimeText,
    string CountryText,
    string Name,
    string ImportanceText,
    string ImportanceColor,
    string StatusText,
    string StatusColor,
    string PreviousText,
    string ForecastText,
    string ActualText);
