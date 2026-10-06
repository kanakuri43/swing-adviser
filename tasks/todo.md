# 損益推移グラフ（4つ目のタブ「損益」）

## 目的
運用サイクル手順5（約定を入力した後）の振り返り。「自分の累計損益がどう推移しているか」を日次で見る。参考値であり、手数料・信用コスト・配当は含まない（既存の通算損益と同じ注記）。

## 方針
- 系列は日次の2本: 実現損益の累計 / 通算損益（実現累計＋その日の含み損益）。
- 期間: 最初の約定日〜最新の終値日。
- 計算は Application 層の新規 `ProfitAndLossHistoryReader`。Domain は変更しない。
- 日次の含み損益は `LoadBarsAsOfAsync` 経由で、その日以前の約定・終値のみ使う（未来データ混入防止）。
- 分割調整は既存の AdjustedPrice/AdjustedQuantity に従う。
- スキーマ変更・マイグレーション追加なし。
- グラフ描画は Presentation のみ。ライブラリは下記の決定待ち。

## 実装項目
- [x] OxyPlot.Wpf を Presentation.csproj のみに追加（Domain/Application には入れない）
- [x] `ProfitAndLossHistoryReader`（Application/Positions）: 日付ごとの (実現累計, 含み, 通算) を返す
- [x] `MainWindowViewModel` に系列プロパティを追加し `ReloadDisplayDataAsync` で読み込む（コンストラクタ引数追加に伴い `MainWindowViewModelTests` も修正）
- [x] `MainWindow.xaml` に「損益」タブを追加（履歴タブの右隣）。約定ゼロ時は「データなし」表示
- [x] DI 登録（`App.xaml.cs`、Singleton）
- [x] テスト: 決済日に実現が階段状に増える／Short の符号／分割前後で連続／約定日より前の日は含めない／終値欠損日の扱い／ポジション未決済のみの場合
- [x] `dotnet build` / `dotnet test` / 実機で目視確認

## 決定事項
1. ライブラリ: OxyPlot.Wpf（ユーザー決定）。
2. 実現損益: 既存の `RealizedProfitAndLossOf`（ポジション全体の平均取得単価基準）に合わせる（ユーザー決定）。保有・履歴タブの数値と最終値が一致する。
3. 終値欠損日（案）: その銘柄の直近の既知終値を引き継ぐ。まだ一度も終値が無ければ含み損益は0扱い。

## レビュー
- `ProfitAndLossHistoryReader` 新規、`ProfitAndLossChartModel` 新規、MainWindow に「損益」タブ追加。スキーマ変更なし。
- build 0エラー、テスト196件合格（新規6件）。起動してウィンドウ表示まで確認。グラフの見た目は実データでの目視が未確認。
