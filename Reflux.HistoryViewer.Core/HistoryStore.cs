using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Reflux.HistoryViewer.Core;

/// <summary>
/// Imports history/playlog.jsonl into a private SQLite cache (viewer.db) and answers the viewer's queries.
/// The JSONL file stays the source of truth: the database can be deleted and is rebuilt from scratch.
/// All members are thread-safe; one connection is shared and every call is serialized with a lock, so an
/// import that is running on a background thread simply delays queries until its transaction is committed.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    public const int SchemaVersion = 1;
    public const string DbFileName = "viewer.db";
    public const string PlaylogKind = "playlog";
    public const string SessionJsonKind = "session_json";

    const string Schema = """
        CREATE TABLE IF NOT EXISTS charts (
            id INTEGER PRIMARY KEY,
            song_id TEXT,
            difficulty TEXT NOT NULL,
            title TEXT,
            title_english TEXT,
            artist TEXT,
            genre TEXT,
            bpm TEXT,
            level INTEGER,
            total_notes INTEGER
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_charts_song ON charts(song_id, difficulty) WHERE song_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_charts_title ON charts(title, difficulty);
        CREATE TABLE IF NOT EXISTS import_state (
            id INTEGER PRIMARY KEY,
            path TEXT NOT NULL UNIQUE,
            kind TEXT NOT NULL,
            offset INTEGER NOT NULL DEFAULT 0,
            size INTEGER NOT NULL DEFAULT 0,
            skipped INTEGER NOT NULL DEFAULT 0,
            updated_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS plays (
            id INTEGER PRIMARY KEY,
            chart_id INTEGER NOT NULL,
            import_id INTEGER NOT NULL,
            recorded_at TEXT NOT NULL,
            play_side TEXT,
            data_available INTEGER NOT NULL,
            ex_score INTEGER,
            grade TEXT,
            lamp TEXT,
            lamp_rank INTEGER,
            miss_count INTEGER,
            gauge_type TEXT,
            gauge_percent INTEGER,
            pgreat INTEGER,
            great INTEGER,
            good INTEGER,
            bad INTEGER,
            poor INTEGER,
            combo_break INTEGER,
            fast INTEGER,
            slow INTEGER,
            style TEXT,
            style2 TEXT,
            assist TEXT,
            range TEXT,
            flip INTEGER,
            battle INTEGER,
            hran INTEGER,
            premature_end INTEGER,
            game_version TEXT,
            source TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_plays_chart_time ON plays(chart_id, recorded_at);
        CREATE INDEX IF NOT EXISTS ix_plays_import ON plays(import_id);
        CREATE INDEX IF NOT EXISTS ix_plays_time ON plays(recorded_at);
        """;

    const string ChartSelect = """
        SELECT c.id, c.song_id, c.difficulty, c.title, c.title_english, c.artist, c.genre, c.bpm, c.level, c.total_notes,
               COUNT(p.id), MAX(p.ex_score), MAX(p.lamp_rank), MIN(p.miss_count), MAX(p.recorded_at)
        FROM charts c
        JOIN plays p ON p.chart_id = c.id
        """;

    const string PlaySelect = """
        SELECT id, chart_id, recorded_at, play_side, data_available, ex_score, grade, lamp, miss_count, gauge_type, gauge_percent,
               pgreat, great, good, bad, poor, combo_break, fast, slow,
               style, style2, assist, range, flip, battle, hran, premature_end, game_version, source
        FROM plays
        """;

    readonly SqliteConnection _conn;
    readonly object _sync = new();

    public string DbPath { get; }

    public HistoryStore(string dbPath)
    {
        DbPath = Path.GetFullPath(dbPath);
        var dir = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, /* so Dispose really closes the file and viewer.db can be deleted */
        }.ToString();
        _conn = new SqliteConnection(connectionString);
        _conn.Open();
        Exec("PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;");
        EnsureSchema();
    }

    public static string DefaultDbPath(string historyDir) => Path.Combine(historyDir, DbFileName);

    public void Dispose()
    {
        lock (_sync)
        {
            _conn.Dispose();
        }
    }

    #region Import

    /// <summary>
    /// Import new lines of a playlog.jsonl file. Resumes from the byte offset stored in import_state, reads only
    /// newline-terminated lines, inserts everything in one transaction and skips (but counts) broken lines.
    /// If the file is smaller than last time it is treated as recreated: its rows are dropped and it is read again.
    /// </summary>
    public ImportResult ImportPlaylog(string path, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_sync)
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists)
            {
                return ImportResult.NotFound(fullPath);
            }
            using var tx = _conn.BeginTransaction();
            var state = GetOrCreateState(fullPath, PlaylogKind, tx);
            long fileSize = file.Length;
            long offset = state.Offset;
            long skippedTotal = state.Skipped;
            bool rebuilt = false;
            if (fileSize < state.Size || fileSize < offset)
            {
                DeletePlaysOfImport(state.Id, tx);
                offset = 0;
                skippedTotal = 0;
                rebuilt = true;
            }

            int inserted = 0, skipped = 0, lines = 0;
            long? firstId = null, lastId = null;
            var chartIds = new HashSet<long>();
            long consumed = offset;
            if (fileSize > offset)
            {
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
                var index = new ChartIndex(_conn, tx);
                using var insert = CreatePlayInsert(tx);
                foreach (var (line, endOffset) in JsonLines.Read(stream, offset, cancellationToken))
                {
                    var record = PlaylogParser.TryParse(line);
                    if (record is null)
                    {
                        skipped++;
                    }
                    else
                    {
                        long chartId = index.Resolve(record);
                        long playId = InsertPlay(insert, record, chartId, state.Id);
                        inserted++;
                        firstId ??= playId;
                        lastId = playId;
                        chartIds.Add(chartId);
                    }
                    consumed = endOffset;
                    if (++lines % 1000 == 0)
                    {
                        progress?.Report(new ImportProgress(lines, consumed - offset, fileSize - offset));
                    }
                }
                progress?.Report(new ImportProgress(lines, consumed - offset, fileSize - offset));
            }
            if (rebuilt)
            {
                DeleteOrphanCharts(tx);
            }
            SaveState(state.Id, consumed, fileSize, skippedTotal + skipped, tx);
            tx.Commit();
            return new ImportResult(fullPath, true, inserted, skipped, rebuilt, consumed, firstId, lastId, chartIds);
        }
    }

    /// <summary>
    /// Import one sessions/Session_*.json file written by Reflux (savejson = true). The whole file is read at once and
    /// inserted in one transaction; a file that was imported before is skipped (<see cref="ImportResult.AlreadyImported"/>).
    /// The rows get source = session_json and carry no song_id, so they join the playlog rows by (title, difficulty).
    /// Only viewer.db is written, never playlog.jsonl. Throws for a file that is not a Reflux session file.
    /// </summary>
    public ImportResult ImportSessionJson(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            return ImportResult.NotFound(fullPath);
        }
        var parsed = SessionJsonImporter.ParseFile(fullPath);
        lock (_sync)
        {
            if (ReadStates("WHERE path = @path", ("@path", fullPath)).Count > 0)
            {
                return new ImportResult(fullPath, true, 0, 0, false, file.Length, null, null, new HashSet<long>()) { AlreadyImported = true };
            }
            using var tx = _conn.BeginTransaction();
            var state = GetOrCreateState(fullPath, SessionJsonKind, tx);
            var index = new ChartIndex(_conn, tx);
            using var insert = CreatePlayInsert(tx);
            int inserted = 0;
            long? firstId = null, lastId = null;
            var chartIds = new HashSet<long>();
            foreach (var record in parsed.Records)
            {
                long chartId = index.Resolve(record);
                long playId = InsertPlay(insert, record, chartId, state.Id);
                inserted++;
                firstId ??= playId;
                lastId = playId;
                chartIds.Add(chartId);
            }
            SaveState(state.Id, file.Length, file.Length, parsed.Skipped, tx);
            tx.Commit();
            return new ImportResult(fullPath, true, inserted, parsed.Skipped, false, file.Length, firstId, lastId, chartIds);
        }
    }

    /// <summary>Forget everything imported from a file so that the next import reads it from the beginning.</summary>
    public void ResetImport(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_sync)
        {
            using var tx = _conn.BeginTransaction();
            var id = Scalar("SELECT id FROM import_state WHERE path = @path", tx, ("@path", fullPath));
            if (id is long importId)
            {
                DeletePlaysOfImport(importId, tx);
                Exec("DELETE FROM import_state WHERE id = @id", tx, ("@id", importId));
                DeleteOrphanCharts(tx);
            }
            tx.Commit();
        }
    }

    public ImportState? GetImportState(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_sync)
        {
            return ReadStates("WHERE path = @path", ("@path", fullPath)).FirstOrDefault();
        }
    }

    public IReadOnlyList<ImportState> GetImportStates()
    {
        lock (_sync)
        {
            return ReadStates("ORDER BY id");
        }
    }

    #endregion

    #region Queries

    public IReadOnlyList<ChartSummary> QueryCharts(ChartFilter? filter = null)
    {
        filter ??= ChartFilter.None;
        var sql = new StringBuilder(ChartSelect);
        var args = new List<(string, object)>();
        sql.Append(" WHERE 1 = 1");
        if (!string.IsNullOrWhiteSpace(filter.TitleContains))
        {
            sql.Append(" AND (c.title LIKE @title ESCAPE '\\' OR c.title_english LIKE @title ESCAPE '\\')");
            args.Add(("@title", "%" + EscapeLike(filter.TitleContains.Trim()) + "%"));
        }
        if (filter.Level is int level)
        {
            sql.Append(" AND c.level = @level");
            args.Add(("@level", (long)level));
        }
        if (!string.IsNullOrEmpty(filter.Side))
        {
            sql.Append(" AND substr(c.difficulty, 1, 2) = @side");
            args.Add(("@side", filter.Side));
        }
        if (!string.IsNullOrEmpty(filter.DifficultyLetter))
        {
            sql.Append(" AND substr(c.difficulty, 3, 1) = @diff");
            args.Add(("@diff", filter.DifficultyLetter));
        }
        if (filter.FromUtc is not null)
        {
            sql.Append(" AND p.recorded_at >= @from");
            args.Add(("@from", filter.FromUtc));
        }
        if (filter.ToUtc is not null)
        {
            sql.Append(" AND p.recorded_at < @to");
            args.Add(("@to", filter.ToUtc));
        }
        sql.Append(" GROUP BY c.id");
        if (filter.BestLampRank is int lamp)
        {
            sql.Append(" HAVING MAX(p.lamp_rank) = @lamp");
            args.Add(("@lamp", (long)lamp));
        }
        sql.Append(" ORDER BY MAX(p.recorded_at) DESC, c.id DESC");
        lock (_sync)
        {
            return ReadCharts(sql.ToString(), args.ToArray());
        }
    }

    /// <summary>Header information for one chart with all-time aggregates (no filter applied).</summary>
    public ChartSummary? GetChart(long chartId)
    {
        lock (_sync)
        {
            return ReadCharts(ChartSelect + " WHERE c.id = @id GROUP BY c.id", ("@id", chartId)).FirstOrDefault();
        }
    }

    public IReadOnlyList<PlayRow> QueryPlays(long chartId, bool newestFirst = true)
    {
        var sql = PlaySelect + " WHERE chart_id = @id ORDER BY recorded_at " + (newestFirst ? "DESC, id DESC" : "ASC, id ASC");
        lock (_sync)
        {
            using var cmd = Command(sql, null, ("@id", chartId));
            using var reader = cmd.ExecuteReader();
            var list = new List<PlayRow>();
            while (reader.Read())
            {
                list.Add(ReadPlay(reader));
            }
            return list;
        }
    }

    /// <summary>Plays per local day, newest day first, with the number of plays that set a new EX best for their chart.</summary>
    public IReadOnlyList<DailySummary> QueryDaily()
    {
        const string sql = """
            WITH ordered AS (
                SELECT recorded_at, ex_score,
                       MAX(ex_score) OVER (PARTITION BY chart_id ORDER BY recorded_at, id ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS prev_best
                FROM plays
            )
            SELECT date(recorded_at, 'localtime') AS day,
                   COUNT(*) AS plays,
                   SUM(CASE WHEN ex_score IS NOT NULL AND (prev_best IS NULL OR ex_score > prev_best) THEN 1 ELSE 0 END) AS new_bests
            FROM ordered
            GROUP BY day
            ORDER BY day DESC
            """;
        lock (_sync)
        {
            using var cmd = Command(sql, null);
            using var reader = cmd.ExecuteReader();
            var list = new List<DailySummary>();
            while (reader.Read())
            {
                list.Add(new DailySummary(reader.GetString(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2)));
            }
            return list;
        }
    }

    public StoreStats GetStats()
    {
        lock (_sync)
        {
            using var cmd = Command("SELECT (SELECT COUNT(*) FROM charts), (SELECT COUNT(*) FROM plays), (SELECT COALESCE(SUM(skipped), 0) FROM import_state)", null);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            return new StoreStats((int)reader.GetInt64(0), (int)reader.GetInt64(1), reader.GetInt64(2));
        }
    }

    #endregion

    #region Internals

    void EnsureSchema()
    {
        var version = (long)(Scalar("PRAGMA user_version", null) ?? 0L);
        if (version != 0 && version != SchemaVersion)
        {
            Exec("DROP TABLE IF EXISTS plays; DROP TABLE IF EXISTS charts; DROP TABLE IF EXISTS import_state;");
            version = 0;
        }
        Exec(Schema);
        if (version == 0)
        {
            Exec("PRAGMA user_version = " + SchemaVersion.ToString(CultureInfo.InvariantCulture));
        }
    }

    ImportState GetOrCreateState(string fullPath, string kind, SqliteTransaction tx)
    {
        var existing = ReadStates("WHERE path = @path", tx, ("@path", fullPath)).FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }
        var now = PlaylogParser.FormatTimestamp(DateTime.UtcNow);
        var id = (long)Scalar("INSERT INTO import_state (path, kind, offset, size, skipped, updated_at) VALUES (@path, @kind, 0, 0, 0, @now) RETURNING id",
            tx, ("@path", fullPath), ("@kind", kind), ("@now", now))!;
        return new ImportState(id, fullPath, kind, 0, 0, 0, now);
    }

    void SaveState(long id, long offset, long size, long skipped, SqliteTransaction tx)
    {
        Exec("UPDATE import_state SET offset = @offset, size = @size, skipped = @skipped, updated_at = @now WHERE id = @id", tx,
            ("@offset", offset), ("@size", size), ("@skipped", skipped), ("@now", PlaylogParser.FormatTimestamp(DateTime.UtcNow)), ("@id", id));
    }

    IReadOnlyList<ImportState> ReadStates(string clause, params (string, object)[] args) => ReadStates(clause, null, args);

    IReadOnlyList<ImportState> ReadStates(string clause, SqliteTransaction? tx, params (string, object)[] args)
    {
        using var cmd = Command("SELECT id, path, kind, offset, size, skipped, updated_at FROM import_state " + clause, tx, args);
        using var reader = cmd.ExecuteReader();
        var list = new List<ImportState>();
        while (reader.Read())
        {
            list.Add(new ImportState(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetString(6)));
        }
        return list;
    }

    void DeletePlaysOfImport(long importId, SqliteTransaction tx) => Exec("DELETE FROM plays WHERE import_id = @id", tx, ("@id", importId));

    void DeleteOrphanCharts(SqliteTransaction tx) => Exec("DELETE FROM charts WHERE NOT EXISTS (SELECT 1 FROM plays WHERE plays.chart_id = charts.id)", tx);

    SqliteCommand CreatePlayInsert(SqliteTransaction tx)
    {
        var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO plays (chart_id, import_id, recorded_at, play_side, data_available, ex_score, grade, lamp, lamp_rank, miss_count, gauge_type, gauge_percent,
                               pgreat, great, good, bad, poor, combo_break, fast, slow, style, style2, assist, range, flip, battle, hran, premature_end, game_version, source)
            VALUES (@chart_id, @import_id, @recorded_at, @play_side, @data_available, @ex_score, @grade, @lamp, @lamp_rank, @miss_count, @gauge_type, @gauge_percent,
                    @pgreat, @great, @good, @bad, @poor, @combo_break, @fast, @slow, @style, @style2, @assist, @range, @flip, @battle, @hran, @premature_end, @game_version, @source)
            RETURNING id
            """;
        foreach (var name in new[] { "chart_id", "import_id", "recorded_at", "play_side", "data_available", "ex_score", "grade", "lamp", "lamp_rank", "miss_count", "gauge_type", "gauge_percent",
            "pgreat", "great", "good", "bad", "poor", "combo_break", "fast", "slow", "style", "style2", "assist", "range", "flip", "battle", "hran", "premature_end", "game_version", "source" })
        {
            cmd.Parameters.AddWithValue("@" + name, DBNull.Value);
        }
        return cmd;
    }

    static long InsertPlay(SqliteCommand cmd, PlayRecord r, long chartId, long importId)
    {
        var p = cmd.Parameters;
        p["@chart_id"].Value = chartId;
        p["@import_id"].Value = importId;
        p["@recorded_at"].Value = r.RecordedAt;
        p["@play_side"].Value = Db(r.PlaySide);
        p["@data_available"].Value = r.DataAvailable ? 1L : 0L;
        p["@ex_score"].Value = Db(r.ExScore);
        p["@grade"].Value = Db(r.Grade);
        p["@lamp"].Value = Db(r.Lamp);
        p["@lamp_rank"].Value = Db(LampInfo.Rank(r.Lamp));
        p["@miss_count"].Value = Db(r.MissCount);
        p["@gauge_type"].Value = Db(r.GaugeType);
        p["@gauge_percent"].Value = Db(r.GaugePercent);
        p["@pgreat"].Value = Db(r.PGreat);
        p["@great"].Value = Db(r.Great);
        p["@good"].Value = Db(r.Good);
        p["@bad"].Value = Db(r.Bad);
        p["@poor"].Value = Db(r.Poor);
        p["@combo_break"].Value = Db(r.ComboBreak);
        p["@fast"].Value = Db(r.Fast);
        p["@slow"].Value = Db(r.Slow);
        p["@style"].Value = Db(r.Style);
        p["@style2"].Value = Db(r.Style2);
        p["@assist"].Value = Db(r.Assist);
        p["@range"].Value = Db(r.Range);
        p["@flip"].Value = Db(r.Flip);
        p["@battle"].Value = Db(r.Battle);
        p["@hran"].Value = Db(r.Hran);
        p["@premature_end"].Value = Db(r.PrematureEnd);
        p["@game_version"].Value = Db(r.GameVersion);
        p["@source"].Value = r.Source;
        return (long)cmd.ExecuteScalar()!;
    }

    IReadOnlyList<ChartSummary> ReadCharts(string sql, params (string, object)[] args)
    {
        using var cmd = Command(sql, null, args);
        using var reader = cmd.ExecuteReader();
        var list = new List<ChartSummary>();
        while (reader.Read())
        {
            list.Add(new ChartSummary(
                reader.GetInt64(0), S(reader, 1), reader.GetString(2), S(reader, 3), S(reader, 4), S(reader, 5), S(reader, 6), S(reader, 7), I(reader, 8), I(reader, 9),
                (int)reader.GetInt64(10), I(reader, 11), I(reader, 12), I(reader, 13), S(reader, 14)));
        }
        return list;
    }

    static PlayRow ReadPlay(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), S(r, 3), r.GetInt64(4) != 0, I(r, 5), S(r, 6), S(r, 7), I(r, 8), S(r, 9), I(r, 10),
        I(r, 11), I(r, 12), I(r, 13), I(r, 14), I(r, 15), I(r, 16), I(r, 17), I(r, 18),
        S(r, 19), S(r, 20), S(r, 21), S(r, 22), B(r, 23), B(r, 24), B(r, 25), B(r, 26), S(r, 27), r.GetString(28));

    SqliteCommand Command(string sql, SqliteTransaction? tx, params (string Name, object Value)[] args)
    {
        var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }

    void Exec(string sql, SqliteTransaction? tx = null, params (string, object)[] args)
    {
        using var cmd = Command(sql, tx, args);
        cmd.ExecuteNonQuery();
    }

    object? Scalar(string sql, SqliteTransaction? tx, params (string, object)[] args)
    {
        using var cmd = Command(sql, tx, args);
        var value = cmd.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    static object Db(string? v) => v is null ? DBNull.Value : v;
    static object Db(int? v) => v is null ? DBNull.Value : (long)v.Value;
    static object Db(bool? v) => v is null ? DBNull.Value : (v.Value ? 1L : 0L);
    static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    static int? I(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : (int)r.GetInt64(i);
    static bool? B(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i) != 0;

    /// <summary>
    /// Resolves records to chart rows during one import. Charts are identified by (song_id, difficulty);
    /// records without a song_id (sessions/*.json imports) fall back to (title, difficulty). A chart that was
    /// first created without a song_id is upgraded in place when a record with the same title arrives with one.
    /// </summary>
    sealed class ChartIndex
    {
        sealed class Entry
        {
            public long Id;
            public string? SongId;
            public string? Title;
            public bool HasMeta;
        }

        readonly Dictionary<(string SongId, string Difficulty), Entry> _bySong = new();
        readonly Dictionary<(string Title, string Difficulty), List<Entry>> _byTitle = new();
        readonly SqliteCommand _insert;
        readonly SqliteCommand _update;

        public ChartIndex(SqliteConnection conn, SqliteTransaction tx)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id, song_id, difficulty, title, level, total_notes FROM charts";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var entry = new Entry { Id = reader.GetInt64(0), SongId = S(reader, 1), Title = S(reader, 3), HasMeta = !reader.IsDBNull(4) && !reader.IsDBNull(5) };
                    Add(entry, reader.GetString(2));
                }
            }
            _insert = conn.CreateCommand();
            _insert.Transaction = tx;
            _insert.CommandText = """
                INSERT INTO charts (song_id, difficulty, title, title_english, artist, genre, bpm, level, total_notes)
                VALUES (@song_id, @difficulty, @title, @title_english, @artist, @genre, @bpm, @level, @total_notes)
                RETURNING id
                """;
            _update = conn.CreateCommand();
            _update.Transaction = tx;
            _update.CommandText = """
                UPDATE charts SET song_id = COALESCE(@song_id, song_id), title = COALESCE(@title, title), title_english = COALESCE(@title_english, title_english),
                                  artist = COALESCE(@artist, artist), genre = COALESCE(@genre, genre), bpm = COALESCE(@bpm, bpm),
                                  level = COALESCE(@level, level), total_notes = COALESCE(@total_notes, total_notes)
                WHERE id = @id
                """;
            foreach (var cmd in new[] { _insert, _update })
            {
                foreach (var name in new[] { "song_id", "difficulty", "title", "title_english", "artist", "genre", "bpm", "level", "total_notes", "id" })
                {
                    cmd.Parameters.AddWithValue("@" + name, DBNull.Value);
                }
            }
        }

        public long Resolve(PlayRecord record)
        {
            var difficulty = record.Difficulty;
            if (record.SongId is { } songId)
            {
                if (_bySong.TryGetValue((songId, difficulty), out var entry))
                {
                    if ((record.Title is not null && record.Title != entry.Title) || (!entry.HasMeta && record.Level is not null && record.TotalNotes is not null))
                    {
                        Update(entry, record);
                    }
                    return entry.Id;
                }
                var orphan = Lookup(record.Title, difficulty)?.FirstOrDefault(e => e.SongId is null);
                if (orphan is not null)
                {
                    Update(orphan, record);
                    _bySong[(songId, difficulty)] = orphan;
                    return orphan.Id;
                }
                var created = Insert(record);
                _bySong[(songId, difficulty)] = created;
                Add(created, difficulty, registerSong: false);
                return created.Id;
            }
            else
            {
                var candidates = Lookup(record.Title, difficulty);
                if (candidates is { Count: > 0 })
                {
                    return (candidates.FirstOrDefault(e => e.SongId is not null) ?? candidates[0]).Id;
                }
                var created = Insert(record);
                Add(created, difficulty);
                return created.Id;
            }
        }

        List<Entry>? Lookup(string? title, string difficulty) => _byTitle.TryGetValue((title ?? "", difficulty), out var list) ? list : null;

        void Add(Entry entry, string difficulty, bool registerSong = true)
        {
            if (registerSong && entry.SongId is not null)
            {
                _bySong[(entry.SongId, difficulty)] = entry;
            }
            var key = (entry.Title ?? "", difficulty);
            if (!_byTitle.TryGetValue(key, out var list))
            {
                _byTitle[key] = list = new List<Entry>();
            }
            list.Add(entry);
        }

        Entry Insert(PlayRecord record)
        {
            Fill(_insert, record, null);
            var id = (long)_insert.ExecuteScalar()!;
            return new Entry { Id = id, SongId = record.SongId, Title = record.Title, HasMeta = record.Level is not null && record.TotalNotes is not null };
        }

        void Update(Entry entry, PlayRecord record)
        {
            Fill(_update, record, entry.Id);
            _update.ExecuteNonQuery();
            entry.SongId ??= record.SongId;
            if (record.Title is not null && record.Title != entry.Title)
            {
                entry.Title = record.Title;
                var key = (record.Title, record.Difficulty);
                if (!_byTitle.TryGetValue(key, out var list))
                {
                    _byTitle[key] = list = new List<Entry>();
                }
                if (!list.Contains(entry))
                {
                    list.Add(entry);
                }
            }
            entry.HasMeta |= record.Level is not null && record.TotalNotes is not null;
        }

        static void Fill(SqliteCommand cmd, PlayRecord r, long? id)
        {
            var p = cmd.Parameters;
            p["@song_id"].Value = Db(r.SongId);
            p["@difficulty"].Value = r.Difficulty;
            p["@title"].Value = Db(r.Title);
            p["@title_english"].Value = Db(r.TitleEnglish);
            p["@artist"].Value = Db(r.Artist);
            p["@genre"].Value = Db(r.Genre);
            p["@bpm"].Value = Db(r.Bpm);
            p["@level"].Value = Db(r.Level);
            p["@total_notes"].Value = Db(r.TotalNotes);
            p["@id"].Value = id is null ? DBNull.Value : id.Value;
        }
    }

    #endregion
}
