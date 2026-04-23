using Microsoft.Data.SqlClient;

namespace S300CRE_to_SI.Source;

public class ScriptRunner
{
    private readonly DatabaseConnection _db;

    public ScriptRunner(DatabaseConnection db)
    {
        _db = db;
    }

    /// <summary>
    /// Executes all .sql files in the given folder, ordered by filename.
    /// </summary>
    public void RunAll(string scriptsFolderPath)
    {
        var scripts = Directory.GetFiles(scriptsFolderPath, "*.sql")
                               .OrderBy(f => Path.GetFileName(f))
                               .ToList();

        if (scripts.Count == 0)
        {
            Console.WriteLine("No SQL scripts found.");
            return;
        }

        var conn = _db.GetConnection();

        foreach (var scriptPath in scripts)
        {
            var fileName = Path.GetFileName(scriptPath);
            Console.WriteLine($"Running: {fileName}");

            var sql = File.ReadAllText(scriptPath);

            // Split on GO statements (T-SQL batch separator)
            var batches = sql.Split(["\r\nGO", "\nGO", " GO"], StringSplitOptions.RemoveEmptyEntries);

            foreach (var batch in batches)
            {
                var trimmed = batch.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                using var cmd = new SqlCommand(trimmed, conn);
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine($"  Done: {fileName}");
        }

        Console.WriteLine("All scripts executed successfully.");
    }
}
