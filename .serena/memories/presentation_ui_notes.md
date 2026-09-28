# Presentation層（WPF/MahApps）のUIメモ

## MainWindow.xaml の構成
単一ファイルにタブ3枚（候補/保有/履歴）をベタ書きしている。コードビハインドは配線のみ
（`MainWindow.xaml.cs`）、業務ロジック・状態組み立ては`MainWindowViewModel`に集約。

## 候補タブの表示（2026-09-28に改修）
- 「方向」「AI見通し」列は`DataGridTemplateColumn`で角丸バッジ表示にしている
  （`DirectionBadgeBorder`/`DirectionBadgeText`、`AiVerdictBadgeBorder`/`AiVerdictBadgeText`という
  Window.Resourcesのスタイルを使用）。色は既存のBullish/Bearish・損益表示と揃えてある
  （Long/Bullish/Profit系=緑`#FF25833D`、Short/Bearish/Loss系=赤`#FFC73535`）。
- 詳細パネル用の`AiVerdictGridText`スタイル（バッジではなく単純な文字色）は別途残っている
  （AI総合評価結果パネルのタイトル行で使用中なので削除しないこと）。
- AI評価結果パネルの「参照URL」セクションは廃止済み（`mem:ai_evaluation_notes`参照）。

## 日次更新ボタン周り
- `MainWindowViewModel`に`ElapsedTimeText`（実行中のみ、`DispatcherTimer`で1秒毎更新、
  `経過時間: mm:ss`形式、1時間超は`hh:mm:ss`）と`LastUpdatedAtText`
  （`最終更新日時（JST）: yyyy-MM-dd HH:mm`、日次更新の株価取得・候補抽出が成功した時点で設定）を追加。
  どちらもセッション内メモリのみで、アプリ再起動をまたいで永続化はしていない（要件が
  「日次更新が終わったら表示する」のみだったための意図的な簡素化）。

## 動作確認の制約
このリポジトリの開発環境はWPFアプリをGUI起動して目視確認できない（表示なしのシェル環境）。
`dotnet build`/`dotnet test`のみで検証しており、実際の見た目（折り返し・バッジ色など）は
必ず実機（Windows GUI環境）での目視確認が別途必要。
