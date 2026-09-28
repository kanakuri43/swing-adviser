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
- [x] EF Core DbContext・SQLite接続（`SwingAdviserDbContext`、DIに登録し起動時に`Database.Migrate()`）
- [x] decimal（価格・数量・比率）を SQLite REAL でなく TEXT に保存する value converter（`ConfigureConventions`で全decimalプロパティに一括適用）
- [x] 命名規約適用（テーブル名複数形スネークケース、主キー`<単数形>_id`、瞬間`_at_utc`、暦日`_date`）— `EFCore.NamingConventions`のsnake_case変換＋PKは各Configurationで明示的に列名指定。列挙型も判定根拠が読めるようTEXT保存にした（CLAUDE.md未記載の追加判断）
- [x] 単一の `InitialCreate` マイグレーションのみ作成（要件固まるまで追加マイグレーションを積まない）— `dotnet ef migrations list`で1件のみ確認済み

## Phase 6: Infrastructure — Codex CLI連携
- [x] `CodexCliExecutor`（外部プロセス実行、timeout/実行パス/並列数は設定値化）— `ExecutablePath`が空なら`CodexCliPathResolver`で解決。`--skip-git-repo-check`/`--sandbox read-only`/`--ephemeral`をterraになかった追加フラグとして付与（EXEディレクトリはgit管理外、調査専用でファイル書込・セッション蓄積をさせないため）
- [x] `CodexCliPathResolver`（terraのnpm vendorパス探索をそのまま移植。現行codex-cli 0.147.0で実在パスを確認済み）
- [x] 1件の失敗が他候補の実行・テクニカル結果を無効化しないことの実装 — `ExecuteAsync`は起動失敗/timeout/非ゼロ終了を例外にせず`AiCliResult`として返す。並列数は`SemaphoreSlim(MaxParallelism)`で制御。テクニカル結果（`CandidateEvaluation`/`HoldingEvaluation`）と`AiEvaluation`はPhase1から別行のため構造上も独立

## Phase 7: Application — ユースケース
- [x] 日次更新（終値・出来高取得 → 全銘柄スキャン → 候補抽出 → 保有ポジション再評価）— `StockMasterSynchronizer`/`DailyBarSynchronizer`/`DailyUpdateService`。依存方向をApplication→Infrastructureに反転し、Infrastructure→Applicationの未使用参照を外した
  - 分割・併合は保存済みバーと保有中ポジションへ乗除算のみで換算。効力発生日以後の約定を含むポジションは自動換算せず警告を返す
  - 取得対象は保有中銘柄∪（バー無し／再取得間隔超過／流動性フィルタ通過）の銘柄。1銘柄の取得失敗は理由付きで記録し他銘柄を止めない。地合い指数(1306)の取得失敗のみ日次更新全体を中断する
- [x] 約定手入力ユースケース（確認画面あり、候補一覧からの自動確定UIを作らない）— `ExecutionEntryService`。プレビュー（未保存）→確定（保存）の2段構成。信用返済期限は`Position.SetMarginDueDate`で後から入力できるようにした（Domain追加）
- [x] AI総合評価実行（候補一覧から選択して個別実行）— `AiEvaluationService`。1件ごとに独立したDbContextとtry/catchで処理し、1件の失敗が他候補を無効化しない。`AiPromptBuilder`/`AiResponseParser`を分離
- [x] 過去日の計算にその日より後の情報を混入させないことをユースケース層で担保 — バー読み込みを`BarRepository.LoadBarsAsOfAsync`の1か所に集約（`TradeDate <= asOfDate`のみ）。約定手入力のATRは約定日より前のバーのみで計算

## Phase 8: Presentation — 基盤
- [x] WPF + MVVM + MahApps.Metro シェル、3タブ構成（候補/保有/履歴）— 各タブの中身はプレースホルダのみ（Phase 9で実装）
- [x] code-behindに業務ロジックを書かない — `MainWindow.xaml.cs`はDataContext設定とLoadedからの`InitializeAsync()`呼び出しのみ。進捗解釈・完了/失敗文言・起動時回復はすべて`MainWindowViewModel`
- [x] 長時間処理（更新処理・AI実行）の非同期化：進捗表示・多重実行防止・キャンセル — 再利用可能な`AsyncRelayCommand`（`IsRunning`でCanExecuteを制御、`CancellationTokenSource`、`Faulted`イベントで未処理例外を隔離）を新設し、Phase 9のAI評価実行ボタンでも同じ形を使う想定。「日次更新」ボタンは実際にYahoo/JPX取得→候補抽出→保有再評価まで動作確認済み
  - DI配線: `AddDbContext`を`AddDbContextFactory`に変更（Applicationサービスが要求する`IDbContextFactory`のため）。Phase6/7で作った各サービスをsingleton登録
  - 実機確認で`ProgressBar.Value`のバインドがデフォルトTwoWayのため読み取り専用プロパティに対して`XamlParseException`を起こす不具合を発見・修正（`Mode=OneWay`を明示）

