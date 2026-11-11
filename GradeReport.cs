using Microsoft.Data.Sqlite;

static class GradeReport
{
    public static void Run(string[] args)
    {
        if (args.Length != 2 && args.Length != 3 && args.Length != 4)
            throw new FormatException("Usage: grade course.db study.tsv");
        using var db = Open(args[1]);
        using var tx = Begin(db);
        Sql(db, "CREATE TABLE IF NOT EXISTS rule(version TEXT,criterion TEXT,weight REAL,level TEXT,percent REAL,descriptor TEXT,PRIMARY KEY(version,criterion,level)); CREATE TABLE IF NOT EXISTS submission(id TEXT PRIMARY KEY, student TEXT,version TEXT); CREATE TABLE IF NOT EXISTS mark(submission TEXT REFERENCES submission(id),criterion TEXT,level TEXT,feedback TEXT,PRIMARY KEY(submission,criterion))");
        if (args.Length >= 3)
            foreach (var r in Rows(args[2]))
            {
                if (r is ["rule", var version, var criterion, var weight, var level, var percent, var desc])
                {
                    var w = Positive(weight);
                    var p = Amount(percent);
                    Require(p <= 100, "Percent above 100.");
                    var existing = Query(db, "SELECT weight,percent,descriptor FROM rule WHERE version=$a AND criterion=$b AND level=$c", version, criterion, level);
                    if (existing.Count > 0)
                    {
                        Require(decimal.Parse(existing[0][0]) == w && decimal.Parse(existing[0][1]) == p && existing[0][2] == desc, "Use a new rubric version for changed rules.");
                        continue;
                    }

                    Require(Query(db, "SELECT id FROM submission WHERE version=$a LIMIT 1", version).Count == 0, "This rubric version is frozen.");
                    var weights = Query(db, "SELECT weight FROM rule WHERE version=$a AND criterion=$b", version, criterion);
                    Require(weights.All(row => decimal.Parse(row[0]) == w), "One weight per criterion.");
                    Sql(db, "INSERT INTO rule VALUES($a,$b,$c,$d,$e,$f)", version, criterion, weight, level, percent, desc);
                }
                else if (r is ["submission", var id, var student, var v])
                {
                    Require(Query(db, "SELECT version FROM rule WHERE version=$a LIMIT 1", v).Count > 0, "Unknown rubric version.");
                    InsertOnce(db, "submission", ["id", "student", "version"], [id, student, v]);
                }
                else if (r is ["score", var sid, var crit, var lvl, var feedback])
                {
                    Require(Query(db, "SELECT r.level FROM submission s JOIN rule r ON r.version=s.version WHERE s.id=$a AND r.criterion=$b AND r.level=$c", sid, crit, lvl).Count == 1, "Unknown submission, criterion or level.");
                    Sql(db, "INSERT INTO mark VALUES($a,$b,$c,$d) ON CONFLICT(submission,criterion) DO UPDATE SET level=excluded.level,feedback=excluded.feedback", sid, crit, lvl, feedback);
                }
                else
                    throw new FormatException("Unknown grading record.");
            }

        var roster = args.Length == 4 ? Query(db, "SELECT id,student,version FROM submission WHERE id=$a", args[3]) : Query(db, "SELECT id,student,version FROM submission ORDER BY student,id");
        Require(args.Length != 4 || roster.Count == 1, "Unknown submission.");
        tx.Commit();
        foreach (var s in roster)
        {
            var rows = Query(db, "SELECT r.criterion,r.weight,r.percent,r.level,r.descriptor,m.feedback FROM mark m JOIN rule r ON r.criterion=m.criterion AND r.level=m.level WHERE m.submission=$a AND r.version=$b ORDER BY r.criterion", s[0], s[2]);
            var targets = Query(db, "SELECT DISTINCT criterion FROM rule WHERE version=$a", s[2]);
            Console.WriteLine($"{s[1]} / {s[0]} / rubric {s[2]}");
            foreach (var r in rows)
                Console.WriteLine($"  {r[0]} — {r[3]}: {r[4]}. Revision: {r[5]}");
            if (rows.Count == targets.Count)
                Console.WriteLine($"  Weighted score {rows.Sum(r => decimal.Parse(r[1]) * decimal.Parse(r[2])) / rows.Sum(r => decimal.Parse(r[1])):0.00}%");
            else
                Console.WriteLine("  Missing: " + string.Join(", ", targets.Select(x => x[0]).Except(rows.Select(x => x[0]))));
        }
    }

    static List<string[]> Rows(string file, int width = 0, bool allowEmpty = false)
    {
        using var reader = file == "-" ? Console.In : File.OpenText(file);
        var rows = new List<string[]>();
        int line = 0;
        while (reader.ReadLine()is string s)
        {
            line++;
            if (string.IsNullOrWhiteSpace(s) || s.TrimStart().StartsWith("#"))
                continue;
            var cells = s.Split('\t', StringSplitOptions.TrimEntries);
            if ((width > 0 && cells.Length != width) || (!allowEmpty && cells.Any(string.IsNullOrEmpty)))
                throw new FormatException($"Invalid columns on line {line}.");
            rows.Add(cells);
        }

        if (rows.Count == 0)
            throw new FormatException("Empty input.");
        return rows;
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new FormatException(message);
    }

    static decimal Amount(string text)
    {
        var value = decimal.Parse(text, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint);
        Require(value >= 0, "Quantity must be nonnegative.");
        return value;
    }

    static decimal Positive(string text)
    {
        var value = Amount(text);
        Require(value > 0, "Quantity must be positive.");
        return value;
    }

    static SqliteTransaction? transaction;
    static SqliteConnection Open(string file)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, ForeignKeys = true }.ToString());
        connection.Open();
        return connection;
    }

    static SqliteTransaction Begin(SqliteConnection db) => transaction = db.BeginTransaction();
    static SqliteCommand Command(SqliteConnection db, string sql, string[] values)
    {
        var cmd = db.CreateCommand();
        if (transaction?.Connection != null)
            cmd.Transaction = transaction;
        cmd.CommandText = sql;
        for (int i = 0; i < values.Length; i++)
            cmd.Parameters.AddWithValue("$" + (char)(97 + i), values[i]);
        return cmd;
    }

    static void Sql(SqliteConnection db, string sql, params string[] values)
    {
        using var cmd = Command(db, sql, values);
        cmd.ExecuteNonQuery();
    }

    static List<string[]> Query(SqliteConnection db, string sql, params string[] values)
    {
        using var cmd = Command(db, sql, values);
        using var reader = cmd.ExecuteReader();
        var records = new List<string[]>();
        while (reader.Read())
            records.Add(Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "").ToArray());
        return records;
    }

    static void InsertOnce(SqliteConnection db, string table, string[] columns, string[] values)
    {
        var old = Query(db, $"SELECT {string.Join(",", columns)} FROM {table} WHERE {columns[0]}=$a", values[0]);
        if (old.Count != 0)
        {
            Require(old[0].SequenceEqual(values), "Conflicting record: " + values[0]);
            return;
        }

        var slots = Enumerable.Range(0, values.Length).Select(i => "$" + (char)(97 + i));
        Sql(db, $"INSERT INTO {table} ({string.Join(",", columns)}) VALUES ({string.Join(",", slots)})", values);
    }
}
