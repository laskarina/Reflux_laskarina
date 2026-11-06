# Reflux (laskarina fork)

beatmania IIDX INFINITAS のメモリからスコアを読み取り、tsv ファイルに書き出すツール [olji/Reflux](https://github.com/olji/Reflux) の非公式フォークです。
An unofficial fork of [olji/Reflux](https://github.com/olji/Reflux), a tool that reads your scores from beatmania IIDX INFINITAS memory and writes them to tsv files.

[日本語](#日本語) | [English](#english)

---

## 日本語

### このフォークについて

- **Reflux 本体と Reflux Server の更新予定はありません。** 今後サポートするのは、INFINITAS からのデータ取得と tsv ファイル出力のみです。
  - [Reflux Server](https://github.com/olji/Reflux_Server) への送信（`saveremote`）、JSON 出力、配信用テキストの出力などは、オリジナルのコードをそのまま残していますがサポート対象外です。
  - INFINITAS のバージョンが上がったときに必要になるオフセット情報（`offsets.txt`）は、`config.ini` の `updateserver`（このリポジトリの `master` ブランチ）から取得します。サーバーに無い場合はメモリを走査してオフセットを自動で探します。
- このフォーク独自の機能として、プレー履歴を曲ごとに閲覧できる **スコアビューア**（`Reflux.HistoryViewer.exe`）を同梱しています。詳しくは「[スコアビューア](#スコアビューア)」を参照してください。

### Reflux の使い方

1. [Releases](https://github.com/laskarina/Reflux_laskarina/releases) から zip をダウンロードし、任意のフォルダに展開します。
2. 必要に応じて `config.ini` を編集します。初期設定のままでも動作します。
3. `Reflux.exe` を起動します。INFINITAS の起動前でも後でも構いません。INFINITAS に接続し、曲リストの読み込みが終わると記録を始めます。
4. 以降、1 曲プレーするたびに下記のファイルが更新されます。

- `Reflux.exe` と `Reflux.HistoryViewer.exe` はどちらも必要なランタイムを同梱しているので、.NET のインストールは不要です。
- 起動時にこのリポジトリの Releases を確認し、新しいバージョンがあれば赤字で知らせます。自動更新はされません。

### 出力ファイル

| ファイル | 内容 | 設定 |
|---|---|---|
| `tracker.tsv` | 全曲・全譜面の自己ベスト（ランプ、グレード、EX スコア、BP、解禁状態、DJ POINTS）。プレーごとに更新されます。[Mina's Unlock Tracker Spreadsheet](https://docs.google.com/spreadsheets/d/1oCcBVNUSRKA5NE9d6lYEyUNHPBosMZchtwsy0yL_nSM/edit?usp=sharing)（自分用にコピーして使用）に貼り付けられます | `[Record] savelocal` |
| `sessions/Session_<日時>.tsv` | 起動ごとに 1 ファイル、1 プレー 1 行。オプション（RAN、LIFT、アシストなど）、結果（スコア、ランプ、グレード）、判定（PGREAT〜POOR、FAST/SLOW）、ゲージ % などを記録します | `[Record] savelocal`、列の選択は `[LocalRecord]` |
| `sessions/Session_<日時>.json` | 上記の JSON 版 | `[Record] savejson` |
| `history/playlog.jsonl` | スコアビューア用のプレー履歴。1 プレー 1 行の追記のみで、書き換えられることはありません | `[Record] savehistory` |
| `latest.json`、`latest*.txt` | 直近のプレー結果。配信画面などに表示するためのものです | `[Record] savelatestjson` / `savelatesttxt` |
| `playstate.txt`、`marquee.txt`、`title.txt` など | 現在のプレー状態と選曲中の曲情報。配信用です | `[Livestream]` |

### config.ini の主な項目

```ini
[Update]
updateFiles = true      ; INFINITAS のバージョンが変わったとき offsets.txt などを updateserver から取得する
updateserver = "https://raw.githubusercontent.com/laskarina/Reflux_laskarina/master/Reflux"

[Record]
saveremote = false      ; Reflux Server へ送信する（このフォークではサポート対象外）
savelocal = true        ; sessions/Session_*.tsv を出力する
savejson = false        ; sessions/Session_*.json を出力する
savelatestjson = true   ; latest.json を出力する
savelatesttxt = true    ; latest*.txt を出力する
savehistory = true      ; history/playlog.jsonl へ追記する（スコアビューア用）
```

`[LocalRecord]` で tsv に含める列を、`[Livestream]` で配信用ファイルの出力をそれぞれ切り替えられます。以前のバージョンの `config.ini` を使い回す場合は、`[Record]` に `savehistory = true` を追加してください。キーが無いときは履歴は記録されません。

### スコアビューア

`Reflux.HistoryViewer.exe` は、Reflux が `history/playlog.jsonl` に記録したプレー履歴を曲・難易度ごとに閲覧する Windows 用アプリです。zip に同梱されているので、`Reflux.exe` と同じフォルダに置いたまま起動してください。

**できること**

- 曲一覧: 譜面ごとのプレー回数、自己ベスト EX スコア、自己ベストランプ、最小 BP、最終プレー日時を表示します。曲名、レベル、サイド（SP/DP）、難易度、ランプ、期間で絞り込めます。
- 譜面の詳細: 選んだ譜面の全プレーを時系列で表示します。EX スコア、グレード、ランプ、BP、ゲージ種別と終了時のゲージ %、オプション、判定内訳、FAST/SLOW、途中終了の有無が分かります。EX スコアと BP の推移グラフも表示します。
- 日別サマリ: 日ごとのプレー数と自己ベスト更新数を表示します。

**使い方**

- `savehistory = true`（初期設定）で Reflux を使っていれば、プレーするたびに `history/playlog.jsonl` に 1 行追記されます。ビューアが読むのはこのファイルだけです。
- 起動すると、自分のフォルダ、次に 1 つ上のフォルダの順に `history/playlog.jsonl` を探します。見つからなければフォルダ選択ダイアログが開きます。`ファイル > フォルダを開く` でいつでも変更できます。選んだフォルダ、ウィンドウの位置と大きさ、絞り込み条件、並び順、表示中のタブは `%APPDATA%\Reflux.HistoryViewer\settings.json` に保存され、次回起動時に復元されます。
- Reflux と同時に起動しておくと、新しいプレーは数秒以内に一覧へ反映されます。
- 読み込んだデータは `history/viewer.db`（SQLite）にキャッシュされます。正となるのは `playlog.jsonl` のほうなので、`viewer.db` はいつ削除しても構いません。次回起動時、または `ファイル > 全件取り込み直し` で作り直されます。
- ビューアが `playlog.jsonl` や Reflux の他の出力ファイルに書き込むことはありません。ネットワークも使いません。
- `savehistory` を有効にする前のプレーも見たい場合は、`ファイル > 過去データを取り込む` で `sessions/Session_*.json`（`savejson = true` のときの出力）を取り込めます。これらは曲 ID やオプションを持たないため曲名と難易度で紐づけられ、出典列に「旧JSON」と表示されます。各ファイルは 1 回だけ取り込まれ、結果は `viewer.db` にのみ保存されます。

**ショートカット**: `F5` 再取り込み、`Ctrl+F` 検索ボックスへ移動、`Esc` フィルタ解除

**動作環境**: 64 bit 版の Windows 10 / 11

### ソースからビルドする

.NET 8 SDK が必要です。

```
dotnet build Reflux.sln -c Release
dotnet test Reflux.HistoryViewer.Tests
```

`master` または `develop` に push すると GitHub Actions（`.github/workflows/build.yml`）が Windows 上でビルドとテストを行い、`Reflux.exe` と `Reflux.HistoryViewer.exe` を含む zip をアーティファクトとして保存します。`master` では GitHub Release も作成されます。

### ライセンス

MIT License です。オリジナルの Reflux は Rickard Jonsson（[olji](https://github.com/olji)）氏の作品です。[LICENSE](LICENSE) を参照してください。

---

## English

### About this fork

- **Reflux itself and Reflux Server will not receive further updates.** Only reading data from INFINITAS and writing tsv files are planned to be supported.
  - Uploading to a [Reflux Server](https://github.com/olji/Reflux_Server) (`saveremote`), JSON output and the livestream text files are kept as they are in the original code, but they are not supported.
  - The memory offsets (`offsets.txt`) needed after an INFINITAS update are fetched from `updateserver` in `config.ini`, which points at the `master` branch of this repository. If the server has none, Reflux scans the game memory and finds the offsets by itself.
- This fork adds a **score viewer** (`Reflux.HistoryViewer.exe`) that browses your play history per song. See [Score viewer](#score-viewer).

### Using Reflux

1. Download the zip from [Releases](https://github.com/laskarina/Reflux_laskarina/releases) and extract it anywhere.
2. Edit `config.ini` if needed. The defaults work as they are.
3. Run `Reflux.exe`, before or after starting INFINITAS. It attaches to the game, waits until the song list is loaded and then starts recording.
4. From then on, the files below are updated after every play.

- Both `Reflux.exe` and `Reflux.HistoryViewer.exe` ship with the runtime they need, so no .NET installation is required.
- On start, Reflux checks the Releases of this repository and prints a red notice when a newer version exists. It does not update itself.

### Output files

| File | Contents | Setting |
|---|---|---|
| `tracker.tsv` | Your best result for every chart (lamp, grade, EX score, miss count, unlock state, DJ POINTS), updated after every play. Can be pasted into [Mina's Unlock Tracker Spreadsheet](https://docs.google.com/spreadsheets/d/1oCcBVNUSRKA5NE9d6lYEyUNHPBosMZchtwsy0yL_nSM/edit?usp=sharing) (make your own copy) | `[Record] savelocal` |
| `sessions/Session_<datetime>.tsv` | One file per launch, one line per play: options (RAN, LIFT, assist, ...), results (score, lamp, grade), judges (PGREAT to POOR, FAST/SLOW), gauge percent and so on | `[Record] savelocal`; columns via `[LocalRecord]` |
| `sessions/Session_<datetime>.json` | JSON version of the above | `[Record] savejson` |
| `history/playlog.jsonl` | Play history for the score viewer. One line per play, append-only, never rewritten | `[Record] savehistory` |
| `latest.json`, `latest*.txt` | The most recent play, meant for stream overlays | `[Record] savelatestjson` / `savelatesttxt` |
| `playstate.txt`, `marquee.txt`, `title.txt`, ... | Current play state and the selected song, meant for streaming | `[Livestream]` |

### Main keys in config.ini

```ini
[Update]
updateFiles = true      ; fetch offsets.txt etc. from updateserver when the INFINITAS version changes
updateserver = "https://raw.githubusercontent.com/laskarina/Reflux_laskarina/master/Reflux"

[Record]
saveremote = false      ; upload to a Reflux Server (not supported by this fork)
savelocal = true        ; write sessions/Session_*.tsv
savejson = false        ; write sessions/Session_*.json
savelatestjson = true   ; write latest.json
savelatesttxt = true    ; write latest*.txt
savehistory = true      ; append to history/playlog.jsonl (for the score viewer)
```

`[LocalRecord]` selects the tsv columns and `[Livestream]` toggles the streaming files. If you keep a `config.ini` from an earlier version, add `savehistory = true` under `[Record]`; without the key no history is recorded.

### Score viewer

`Reflux.HistoryViewer.exe` is a Windows app that browses the play history Reflux records in `history/playlog.jsonl`, per song and difficulty. It is included in the zip; keep it in the same folder as `Reflux.exe` and start it from there. Its user interface is in Japanese.

**What it shows**

- Song list: play count, best EX score, best lamp, lowest miss count and last played time for every chart. Filter by title, level, side (SP/DP), difficulty, lamp and date range.
- Chart detail: every play of the selected chart in time order, with EX score, grade, lamp, miss count, gauge type and gauge percent at the end, options, judge counts, FAST/SLOW and whether the play ended early. EX score and miss count are also drawn as graphs.
- Daily summary: plays per day and how many personal bests were set.

**How to use it**

- With `savehistory = true` (the default) Reflux appends one line to `history/playlog.jsonl` per play. That file is the only thing the viewer reads.
- On start the viewer looks for `history/playlog.jsonl` in its own folder and then in the parent folder. If neither has one, a folder dialog opens. You can change the folder at any time with `ファイル > フォルダを開く` (File > Open folder). The chosen folder, the window position and size, the filters, the sort order and the current tab are saved in `%APPDATA%\Reflux.HistoryViewer\settings.json` and restored on the next start.
- Keep it open while Reflux is running and new plays show up within a few seconds.
- The imported data is cached in `history/viewer.db` (SQLite). `playlog.jsonl` stays the source of truth, so `viewer.db` can be deleted at any time; it is rebuilt on the next start or with `ファイル > 全件取り込み直し` (File > Rebuild all).
- The viewer never writes to `playlog.jsonl` or to any other Reflux file, and it does not use the network.
- To see plays from before `savehistory` was enabled, use `ファイル > 過去データを取り込む` (File > Import past data) to import the `sessions/Session_*.json` files written with `savejson = true`. Those plays carry no song id or options, so they are matched by title and difficulty and are marked as old JSON in the source column. Each file is imported once and the result goes into `viewer.db` only.

**Shortcuts**: `F5` re-import, `Ctrl+F` jump to the search box, `Esc` clear the filters

**Requirements**: 64-bit Windows 10 / 11

### Building from source

The .NET 8 SDK is required.

```
dotnet build Reflux.sln -c Release
dotnet test Reflux.HistoryViewer.Tests
```

Every push to `master` or `develop` runs GitHub Actions (`.github/workflows/build.yml`) on Windows, which builds, runs the tests and stores a zip containing `Reflux.exe` and `Reflux.HistoryViewer.exe` as an artifact. Pushes to `master` also create a GitHub Release.

### License

MIT License. The original Reflux is the work of Rickard Jonsson ([olji](https://github.com/olji)). See [LICENSE](LICENSE).