## Phase 9: Presentation — 各タブ
- [x] 候補タブ（Long/Short一覧、スコア・信頼度ラベル・指標値・参考損切幅、AI評価トリガー）— `CandidateOverviewReader`（表示専用読み取り、書き込み側のDailyUpdateServiceとは別）＋`CandidateRow`（1行ごとに`AsyncRelayCommand`でAI評価を実行、多重実行防止はコマンド自身のIsRunningで完結）
- [x] 保有タブ（Exit/TakeProfit/Hold表示、信用返済期限接近の警告、期限未入力＝「未確認」表示）— `HoldingOverviewReader`。残営業日は土日のみ除外する簡易カウント（JPX休場日カレンダーは持たない、警告用の目安と割り切った）
- [x] 履歴タブ（約定手入力フォーム＋確認画面）— `OpenPositionWindow`/`AddExecutionWindow`（各プレビュー→確認→保存の2段ViewModel）。候補・保有タブからは銘柄・方向・対象ポジションIDの入力補助のみを渡す
- [x] AI総合評価結果表示（BUY/SELL推奨ではなく相場見通しである旨を明示、利益保証表現をしない）— 候補タブの選択行詳細パネルに明示文言を固定表示

読み取り側3サービス（`CandidateOverviewReader`/`HoldingOverviewReader`/`ExecutionOverviewReader`）はPhase7の書き込み側サービスと対で追加し、同じ`SqliteInMemoryContextFactory`パターンでテストした。実機起動でXAMLバインディングエラーが出ないことを確認済み（3タブとも表示される。複数モニタ環境での自動クリック操作は今回の環境では安定せず、対話的な全ボタンのクリック確認までは行えていない — 手動での最終確認を推奨）。

## Phase 10: 検証
- [x] `dotnet restore` / `dotnet build` / `dotnet test` が通ること — 167件全て成功
- [x] 過去日シグナル計算への未来データ混入がないことのテスト — `TechnicalIndicatorsTests.Indicators_AppendingFutureBars_DoesNotChangeEarlierValues`（Domain）、`DailyUpdateServiceTests.EvaluateAsync_FutureBarsDoNotAffectPastEvaluationDate`（Application）
- [x] 約定履歴が自動生成されないことのテスト — `DailyUpdateServiceTests.RunAsync_DoesNotCreatePositionsOrExecutions`
- [x] Long/Short対称性テスト — `CandidateScannerTests.Evaluate_ReflectedPriceAxis_...`、`HoldingRiskEvaluatorTests.Evaluate_ReflectedPriceAxis_...`
- [x] リスク判定の優先順位テスト（損切>時間ストップ>Exit>利確>Hold）— `HoldingRiskEvaluatorTests`に隣接優先度が同時成立するケースを3本（StopLoss/TimeStop両立→StopLoss勝ち、等）＋境界値テスト
- [x] 分割前後の価格・株数・ATR単位整合テスト — `DailyBarTests`/`PositionTests`のApplySplit系、`DailyBarSynchronizerTests`（保存済みバー・保有ポジションへの伝播）
- [x] 期待値を本体と同じ計算式で再計算するだけの無意味なテストを書いていないか確認 — 全テストを監査し、`TechnicalIndicatorsTests.Macd_SignalSeed_ExcludesUndefinedLeadingValues`が本体と同じ再帰式をテスト内で再実装していた1件のみ該当。EMA自体の再帰式検証は`Ema_MatchesHandComputedSeedAndRecursion`（手計算のリテラル値）が既に独立にカバー済みのため、Macd固有の境界（未定義区間・シグナルの種の扱い・Histogram=Line−Signatureの整合性）だけを見るよう書き直した。他に該当なし
- [x] テーブル数が35を超えていないか確認（目安15前後）— 7テーブル（ai_evaluations/candidate_evaluations/daily_bars/holding_evaluations/positions/stocks/executions）、`InitialCreate`マイグレーション1件のみ
