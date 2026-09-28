# AI総合評価（AiEvaluation）に関するメモ

## 現状の構造（2026-09-28時点）
- `SwingAdviser.Domain.Ai.AiEvaluation`: `Pending → Running → Succeeded | Failed` の4状態のみ。
  `MarkSucceeded(verdict, confidence, summary, positiveFactors, riskFactors, invalidationConditions, nowUtc)` — **引数は6個**（`referenceUrls`は廃止済み）。
- `referenceUrls`（参照URL一覧）は2026-09-28に完全に削除した。理由: ユーザー要望で「参照URLは不要」。
  影響範囲: `AiPromptBuilder`（プロンプトのJSONスキーマから除去）、`AiResponseParser`、`AiEvaluationService`、
  `AiEvaluation`ドメイン、`AiEvaluationConfiguration`（EF Core mapping）、`CandidateOverviewReader`、
  `CandidateRow`、`MainWindow.xaml`（参照URL表示のItemsControl削除）、関連テスト一式。
- `summary`フィールドはAIに「2〜3文程度に区切り、1文を長くしすぎない」よう`AiPromptBuilder`で指示している
  （ユーザーから「一文が長すぎて読みにくい」との指摘を受けて追加）。
  **注意**: これは今後新規実行される評価にのみ効く。DBに保存済みの過去のAI評価結果（古いsummary）は
  遡って書き換わらない。古い評価を新フォーマットにしたい場合は候補タブの「実行」ボタンで再実行が必要
  （再実行すると新しい`AiEvaluation`行が追加され、`CandidateOverviewReader`は最新行を表示する）。

## CLAUDE.mdとの整合
プロジェクトの`CLAUDE.md`「AI総合評価」節もこの変更に合わせて更新済み（参照URLの記載を削除、summaryの方針を追記）。
仕様変更をしたら必ず`CLAUDE.md`側も同期させること（このプロジェクトの方針）。
