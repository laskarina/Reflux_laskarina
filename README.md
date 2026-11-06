# Reflux

Probes INFINITAS memory and outputs all top scores into a tsv file.

It also saves plays in sessions files with the following data:
- Settings (RAN,LIFT,Autoscratch etc.)
- Play results (Score, lamp, DJ level)
- Judge data (Pgreat, misses, fast/slow)
- Metadata (Gauge percent, note progress)

It also dumps the database for chart unlocks.

Some live streaming utilities are available as well, such as outputting current playing song and the play state tracking info to separate files for displaying on screen or automating behaviour.

Play data is, depending on configuration, appended to a .tsv session file, a json file and/or sent to a URL running a [Reflux Server](https://github.com/olji/Reflux_Server) (Or homebrew with compatible API). 

Memory offsets used to find the relevant information is stored in a file along with the build version it works with. On start the binary will probe the executable for build version by brute force and compare with what's listed in the offset file.
If no match it will check for an applicable offset file on a configurable server.
This behaviour can be turned off if unwanted.

Full compatibility with Mina's Unlock Tracker Spreadsheet (make your own copy): https://docs.google.com/spreadsheets/d/1oCcBVNUSRKA5NE9d6lYEyUNHPBosMZchtwsy0yL_nSM/edit?usp=sharing

## Play history (`history/playlog.jsonl`)

Set `savehistory = true` in the `[Record]` section of `config.ini` to have Reflux append one record per play to `history/playlog.jsonl` (JSON Lines: one JSON object per line, UTF-8 without BOM).

```ini
[Record]
savehistory = true
```

- Every finished play is recorded, including plays with H-RAN, BATTLE or assist options that the tsv/json outputs skip. For those, `data_available` is `false` and `lamp` / `gauge_percent` are `null`. Quick retries are not recorded, and plays that ended early carry `premature_end: true`.
- Each line holds the song id, title, artist, genre, BPM, difficulty, level, note count, play side (P1/P2/DP), EX score, grade, lamp, BP, gauge type and end-of-play gauge percent, all judge counts, FAST/SLOW, the options used (style, style2 for DP, assist, range, flip, battle, H-RAN) and the INFINITAS build / Reflux version that wrote it. `schema_version` is `1`.
- The file is append-only and is never rewritten. Delete it to start over.
- The existing outputs (`tracker.tsv`, `sessions/`, `latest.*`, the livestream text files) are not affected, and when the key is missing from `config.ini` nothing changes.
