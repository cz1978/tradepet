using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using TradePet.Application.Review;
using TradePet.Core.Domain;

namespace TradePet.App.ViewModels.Review;

public sealed partial class ReviewWorkspaceViewModel
{
    private bool _updatingRuleDrafts;
    private string _ruleEditorError = "";
    private string? _loadedPlaybookVersionId;
    private IReadOnlyDictionary<string, string> _playbookLabels = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _tradeLabels = new Dictionary<string, string>();
    public ObservableCollection<PlaybookRuleDraft> PlaybookRuleDrafts { get; } = [];
    public ICommand AddPlaybookRuleCommand { get; private set; } = null!;
    public ICommand UseSelectedCampaignTradesCommand { get; private set; } = null!;
    public ICommand UseLoadedOpportunityPlaybookCommand { get; private set; } = null!;
    public ICommand UseSelectedOpportunityTradeCommand { get; private set; } = null!;
    public string OpportunityPlaybookLabel => string.IsNullOrWhiteSpace(OpportunityPlaybook) ? "未关联策略" : _playbookLabels.GetValueOrDefault(OpportunityPlaybook) ?? "已关联历史策略版本";
    public string OpportunityLinkedTradeLabel => string.IsNullOrWhiteSpace(OpportunityLinkedTrade) ? "尚无后续成交关联" : _tradeLabels.GetValueOrDefault(OpportunityLinkedTrade) ?? "已关联历史交易";
    public string RuleEditorError
    {
        get => _ruleEditorError;
        private set { if (SetProperty(ref _ruleEditorError, value)) RaisePropertyChanged(nameof(CanSavePlaybookRules)); }
    }
    public bool CanSavePlaybookRules => RuleEditorError.Length == 0;

    private void InitializePlaybookRuleEditor()
    {
        AddPlaybookRuleCommand = new RelayCommand(() =>
        {
            AddRuleDraft("入场", "", "", false);
            UpdatePlaybookRuleText();
        });
        UseSelectedCampaignTradesCommand = new RelayCommand(() =>
        {
            if (SelectedTradeIds.Count == 0) { StatusText = "先在交易档案勾选要加入的交易。"; return; }
            var existing = CampaignMembers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            CampaignMembers = string.Join(",", existing.Concat(SelectedTradeIds.Select(id => id.ToString())).Distinct());
            StatusText = $"已加入 {SelectedTradeIds.Count} 笔勾选交易；填写共同想法后保存分组。";
        });
        UseLoadedOpportunityPlaybookCommand = new RelayCommand(() =>
        {
            if (_loadedPlaybookVersionId is null) { StatusText = "先在策略与改进页选择已保存的策略模板。"; return; }
            OpportunityPlaybook = _loadedPlaybookVersionId;
        });
        UseSelectedOpportunityTradeCommand = new RelayCommand(() =>
        {
            if (SelectedPositionId is null) { StatusText = "先在交易档案打开一笔交易。"; return; }
            OpportunityLinkedTrade = SelectedPositionId.Value.ToString();
        });
        ReloadPlaybookRuleDrafts();
    }

    private void RefreshFormReferences(ReviewWorkspaceData data)
    {
        _playbookLabels = data.Playbooks.ToDictionary(item => item.Id, item => $"{item.Name} · v{item.Version}");
        _tradeLabels = data.Trades.ToDictionary(item => item.PositionId.ToString(), item => $"{item.Symbol} · #{item.PositionId}");
        RaisePropertyChanged(nameof(OpportunityPlaybookLabel));
        RaisePropertyChanged(nameof(OpportunityLinkedTradeLabel));
    }

