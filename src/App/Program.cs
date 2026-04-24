using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using S300CRE_to_SI.Source;


if (args.Length == 0)
{
    Console.WriteLine("Usage: S300CRE_to_SI.App <operation> [args] --database <name>");
    Console.WriteLine("Operations:");
    Console.WriteLine("  initialize                        Run initial mapping setup (01_Initial_Mappings). Runs once per client database.");
    Console.WriteLine("  teardown                          Drop all MAP schema objects created by initialize. Drops schema too if nothing else remains.");
    Console.WriteLine("  apply-mappings <path-to-xlsx>     Apply mappings from an ETL mapping document to the database.");
    Console.WriteLine("  generate-imports <output-folder>  Generate import .xlsx files from SQL scripts in 02_Import_Template_Definitions.");
    Console.WriteLine("  export-mappings <output-folder>   Export current mapping tables to an ETL Mapping Document .xlsx file.");
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

    case "teardown":
        RunTeardown(db, databaseName);
        break;

    case "export-mappings":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: export-mappings <output-folder>");
            break;
        }
        var mappingExporter = new MappingExporter(db);
        mappingExporter.Export(args[1], databaseName);
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

static void RunTeardown(DatabaseConnection db, string databaseName)
{
    // Check MAP schema exists
    using var checkCmd = new SqlCommand("SELECT COUNT(1) FROM sys.schemas WHERE name = 'MAP'", db.GetConnection());
    if ((int)checkCmd.ExecuteScalar()! == 0)
    {
        Console.WriteLine("MAP schema does not exist. Nothing to tear down.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"WARNING: This will drop all MAP schema objects created by initialize on [{databaseName}].");
    Console.Write("Type the database name to confirm: ");
    var confirmation = Console.ReadLine()?.Trim();

    if (!string.Equals(confirmation, databaseName, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Confirmation did not match. Aborting.");
        return;
    }

    var views = new[]
    {
        "[MAP].[T_MASTER_ACCOUNT]",
        "[MAP].[T_MASTER_EMPLOYEE]",
    };

    var tables = new[]
    {
        "[MAP].[T_TRANS_DATA_FOLDER]",
        "[MAP].[T_TRANS_JOB]",
        "[MAP].[T_TRANS_CUSTOMER]",
        "[MAP].[T_TRANS_VENDOR]",
        "[MAP].[T_TRANS_COST_CODE]",
        "[MAP].[T_TRANS_COST_TYPE]",
        "[MAP].[T_TRANS_BASEACCT]",
        "[MAP].[T_TRANS_ENTITY]",
        "[MAP].[T_TRANS_BATCH_COMBINE]",
        "[MAP].[T_TRANS_EMPLOYEE]",
        "[MAP].[T_STATE]",
        "[MAP].[T_1099_TYPE]",
        "[MAP].[T_TRANS_LOCATION]",
        "[MAP].[T_TRANS_DEPARTMENT]",
        "[MAP].[E_USEFUL_FIELDS]",
        "[MAP].[T_TRANS_CLASS]",
        "[MAP].[T_TRANS_ITEM]",
        "[MAP].[T_TRANS_WAREHOUSE]",
    };

    Console.WriteLine();

    foreach (var view in views)
    {
        using var cmd = new SqlCommand($"DROP VIEW IF EXISTS {view}", db.GetConnection());
        cmd.ExecuteNonQuery();
        Console.WriteLine($"  Dropped view: {view}");
    }

    foreach (var table in tables)
    {
        using var cmd = new SqlCommand($"DROP TABLE IF EXISTS {table}", db.GetConnection());
        cmd.ExecuteNonQuery();
        Console.WriteLine($"  Dropped table: {table}");
    }

    // Drop schema only if no other objects remain
    using var countCmd = new SqlCommand(
        "SELECT COUNT(1) FROM sys.objects WHERE schema_id = SCHEMA_ID('MAP')",
        db.GetConnection());
    var remaining = (int)countCmd.ExecuteScalar()!;

    if (remaining == 0)
    {
        using var dropSchema = new SqlCommand("DROP SCHEMA [MAP]", db.GetConnection());
        dropSchema.ExecuteNonQuery();
        Console.WriteLine("  Dropped schema: MAP");
    }
    else
    {
        Console.WriteLine($"  MAP schema retained ({remaining} manually created object(s) remain).");
    }

    Console.WriteLine("Teardown complete.");
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
