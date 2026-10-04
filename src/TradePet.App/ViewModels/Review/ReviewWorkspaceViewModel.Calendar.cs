using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using TradePet.Application.Review;
using TradePet.Core.Domain;

namespace TradePet.App.ViewModels.Review;

public sealed partial class ReviewWorkspaceViewModel
{
    private IReadOnlyList<string> _calendarMonths = [];
    private string? _selectedCalendarMonth;
    private string _calendarMonthSummary = "当前范围没有日历数据。";
    private IReadOnlyDictionary<DateOnly, ReviewCalendarCell> _calendarDays = new Dictionary<DateOnly, ReviewCalendarCell>();
    public IReadOnlyList<string> CalendarMonths { get => _calendarMonths; private set => SetProperty(ref _calendarMonths, value); }
    public string? SelectedCalendarMonth
    {
        get => _selectedCalendarMonth;
        set
        {
            if (!SetProperty(ref _selectedCalendarMonth, value)) return;
            UpdateCalendarMonth();
            RaisePropertyChanged(nameof(CanPreviousCalendarMonth));
            RaisePropertyChanged(nameof(CanNextCalendarMonth));
        }
    }
    public string CalendarMonthSummary { get => _calendarMonthSummary; private set => SetProperty(ref _calendarMonthSummary, value); }
    public IReadOnlyList<string> CalendarWeekdays { get; } = ["一", "二", "三", "四", "五", "六", "日"];
    public ObservableCollection<ReviewCalendarCell> CalendarMonthCells { get; } = [];
    public bool CanPreviousCalendarMonth => SelectedCalendarMonth is not null && CalendarMonths.ToList().IndexOf(SelectedCalendarMonth) > 0;
    public bool CanNextCalendarMonth => SelectedCalendarMonth is not null && CalendarMonths.ToList().IndexOf(SelectedCalendarMonth) < CalendarMonths.Count - 1;
    public ICommand PreviousCalendarMonthCommand => new RelayCommand(() => MoveCalendarMonth(-1));
    public ICommand NextCalendarMonthCommand => new RelayCommand(() => MoveCalendarMonth(1));

    private void MoveCalendarMonth(int difference)
    {
        var index = CalendarMonths.ToList().IndexOf(SelectedCalendarMonth ?? "") + difference;
        if (index >= 0 && index < CalendarMonths.Count) SelectedCalendarMonth = CalendarMonths[index];
    }

    private void ApplyCalendar(ReviewWorkspaceSnapshot snapshot, ReviewWorkspaceData data)
    {
        var days = new Dictionary<DateOnly, ReviewCalendarCell>();
        foreach (var day in snapshot.Calendar)
        {
            var journal = data.DailyJournals.GetValueOrDefault(day.ServerDate);
            var facts = snapshot.DailyFacts?.GetValueOrDefault(day.ServerDate);
            var journalStatus = FormatDailyJournalStatus(journal, facts?.SourceVersion);
            days[day.ServerDate] = new ReviewCalendarCell(day.ServerDate,
                Signed(day.RealizedCashPnl), $"平仓 {day.CompleteTradeCount}", day.PendingReviewCount > 0 ? $"待复盘 {day.PendingReviewCount}" : "",
                journal is null ? "" : journalStatus, day.HasDataGap ? "数据缺口" : "",
                FinancialPalette.For(day.RealizedCashPnl), FinancialPalette.BackgroundFor(day.RealizedCashPnl),
                $"{day.ServerDate:yyyy-MM-dd} · 现金盈亏 {Signed(day.RealizedCashPnl)} {data.Currency}\n开仓 {day.OpeningTradeCount} · 完整平仓 {day.CompleteTradeCount}\n{journalStatus}" + (day.HasDataGap ? "\n数据有缺口" : ""),
                new AsyncRelayCommand(() => OpenDailyOrApplyAsync(day.ServerDate, journal, facts, facts?.SourceVersion ?? string.Empty)), day.RealizedCashPnl);
        }
        _calendarDays = days;
        CalendarMonths = snapshot.Calendar.Select(day => day.ServerDate.ToString("yyyy-MM")).Distinct().Order().ToArray();
        if (SelectedCalendarMonth is null || !CalendarMonths.Contains(SelectedCalendarMonth))
            SelectedCalendarMonth = CalendarMonths.LastOrDefault();
        else UpdateCalendarMonth();
        RaisePropertyChanged(nameof(CanPreviousCalendarMonth));
        RaisePropertyChanged(nameof(CanNextCalendarMonth));
    }

    private void UpdateCalendarMonth()
    {
        CalendarMonthCells.Clear();
        if (!DateOnly.TryParseExact(SelectedCalendarMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
        {
            CalendarMonthSummary = "当前范围没有日历数据。";
            return;
        }
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var count = DateTime.DaysInMonth(first.Year, first.Month);
        var cellCount = (int)Math.Ceiling((offset + count) / 7d) * 7;
        for (var index = 0; index < cellCount; index++)
        {
            var date = first.AddDays(index - offset);
            CalendarMonthCells.Add(date.Month == first.Month && _calendarDays.TryGetValue(date, out var cell) ? cell :
                new ReviewCalendarCell(date.Month == first.Month ? date : null));
        }
        var monthDays = _calendarDays.Where(item => item.Key.Year == first.Year && item.Key.Month == first.Month).ToArray();
        var pnl = monthDays.Sum(item => item.Value.CashPnl);
        CalendarMonthSummary = $"账户现金盈亏 {Signed(pnl)} · {monthDays.Length} 个范围内日期 · 红盈绿亏；点击日期写日记。";
        UpdateCalendarSelection();
    }

    private void UpdateCalendarSelection()
    {
        DateOnly.TryParse(DailyDate, out var selected);
        foreach (var cell in CalendarMonthCells) cell.IsSelected = cell.Date == selected;
    }

    private void ClearCalendarDashboard()
    {
        _calendarDays = new Dictionary<DateOnly, ReviewCalendarCell>();
        CalendarMonths = [];
        SelectedCalendarMonth = null;
        CalendarMonthCells.Clear();
        CalendarMonthSummary = "当前范围没有日历数据。";
    }
}

public sealed class ReviewCalendarCell(DateOnly? date, string pnl = "", string trades = "", string pending = "", string journal = "",
    string gap = "", string color = FinancialPalette.Neutral, string background = FinancialPalette.NeutralBackground,
    string detail = "不在当前查询范围内", ICommand? openCommand = null, decimal cashPnl = 0m) : ObservableObject
{
    private bool _isSelected;
    public DateOnly? Date { get; } = date;
    public string Day => Date?.Day.ToString() ?? "";
    public string Pnl { get; } = pnl;
    public decimal CashPnl { get; } = cashPnl;
    public string Trades { get; } = trades;
    public string Pending { get; } = pending;
    public string Journal { get; } = journal;
    public string Gap { get; } = gap;
    public string Color { get; } = color;
    public string Background { get; } = background;
    public string Detail { get; } = detail;
    public ICommand? OpenCommand { get; } = openCommand;
    public bool CanOpen => OpenCommand is not null;
    public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) RaisePropertyChanged(nameof(BorderColor)); } }
    public string BorderColor => IsSelected ? "#8296FF" : "#263142";
}