    private void LoadPlaybookDraft(PlaybookVersion playbook)
    {
        _loadedPlaybookVersionId = playbook.Id;
        PlaybookName = playbook.Name;
        PlaybookSymbols = playbook.ApplicableSymbols;
        PlaybookConditions = playbook.MarketConditions;
        PlaybookInvalidWhen = playbook.InvalidWhen;
        PlaybookRules = string.Join(Environment.NewLine, playbook.Rules.OrderBy(rule => rule.Order).Select(rule =>
            $"{rule.Section switch { PlaybookRuleSection.Entry => "入场", PlaybookRuleSection.Risk => "风险", PlaybookRuleSection.Management => "管理", _ => "退出" }}|{rule.Name}|{rule.Description}|{(rule.IsCritical ? "关键" : "普通")}"));
        StatusText = $"已载入“{playbook.Name}”v{playbook.Version}；修改后保存会建立新版本。";
    }

    private void ClearFormReferences()
    {
        _loadedPlaybookVersionId = null;
        _playbookLabels = new Dictionary<string, string>();
        _tradeLabels = new Dictionary<string, string>();
        RaisePropertyChanged(nameof(OpportunityPlaybookLabel));
        RaisePropertyChanged(nameof(OpportunityLinkedTradeLabel));
    }

    private void ReloadPlaybookRuleDrafts()
    {
        foreach (var row in PlaybookRuleDrafts) row.PropertyChanged -= OnRuleDraftChanged;
        PlaybookRuleDrafts.Clear();
        foreach (var line in PlaybookRules.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            var section = parts[0] switch { "Entry" => "入场", "Risk" => "风险", "Management" => "管理", "Exit" => "退出", _ => parts[0] };
            AddRuleDraft(section, parts.Length > 1 ? parts[1] : "", parts.Length > 2 ? parts[2] : "", parts.Length > 3 && parts[3] == "关键");
        }
        ValidateRuleDrafts();
    }

    private void AddRuleDraft(string section, string name, string description, bool critical)
    {
        PlaybookRuleDraft? draft = null;
        draft = new PlaybookRuleDraft(section, name, description, critical, new RelayCommand(() =>
        {
            draft!.PropertyChanged -= OnRuleDraftChanged;
            PlaybookRuleDrafts.Remove(draft);
            UpdatePlaybookRuleText();
        }));
        draft.PropertyChanged += OnRuleDraftChanged;
        PlaybookRuleDrafts.Add(draft);
    }

    private void OnRuleDraftChanged(object? sender, PropertyChangedEventArgs e) => UpdatePlaybookRuleText();

    private void ValidateRuleDrafts()
    {
        RuleEditorError = PlaybookRuleDrafts.Any(row => string.IsNullOrWhiteSpace(row.Name)) ? "请填写每条规则的名称。" :
            PlaybookRuleDrafts.Any(row => !row.Sections.Contains(row.Section)) ? "请选择有效的规则阶段。" :
            PlaybookRuleDrafts.Any(row => row.Name.IndexOfAny(['|', '\r', '\n']) >= 0 || row.Description.IndexOfAny(['|', '\r', '\n']) >= 0)
                ? "规则名称和说明请使用单行文字，不包含竖线。" : "";
    }

    private void UpdatePlaybookRuleText()
    {
        ValidateRuleDrafts();
        _updatingRuleDrafts = true;
        try
        {
            PlaybookRules = string.Join(Environment.NewLine, PlaybookRuleDrafts.Select(row =>
                $"{row.Section}|{row.Name.Trim()}|{row.Description.Trim()}|{(row.IsCritical ? "关键" : "普通")}"));
        }
        finally { _updatingRuleDrafts = false; }
    }
}

public sealed class PlaybookRuleDraft(string section, string name, string description, bool isCritical, ICommand removeCommand) : ObservableObject
{
    private string _section = section;
    private string _name = name;
    private string _description = description;
    private bool _isCritical = isCritical;
    public IReadOnlyList<string> Sections { get; } = ["入场", "风险", "管理", "退出"];
    public string Section { get => _section; set => SetProperty(ref _section, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public bool IsCritical { get => _isCritical; set => SetProperty(ref _isCritical, value); }
    public ICommand RemoveCommand { get; } = removeCommand;
}
