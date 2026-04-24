using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using S300CRE_to_SI.Source;


if (args.Length == 0)
{
    Console.WriteLine("Usage: S300CRE_to_SI.App <operation> [args] --database <name>");
    Console.WriteLine("Operations:");
    Console.WriteLine("  initialize                        Run initial mapping setup (01_Initial_Mappings). Runs once per client database.");
    Console.WriteLine("  apply-mappings <path-to-xlsx>     Apply mappings from an ETL mapping document to the database.");
    Console.WriteLine("  generate-imports <output-folder>  Generate import .xlsx files from SQL scripts in 02_Import_Template_Definitions.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --database <name>                 (Required) The database to connect to.");
    return;
}

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .Build();

var connectionString = config["ConnectionString"]
    ?? throw new InvalidOperationException("ConnectionString is missing from appsettings.json.");

var dbFlagIndex = Array.IndexOf(args, "--database");
if (dbFlagIndex < 0 || dbFlagIndex + 1 >= args.Length)
{
    Console.WriteLine("ERROR: --database <name> is required.");
    return;
}
var databaseName = args[dbFlagIndex + 1];

var scriptsFolderPath = config["ScriptsFolderPath"]
    ?? throw new InvalidOperationException("ScriptsFolderPath is missing from appsettings.json.");

var userId = config["DatabaseUserId"]
    ?? throw new InvalidOperationException("DatabaseUserId is missing from user secrets.");

var password = config["DatabasePassword"]
    ?? throw new InvalidOperationException("DatabasePassword is missing from user secrets.");

if (!Path.IsPathRooted(scriptsFolderPath))
    scriptsFolderPath = Path.Combine(AppContext.BaseDirectory, scriptsFolderPath);

connectionString += $"User Id={userId};Password={password};";

Console.WriteLine($"Connecting to database: {databaseName}...");

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

    case "apply-mappings":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: apply-mappings <path-to-xlsx>");
            break;
        }
        var applier = new MappingApplier(db);
        applier.Apply(args[1]);
        break;

    case "generate-imports":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: generate-imports <output-folder>");
            break;
        }
        var exporter = new ImportExporter(db);
        exporter.Export(scriptsFolderPath, args[1], databaseName);
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
