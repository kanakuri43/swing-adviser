using System.Collections.ObjectModel;
using SwingAdviser.Application.Ai;
using SwingAdviser.Application.Analysis;
using SwingAdviser.Application.DailyUpdate;
using SwingAdviser.Application.Risk;
using SwingAdviser.Application.Positions;
using SwingAdviser.Domain.Common;

namespace SwingAdviser.Presentation.ViewModels;

/// <summary>
/// メインウィンドウのViewModel。業務ロジック（進捗の解釈・完了/失敗時の文言組み立て・起動時の中断回復・
/// 表示データの読み直し）はここに置き、code-behindにはWPFの都合上必要な最小限の配線だけを残す。
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private readonly DailyUpdateService _dailyUpdateService;
    private readonly AiEvaluationService _aiEvaluationService;
    private readonly CandidateOverviewReader _candidateOverviewReader;
    private readonly HoldingOverviewReader _holdingOverviewReader;
    private readonly ExecutionOverviewReader _executionOverviewReader;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    private string _statusMessage = "起動しました。「日次更新」で株価取得・候補抽出・保有再評価を実行できます。";
    private string _dailyUpdateStageText = "未実行";
    private double _dailyUpdateProgressPercent;
    private bool _isDailyUpdateProgressIndeterminate;

    public MainWindowViewModel(
        DailyUpdateService dailyUpdateService,
        AiEvaluationService aiEvaluationService,
        CandidateOverviewReader candidateOverviewReader,
        HoldingOverviewReader holdingOverviewReader,
        ExecutionOverviewReader executionOverviewReader)
    {
        _dailyUpdateService = dailyUpdateService;
        _aiEvaluationService = aiEvaluationService;
        _candidateOverviewReader = candidateOverviewReader;
        _holdingOverviewReader = holdingOverviewReader;
        _executionOverviewReader = executionOverviewReader;

        RunDailyUpdateCommand = new AsyncRelayCommand(RunDailyUpdateAsync);
        RunDailyUpdateCommand.Faulted += OnDailyUpdateFaulted;
        CancelDailyUpdateCommand = new RelayCommand(() => RunDailyUpdateCommand.Cancel());
    }

    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    public ObservableCollection<PositionRow> Positions { get; } = [];

    public ObservableCollection<ExecutionRow> Executions { get; } = [];

    private CandidateRow? _selectedCandidate;

    public CandidateRow? SelectedCandidate { get => _selectedCandidate; set => Set(ref _selectedCandidate, value); }

    public string Title => "SwingAdviser — 日本株スイング判断支援";

    public string SafetyNotice =>
        "分析結果・AI評価は参考情報であり、利益を保証するものではありません。本アプリは証券会社へ注文を送信せず、自動売買・自動決済は行いません。" +
        "約定は利用者が証券会社側で行い、結果をこのアプリへ手入力します。";

    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    public string DailyUpdateStageText { get => _dailyUpdateStageText; private set => Set(ref _dailyUpdateStageText, value); }

    public double DailyUpdateProgressPercent { get => _dailyUpdateProgressPercent; private set => Set(ref _dailyUpdateProgressPercent, value); }

    public bool IsDailyUpdateProgressIndeterminate { get => _isDailyUpdateProgressIndeterminate; private set => Set(ref _isDailyUpdateProgressIndeterminate, value); }

    public AsyncRelayCommand RunDailyUpdateCommand { get; }

    public RelayCommand CancelDailyUpdateCommand { get; }

    /// <summary>MainWindowのLoadedから呼ぶ。前回起動時に中断されたAI総合評価をFailedとして記録してから表示データを読み込む。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var recoveredCount = await _aiEvaluationService.RecoverInterruptedAsync();
            if (recoveredCount > 0)
            {
                StatusMessage = $"前回起動時に中断されたAI総合評価が{recoveredCount}件あったため、失敗として記録しました。";
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"起動時の初期化に失敗しました: {exception.Message}";
        }

        await ReloadDisplayDataAsync();
    }

    /// <summary>候補/保有/履歴タブの表示データを読み直す。保存を伴う操作（日次更新・約定入力・AI評価）の後は必ずこれを呼ぶ。
    /// 選択中の候補は銘柄・方向で復元する。複数の非同期処理（例: 複数行の同時AI評価）から同時に呼ばれても
    /// ObservableCollectionへのClear/Addが競合しないよう、セマフォで直列化する。</summary>
    public async Task ReloadDisplayDataAsync()
    {
        await _reloadGate.WaitAsync();
        try
        {
            var candidates = await _candidateOverviewReader.GetLatestAsync();
            var positions = await _holdingOverviewReader.GetOpenPositionsAsync();
            var executions = await _executionOverviewReader.GetAllAsync();

            var selectedKey = SelectedCandidate is { } selected
                ? (selected.StockCode, selected.Overview.Direction)
                : ((string StockCode, TradeDirection Direction)?)null;

            Candidates.Clear();
            foreach (var overview in candidates)
            {
                Candidates.Add(new CandidateRow(overview, RunAiEvaluationForRowAsync));
            }

            SelectedCandidate = selectedKey is { } key
                ? Candidates.FirstOrDefault(c => c.StockCode == key.StockCode && c.Overview.Direction == key.Direction)
                : null;

            Positions.Clear();
            foreach (var overview in positions)
            {
                Positions.Add(new PositionRow(overview));
            }

            Executions.Clear();
            foreach (var overview in executions)
            {
                Executions.Add(new ExecutionRow(overview));
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"表示データの読み込みに失敗しました: {exception.Message}";
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task RunAiEvaluationForRowAsync(CandidateRow row, CancellationToken cancellationToken)
    {
        try
        {
            var target = new AiEvaluationTarget(
                row.StockCode, row.Overview.Direction, row.StockName, row.Close, row.Overview.EvaluationDate, row.Score, row.Overview.Confidence);
            await _aiEvaluationService.RunAsync([target], cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"{row.StockCode}のAI総合評価を開始できませんでした: {exception.Message}";
        }
        finally
        {
            await ReloadDisplayDataAsync();
        }
    }

    private async Task RunDailyUpdateAsync(CancellationToken cancellationToken)
    {
        StatusMessage = "日次更新を実行しています…";
        DailyUpdateStageText = "開始準備中";
        DailyUpdateProgressPercent = 0;
        IsDailyUpdateProgressIndeterminate = true;

        DailyUpdateResult? result = null;
        try
        {
            var progress = new Progress<DailyUpdateProgress>(OnDailyUpdateProgress);
            result = await _dailyUpdateService.RunAsync(progress, cancellationToken);
            await ReloadDisplayDataAsync();

            var candidates = await _candidateOverviewReader.GetLatestAsync(cancellationToken);
            var highConfidenceCount = candidates.Count(c => c.Confidence == ConfidenceLevel.High);
            var autoTargets = AiEvaluationService.SelectAutoTargets(candidates);
            var autoSkippedCount = highConfidenceCount - autoTargets.Count;

            AiEvaluationRunResult? autoAiResult = null;
            if (autoTargets.Count > 0)
            {
                IsDailyUpdateProgressIndeterminate = false;
                DailyUpdateProgressPercent = 0;
                DailyUpdateStageText = $"AI総合評価（High候補） 0/{autoTargets.Count}";
                var aiProgress = new Progress<AiEvaluationProgress>(p =>
                {
                    DailyUpdateProgressPercent = 100.0 * p.Completed / p.Total;
                    DailyUpdateStageText = $"AI総合評価（High候補） {p.Completed}/{p.Total}";
                });
                autoAiResult = await _aiEvaluationService.RunAsync(autoTargets, aiProgress, cancellationToken);
            }

            StatusMessage = BuildCompletionMessage(result, autoAiResult, autoSkippedCount);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = result is null
                ? "日次更新を中止しました。完了済みの銘柄までの結果は保存されています。"
                : "日次更新は完了しましたが、AI総合評価（High候補）は中止しました。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"日次更新に失敗しました: {exception.Message}";
        }
        finally
        {
            IsDailyUpdateProgressIndeterminate = false;
            await ReloadDisplayDataAsync();
        }
    }

    private void OnDailyUpdateProgress(DailyUpdateProgress progress)
    {
        IsDailyUpdateProgressIndeterminate = progress.Total <= 0;
        DailyUpdateProgressPercent = progress.Total > 0 ? 100.0 * progress.Completed / progress.Total : 0;
        DailyUpdateStageText = progress.Total > 0
            ? $"{progress.Stage}（{progress.Completed}/{progress.Total}）"
            : progress.Stage;
    }

    private void OnDailyUpdateFaulted(object? sender, Exception exception)
    {
        IsDailyUpdateProgressIndeterminate = false;
        StatusMessage = $"日次更新で予期しないエラーが発生しました: {exception.Message}";
    }

    private static string BuildCompletionMessage(DailyUpdateResult result, AiEvaluationRunResult? autoAiResult, int autoAiSkippedCount)
    {
        var failureText = result.Failures.Count > 0 ? $"（取得失敗{result.Failures.Count}件）" : string.Empty;
        var baseMessage = $"日次更新が完了しました。評価日 {result.EvaluationDate:yyyy-MM-dd}、日足同期 {result.SyncedCount}件{failureText}、" +
               $"候補 {result.CandidateCount}件、保有再評価 {result.HoldingEvaluationCount}件。";

        if (autoAiResult is null)
        {
            return baseMessage;
        }

        var skippedText = autoAiSkippedCount > 0 ? $"・スキップ{autoAiSkippedCount}件" : string.Empty;
        return $"{baseMessage} AI総合評価（High候補）成功{autoAiResult.SucceededCount}件・失敗{autoAiResult.FailedCount}件{skippedText}。";
    }
}
