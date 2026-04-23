using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using S300CRE_to_SI.Source;

if (args.Length == 0)
{
    Console.WriteLine("Usage: S300CRE_to_SI.App <operation>");
    Console.WriteLine("Operations:");
    Console.WriteLine("  initialize    Run initial mapping setup (01_Initial_Mappings). Runs once per client database.");
    return;
}

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .Build();

var connectionString = config["ConnectionString"]
    ?? throw new InvalidOperationException("ConnectionString is missing from appsettings.json.");

var databaseName = config["DatabaseName"]
    ?? throw new InvalidOperationException("DatabaseName is missing from appsettings.json.");

var scriptsFolderPath = config["ScriptsFolderPath"]
    ?? throw new InvalidOperationException("ScriptsFolderPath is missing from appsettings.json.");

var userId = config["DatabaseUserId"]
    ?? throw new InvalidOperationException("DatabaseUserId is missing from user secrets.");

var password = config["DatabasePassword"]
    ?? throw new InvalidOperationException("DatabasePassword is missing from user secrets.");

if (!Path.IsPathRooted(scriptsFolderPath))
    scriptsFolderPath = Path.Combine(AppContext.BaseDirectory, scriptsFolderPath);

connectionString += $"User Id={userId};Password={password};";

Console.WriteLine("Connecting to database...");

using var db = new DatabaseConnection(connectionString);

try
{
    db.GetConnection(databaseName);
    Console.WriteLine("Connection successful.");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to connect: {ex.Message}");
    return;
}

var operation = args[0].ToLowerInvariant();
var runner = new ScriptRunner(db);

switch (operation)
{
    case "initialize":
        RunInitialize(db, runner, scriptsFolderPath);
        break;

    default:
        Console.WriteLine($"Unknown operation: '{args[0]}'");
        break;
}

static void RunInitialize(DatabaseConnection db, ScriptRunner runner, string scriptsFolderPath)
{
    // Safety check: abort if MAP schema already exists
    const string checkSql = "SELECT COUNT(1) FROM sys.schemas WHERE name = 'MAP'";
    using var cmd = new SqlCommand(checkSql, db.GetConnection());
    var exists = (int)cmd.ExecuteScalar()! > 0;

    if (exists)
    {
        Console.WriteLine();
        Console.WriteLine("WARNING: The MAP schema already exists in this database.");
        Console.WriteLine("Initialize has already been run. Aborting to protect existing mapping data.");
        Console.WriteLine("If you need to re-initialize, drop the MAP schema manually first.");
        return;
    }

    var initPath = Path.Combine(scriptsFolderPath, "01_Initial_Mappings");

    if (!Directory.Exists(initPath))
    {
        Console.WriteLine($"ERROR: Script folder not found: {initPath}");
        return;
    }

    Console.WriteLine("Running initial mapping setup...");
    runner.RunAll(initPath);
}
