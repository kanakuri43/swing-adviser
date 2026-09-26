# TODO.md

実装ステップのチェックリスト。数値・仕様の詳細は常に `CLAUDE.md` を正とする（このファイルには数値を重複記載しない）。
依存関係の少ない順（Domain→Infrastructure→Application→Presentation）に並べてあるが、垂直スライスで進めても良い。

各フェーズ着手前に「意図的に作らないもの」（revision/manifest凍結、単位ハッシュ検証、MarginCostLedger、多段fail-closed分類、3層Lot構造）を作っていないか確認する。

## Phase 0: プロジェクト基盤
- [x] ソリューション作成、4プロジェクト分割（Domain / Application / Infrastructure / Presentation）+ テストプロジェクト
- [x] Domain に外部依存を持ち込まない（NuGet追加はApplication以降のみ）
- [x] NuGet: EF Core Sqlite、MahApps.Metro
- [x] `appsettings.json` 雛形作成（StrategyParameters、Yahoo取得のレート制御値、Codex CLIの実行パス/timeout/並列数、流動性フィルタ閾値）
- [x] DB配置: `AppContext.BaseDirectory` 基準で `swing-adviser.db` 固定、書込不可なら起動時エラー（暗黙フォールバック禁止）

## Phase 1: Domain — エンティティ
- [ ] 銘柄マスタ（コード・銘柄名・市場区分・有効フラグ）
- [ ] 日足（銘柄+取引日で一意制約）
- [ ] 分析結果/候補（判定日ごとに1行追記、使用した指標値・戦略パラメータをその行にそのまま保存。manifest/hashによる別テーブル凍結はしない）
- [ ] ポジション（銘柄・方向・状態・メモ）
- [ ] 約定明細（1ポジションに対し複数の新規/決済を紐付け、信用返済期限を約定ごとに保持）
- [ ] AI評価（状態はPending/Running/Succeeded/Failedの4状態のみ。verdict/confidence/summary/好材料/リスク要因/無効化条件/参照URL一覧）
- [ ] revision/supersedeチェーンにしない。訂正はUPDATE＋変更理由の追記欄のみ

## Phase 2: Domain — テクニカル分析エンジン
- [ ] MACD・EMA(20/100)・ATR14(Wilder)・出来高倍率(20日平均)の計算関数
- [ ] TOPIX連動ETF(1306.T)によるMACD地合い判定
- [ ] Long候補化ゲート一式（MACDトリガー・MACD勢い・トレンド環境・過熱除外）
- [ ] Short候補化ゲート（Longの完全対称、共通コードで実装し重複を避ける）
- [ ] スコアリング（0-100、下駄なし加点、MACD系配点50点）とHigh/Medium/Lowラベル
- [ ] terraの `TechnicalAnalysisEngine.cs` は指標計算式の参照のみに使う。条件1/条件2構造はそのまま移植しない

## Phase 3: Domain — リスク評価器（保有再評価）
- [ ] 建玉時ATR14固定の損切ライン（以後の日次再計算で上書きしない）
- [ ] 1.5R到達時の50%一部利確候補判定
- [ ] Exit判定（MACDデッドクロス or 終値のEMA20割れ、Long/Short対称）
- [ ] 時間ストップ（建玉から20営業日）
- [ ] 優先順位ロジック（損切 > 時間ストップ > Exit > 利確 > Hold）
- [ ] 分割・併合時の株数・取得単価・損切/利確ラインの換算（元約定=監査原票は変更しない、乗除算のみでハッシュ照合はしない）

## Phase 4: Infrastructure — 市場データ取得
- [ ] Yahoo Finance chart API 呼び出し（`query1.finance.yahoo.com/v8/finance/chart/{code}.T`）
- [ ] レート制御の実装
- [ ] JPX上場銘柄一覧CSV取込
- [ ] 流動性フィルタ（出来高・時価総額）で東証国内普通株をスキャン対象に絞込み
- [ ] 分割・併合調整（単純乗除算）

## Phase 5: Infrastructure — 永続化
- [ ] EF Core DbContext・SQLite接続
- [ ] decimal（価格・数量・比率）を SQLite REAL でなく TEXT に保存する value converter
- [ ] 命名規約適用（テーブル名複数形スネークケース、主キー`<単数形>_id`、瞬間`_at_utc`、暦日`_date`）
- [ ] 単一の `InitialCreate` マイグレーションのみ作成（要件固まるまで追加マイグレーションを積まない）

## Phase 6: Infrastructure — Codex CLI連携
- [ ] `CodexCliExecutor`（外部プロセス実行、timeout/実行パス/並列数は設定値化）
- [ ] `CodexCliPathResolver`
- [ ] 1件の失敗が他候補の実行・テクニカル結果を無効化しないことの実装

## Phase 7: Application — ユースケース
- [ ] 日次更新（終値・出来高取得 → 全銘柄スキャン → 候補抽出 → 保有ポジション再評価）
- [ ] 約定手入力ユースケース（確認画面あり、候補一覧からの自動確定UIを作らない）
- [ ] AI総合評価実行（候補一覧から選択して個別実行）
- [ ] 過去日の計算にその日より後の情報を混入させないことをユースケース層で担保

## Phase 8: Presentation — 基盤
- [ ] WPF + MVVM + MahApps.Metro シェル、3タブ構成（候補/保有/履歴）
- [ ] code-behindに業務ロジックを書かない
- [ ] 長時間処理（更新処理・AI実行）の非同期化：進捗表示・多重実行防止・キャンセル

## Phase 9: Presentation — 各タブ
- [ ] 候補タブ（Long/Short一覧、スコア・信頼度ラベル・指標値・参考損切幅、AI評価トリガー）
- [ ] 保有タブ（Exit/TakeProfit/Hold表示、信用返済期限接近の警告、期限未入力＝「未確認」表示）
- [ ] 履歴タブ（約定手入力フォーム＋確認画面）
- [ ] AI総合評価結果表示（BUY/SELL推奨ではなく相場見通しである旨を明示、利益保証表現をしない）

## Phase 10: 検証
- [ ] `dotnet restore` / `dotnet build` / `dotnet test` が通ること
- [ ] 過去日シグナル計算への未来データ混入がないことのテスト
- [ ] 約定履歴が自動生成されないことのテスト
- [ ] Long/Short対称性テスト
- [ ] リスク判定の優先順位テスト（損切>時間ストップ>Exit>利確>Hold）
- [ ] 分割前後の価格・株数・ATR単位整合テスト
- [ ] 期待値を本体と同じ計算式で再計算するだけの無意味なテストを書いていないか確認
- [ ] テーブル数が35を超えていないか確認（目安15前後）
