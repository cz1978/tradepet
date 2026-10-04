using System.Windows;
using TradePet.Core.Localization;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBlock = System.Windows.Controls.TextBlock;
using WrapPanel = System.Windows.Controls.WrapPanel;

namespace TradePet.App.Views;

public sealed record PetActionChoice(string Value, string Label);
public sealed record PetActionChoiceGroup(string Key, string Title, IReadOnlyList<PetActionChoice> Choices);

public partial class BehaviorActionCard : System.Windows.Controls.UserControl
{
    private readonly Dictionary<string, IReadOnlyList<RadioButton>> _choices = [];
    private bool _saving;
    public bool IsCompleted { get; private set; }
    public Func<BehaviorActionCard, Task<string?>>? SaveActionAsync { get; set; }
    public event Action<BehaviorActionCard>? Completed;

    public BehaviorActionCard(string title, string summary, string saveLabel, IReadOnlyList<PetActionChoiceGroup> groups,
        bool compactSummary = false)
    {
        InitializeComponent();
        TitleText.Text = UiText.Translate(title);
        SummaryText.Text = UiText.Translate(summary);
        if (compactSummary)
        {
            SummaryDetailsText.Text = SummaryText.Text;
            SummaryText.Text = string.Join(Environment.NewLine, SummaryText.Text.Split('\n').Take(4)).TrimEnd();
            SummaryDetails.Visibility = Visibility.Visible;
        }
        SaveButton.Content = UiText.Translate(saveLabel);
        foreach (var group in groups)
        {
            ChoiceGroups.Children.Add(new TextBlock { Text = UiText.Translate(group.Title), FontSize = 12, Margin = new Thickness(0, 8, 0, 4) });
            var panel = new WrapPanel();
            var buttons = new List<RadioButton>();
            foreach (var choice in group.Choices)
            {
                var button = new RadioButton
                {
                    Content = UiText.Translate(choice.Label), Tag = choice.Value, GroupName = group.Key,
                    Style = (Style)FindResource("PetQuickChoiceStyle"),
                };
                panel.Children.Add(button);
                buttons.Add(button);
            }
            _choices.Add(group.Key, buttons);
            ChoiceGroups.Children.Add(panel);
        }
    }

    public string? Selection(string group) => _choices.GetValueOrDefault(group)?.FirstOrDefault(item => item.IsChecked == true)?.Tag as string;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || IsCompleted) return;
        _saving = true;
        Form.IsEnabled = FooterButtons.IsEnabled = false;
        StatusText.Text = UiText.Translate("正在记录…");
        string? error;
        try { error = SaveActionAsync is null ? "本地记录尚未就绪，请稍后重试。" : await SaveActionAsync(this); }
        catch (Exception) { error = "记录失败，内容仍保留，请重试。"; }
        finally { _saving = false; Form.IsEnabled = FooterButtons.IsEnabled = true; }
        if (error is not null) { StatusText.Text = UiText.Translate(error); return; }
        Dismiss();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Dismiss();
    public void Dismiss()
    {
        if (_saving || IsCompleted) return;
        IsCompleted = true;
        Completed?.Invoke(this);
    }
}
