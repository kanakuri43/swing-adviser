# EF Coreマイグレーション運用ルール（重要・要注意）

## 方針
`CLAUDE.md`に明記の通り、初期実装は単一の`InitialCreate`マイグレーションのみとし、要件が固まるまで
追加マイグレーションを積まない。スキーマを直す必要が出たら、**新しいマイグレーションを足すのではなく
`InitialCreate`自体を編集し直す**。

## 罠（2026-09-28に実際に踏んだ）
`dotnet ef migrations remove` → `dotnet ef migrations add InitialCreate` で素朴に作り直すと、
マイグレーションファイル名とID（先頭のタイムスタンプ、例: `20260926112305_InitialCreate`）が
新しいタイムスタンプに変わってしまう。

これをやると、**既にそのマイグレーションIDで`__EFMigrationsHistory`に記録済みの実DB**（この個人アプリは
実行中EXEと同じディレクトリの`swing-adviser.db`を使う運用）に対して、次回起動時の`Database.Migrate()`が
「新しいマイグレーションが未適用」と誤認し、既存テーブルに対して`CREATE TABLE`を再実行しようとしてクラッシュする。
実際、開発中のDB（日足97万件・銘柄3708件・候補275件・AI評価30件などの実データ入り）で検証し、
この壊れ方を確認済み。

## 正しい手順
1. `dotnet ef migrations remove` → `dotnet ef migrations add InitialCreate` で新しいUp/Downコードを生成する
   （これは一時的な作業用。スキーマ差分を機械的に出すために使うだけ）。
2. 生成された新しい`.cs`/`.Designer.cs`の中身（テーブル定義）はそのまま使うが、
   ファイル名と`.Designer.cs`内の`[Migration("...")]`属性の文字列は**元の`InitialCreate`のIDに戻す**
   （例: `20260926112305_InitialCreate`のまま）。
3. `SwingAdviserDbContextModelSnapshot.cs`はマイグレーションIDを持たないのでそのまま新しい内容でよい。
4. 既存の実DBには物理的に削除したはずの列（例: `reference_urls`）が残るが、EF側のモデルからは
   マッピングを外しているので実害はない（単なる未使用列として残るだけ）。

## 検証方法
本番と同じ`UseSqlite + UseSnakeCaseNamingConvention`設定で、既存DBのコピーに対して
`context.Database.Migrate()`を呼ぶ使い捨てのxUnitテストを書いて実行し、例外が出ないこと・
データ件数が読めることを確認してから、テストファイルを削除する、という手順で検証した。
