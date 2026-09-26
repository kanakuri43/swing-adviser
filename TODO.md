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
- [x] 銘柄マスタ（コード・銘柄名・市場区分・有効フラグ）
- [x] 日足（銘柄+取引日で一意制約。EF Coreでの一意インデックス設定自体はPhase 5）
- [x] 分析結果/候補（判定日ごとに1行追記、使用した指標値・戦略パラメータをその行にそのまま保存。manifest/hashによる別テーブル凍結はしない）
  - 候補化スキャンと保有再評価は判断内容が別物のため、`CandidateEvaluation`（新規候補）と`HoldingEvaluation`（保有ポジション再評価）の2エンティティに分けた
- [x] ポジション（銘柄・方向・状態・メモ）
- [x] 約定明細（1ポジションに対し複数の新規/決済を紐付け、信用返済期限を約定ごとに保持）
- [x] AI評価（状態はPending/Running/Succeeded/Failedの4状態のみ。verdict/confidence/summary/好材料/リスク要因/無効化条件/参照URL一覧）
- [x] revision/supersedeチェーンにしない。訂正はUPDATE＋変更理由の追記欄のみ（`Execution.Correct`/`CorrectionLog`）

## Phase 2: Domain — テクニカル分析エンジン
- [x] MACD・EMA(20/100)・ATR14(Wilder)・出来高倍率(20日平均)の計算関数（`TechnicalIndicators`）
- [x] TOPIX連動ETF(1306.T)によるMACD地合い判定（ゲートではなくスコア専用。地合い不一致でも候補化はする）
- [x] Long候補化ゲート一式（MACDトリガー・MACD勢い・トレンド環境・過熱除外）
- [x] Short候補化ゲート（Longの完全対称、`sign`正規化による共通コードで実装。重複コードなし）
- [x] スコアリング（0-100、下駄なし加点、MACD系配点50点）とHigh/Medium/Lowラベル
  - CLAUDE.md未確定だったMACD勢いスコアの満点閾値は`MomentumFullScoreAtrMultiple`(仮値0.3)として追加
- [x] terraの `TechnicalAnalysisEngine.cs` は指標計算式の参照のみに使う。条件1/条件2構造はそのまま移植しない

## Phase 3: Domain — リスク評価器（保有再評価）
- [x] 建玉時ATR14固定の損切ライン（以後の日次再計算で上書きしない）— Phase 1の`Position`で完了済み
- [x] 1.5R到達時の50%一部利確候補判定（`HoldingRiskEvaluator`。一部利確済みかは`Position.Executions`のClose約定有無から導出し、専用フラグは持たない）
- [x] Exit判定（MACDデッドクロス or 終値のEMA20割れ、Long/Short対称）— 状態ベース判定（クロス当日限定にしない）。`sign`正規化でCandidateScannerと同様に共通コード化
- [x] 時間ストップ（建玉から20営業日）— barsのインデックス差で営業日ベースに計算
- [x] 優先順位ロジック（損切 > 時間ストップ > Exit > 利確 > Hold）— if-elseの直列評価
- [x] 分割・併合時の株数・取得単価・損切/利確ラインの換算（元約定=監査原票は変更しない、乗除算のみでハッシュ照合はしない）— Phase 1の`Position.ApplySplit`で完了済み

## Phase 4: Infrastructure — 市場データ取得
- [x] Yahoo Finance chart API 呼び出し（`query1.finance.yahoo.com/v8/finance/chart/{code}.T`）— `events.splits`から分割を直接取得。配当(`events=div`)は取得しない
- [x] レート制御の実装（`RateLimiter`、`MarketData.YahooFinance.MaxRequestsPerSecond`から算出）
- [x] JPX上場銘柄一覧取込（Excel/CSV/TSV両対応。東証国内普通株かつプライム/スタンダード/グロースのみに絞ってパーサー段階で除外）
- [x] 流動性フィルタ（20営業日平均売買代金=終値×出来高、`LiquidityFilterOptions`の閾値で判定。CLAUDE.md記載を時価総額→売買代金に変更済み、Phase0参照）
- [x] 分割・併合調整（単純乗除算）— Yahoo応答のnumerator/denominatorからratioを算出し、Phase 1の`DailyBar.ApplySplit(ratio)`とそのまま整合。保存済みバーへの適用タイミング（いつUPDATEするか）はApplication層(Phase 7)の責務として持ち越し

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
