using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Security;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// Blocklist card for Parent Mode (UC05). Shows built-in and custom rules grouped
/// by category and lets the parent disable/enable them; deletion is not offered
/// for built-in rules.
/// </summary>
public sealed partial class BlocklistViewModel : ObservableObject
{
    private readonly IBlockRuleUseCase _rules;
    private readonly IAppLogger _logger;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;

    public BlocklistViewModel(IBlockRuleUseCase rules, IAppLogger logger)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<BlockRuleGroup> Groups { get; } = [];

    public bool HasRules => Groups.Count > 0;

    public bool HasNoRules => Groups.Count == 0;

    public bool IsNotBusy => !IsBusy;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasStatusMessage));

    /// <summary>Loads the stored rules when Parent Mode opens. Never throws.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Apply(await Task.Run(() => _rules.ListAsync(cancellationToken), cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ParentAuthorizationException)
        {
            Apply([]);
            SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not read the blocklist.", ex);
            SetStatus("Không thể đọc danh sách chặn. Vui lòng thử lại.", true);
        }
    }

    [RelayCommand]
    private async Task ToggleRuleAsync(BlockRuleRow? row)
    {
        if (row is null || IsBusy)
            return;

        IsBusy = true;

        try
        {
            var result = await Task.Run(() => _rules.SetEnabledAsync(row.Id, !row.IsEnabled));
            Apply(await Task.Run(() => _rules.ListAsync()));

            var state = result.Rule.IsEnabled ? "bật" : "tắt";
            SetStatus($"Đã {state} rule {result.Rule.Value}.", false);
        }
        catch (ParentAuthorizationException)
        {
            SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        }
        catch (InvalidOperationException)
        {
            SetStatus("Rule không còn tồn tại. Danh sách sẽ được làm mới.", true);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not change a block rule.", ex);
            SetStatus("Không thể đổi rule lúc này. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(IReadOnlyList<BlockRuleInfo> rules)
    {
        Groups.Clear();

        foreach (var group in rules.GroupBy(rule => rule.Category).OrderBy(group => group.Key))
        {
            Groups.Add(
                new BlockRuleGroup(
                    CategoryTitle(group.Key),
                    group.Select(rule => new BlockRuleRow(rule)).ToList()));
        }

        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(HasNoRules));
    }

    private static string CategoryTitle(BlockRuleCategory category) =>
        category switch
        {
            BlockRuleCategory.Chatbot => "Chatbot / AI Assistant",
            BlockRuleCategory.Social => "Mạng xã hội",
            BlockRuleCategory.Game => "Trò chơi",
            BlockRuleCategory.Study => "Học tập",
            BlockRuleCategory.System => "Hệ thống",
            _ => "Tùy chỉnh"
        };

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}

public sealed record BlockRuleGroup(string Title, IReadOnlyList<BlockRuleRow> Rules);

public sealed class BlockRuleRow
{
    public BlockRuleRow(BlockRuleInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Id = info.Id;
        Value = info.Value;
        IsEnabled = info.IsEnabled;
        SourceText = info.IsBuiltIn ? "Mặc định" : "Tùy chỉnh";
        ToggleButtonText = info.IsEnabled ? "Tắt" : "Bật";

        var effect = info.Effect == BlockRuleEffect.Allow ? "Cho phép" : "Chặn";
        var state = info.IsEnabled ? "Đang bật" : "Đang tắt";
        DetailText = $"{effect} · {SourceText} · {state}";

        if (info.EnforcementStatus == EnforcementStatus.NotEnforced)
            DetailText += " · Chưa được hỗ trợ";
        else if (info.RuleType == RuleType.Url || info.MatchType != RuleMatchType.Exact)
            DetailText += " · Cần extension trình duyệt";
    }

    public Guid Id { get; }

    public string Value { get; }

    public bool IsEnabled { get; }

    public string SourceText { get; }

    public string ToggleButtonText { get; }

    public string DetailText { get; }
}
