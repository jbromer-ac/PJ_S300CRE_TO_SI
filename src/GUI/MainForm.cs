using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using S300CRE_to_SI.Source;

namespace S300CRE_to_SI.GUI;

public class MainForm : Form
{
    private readonly string _connStrBase;
    private readonly string _scriptsFolderPath;
    private readonly string _userId;
    private readonly string _password;

    private ComboBox _dbCombo = null!;
    private Button _refreshBtn = null!;

    private Button _initBtn = null!;
    private Button _teardownBtn = null!;
    private Button _applyMappingsBtn = null!;
    private Button _exportMappingsBtn = null!;
    private Button _generateImportsBtn = null!;
    private RichTextBox _log = null!;

    public MainForm()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<MainForm>()
            .Build();

        _connStrBase = config["ConnectionString"]
            ?? throw new InvalidOperationException("ConnectionString missing from appsettings.json.");
        _scriptsFolderPath = config["ScriptsFolderPath"]
            ?? throw new InvalidOperationException("ScriptsFolderPath missing from appsettings.json.");
        _userId = config["DatabaseUserId"]
            ?? throw new InvalidOperationException("DatabaseUserId missing from user secrets.");
        _password = config["DatabasePassword"]
            ?? throw new InvalidOperationException("DatabasePassword missing from user secrets.");

        if (!Path.IsPathRooted(_scriptsFolderPath))
            _scriptsFolderPath = Path.Combine(AppContext.BaseDirectory, _scriptsFolderPath);

        BuildUI();
        LoadDatabases();
    }

    private string FullConnStr => _connStrBase + $"User Id={_userId};Password={_password};";

    private DatabaseConnection OpenDb(string databaseName)
    {
        var db = new DatabaseConnection(FullConnStr);
        db.GetConnection(databaseName);
        return db;
    }

    // -------------------------------------------------------------------------
    // UI Construction
    // -------------------------------------------------------------------------

    private void BuildUI()
    {
        Text = "S300 CRE → Sage Intacct Migration Tool";
        Size = new Size(620, 500);
        MinimumSize = new Size(560, 440);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(245, 246, 247);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // header
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // database row
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // buttons
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // log
        Controls.Add(outer);

        outer.Controls.Add(BuildHeader());
        outer.Controls.Add(BuildDatabasePanel());
        outer.Controls.Add(BuildButtonPanel());
        outer.Controls.Add(BuildLogGroup());
    }

    private static Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(0, 56, 117),
            Padding = new Padding(14, 10, 14, 10),
            Height = 44,
        };
        header.Controls.Add(new Label
        {
            Text = "S300 CRE  →  Sage Intacct Migration Tool",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        return header;
    }

    private Control BuildDatabasePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(235, 237, 240),
            Padding = new Padding(14, 8, 14, 8),
            Height = 42,
        };

        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
        };

        flow.Controls.Add(new Label
        {
            Text = "Database:",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 4, 8, 0),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        });

        _dbCombo = new ComboBox
        {
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 2, 6, 0),
        };
        flow.Controls.Add(_dbCombo);

        _refreshBtn = new Button
        {
            Text = "↻",
            Width = 28,
            Height = 23,
            Margin = new Padding(0, 2, 0, 0),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderColor = Color.FromArgb(180, 180, 180) },
        };
        _refreshBtn.Click += (_, _) => LoadDatabases();
        flow.Controls.Add(_refreshBtn);

        panel.Controls.Add(flow);
        return panel;
    }

    private Control BuildButtonPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(245, 246, 247),
            Padding = new Padding(14, 12, 14, 12),
            AutoSize = true,
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Row 0: Create Default Mappings | Teardown Mappings
        var row0 = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 0, 0, 8), BackColor = Color.Transparent };
        _initBtn = MakeBtn("Create Default Mappings", Color.FromArgb(0, 99, 177));
        _initBtn.Click += async (_, _) => await RunAsync(RunInitialize);
        _teardownBtn = MakeBtn("Teardown Mappings", Color.FromArgb(168, 0, 0));
        _teardownBtn.Click += async (_, _) => await RunAsync(RunTeardown);
        row0.Controls.AddRange(new Control[] { _initBtn, _teardownBtn });
        layout.Controls.Add(row0);

        // Row 1: Import Mappings | Export Mappings | Generate Imports
        var row1 = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent };
        _applyMappingsBtn = MakeBtn("Import Mappings", Color.FromArgb(16, 110, 16));
        _applyMappingsBtn.Click += async (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*", Title = "Select Mapping File" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var path = dlg.FileName;
            await RunAsync(() => RunApplyMappings(path));
        };
        _exportMappingsBtn = MakeBtn("Export Mappings", Color.FromArgb(16, 110, 16));
        _exportMappingsBtn.Click += async (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Select output folder for Export Mappings" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var folder = dlg.SelectedPath;
            await RunAsync(() => RunExportMappings(folder));
        };
        _generateImportsBtn = MakeBtn("Generate Imports", Color.FromArgb(16, 110, 16));
        _generateImportsBtn.Click += async (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Select output folder for Generate Imports" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var folder = dlg.SelectedPath;
            await RunAsync(() => RunGenerateImports(folder));
        };
        row1.Controls.AddRange(new Control[] { _applyMappingsBtn, _exportMappingsBtn, _generateImportsBtn });
        layout.Controls.Add(row1);

        panel.Controls.Add(layout);
        return panel;
    }

    private GroupBox BuildLogGroup()
    {
        var group = new GroupBox
        {
            Text = "Output",
            Dock = DockStyle.Fill,
            Margin = new Padding(10, 0, 10, 10),
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.Controls.Add(layout);

        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(22, 22, 22),
            ForeColor = Color.FromArgb(204, 204, 204),
            Font = new Font("Consolas", 8.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
        };
        layout.Controls.Add(_log);

        var btnRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 3, 0, 0),
        };
        var clearBtn = new Button { Text = "Clear", AutoSize = true };
        clearBtn.Click += (_, _) => _log.Clear();
        btnRow.Controls.Add(clearBtn);
        layout.Controls.Add(btnRow);

        return group;
    }

    private static Button MakeBtn(string text, Color back) => new Button
    {
        Text = text,
        Width = 158,
        Height = 30,
        Margin = new Padding(0, 0, 8, 0),
        BackColor = back,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        Font = new Font("Segoe UI", 9f),
    };

    // -------------------------------------------------------------------------
    // Database List
    // -------------------------------------------------------------------------

    private void LoadDatabases()
    {
        _dbCombo.Items.Clear();
        _refreshBtn.Enabled = false;
        try
        {
            using var conn = new SqlConnection(FullConnStr + "Initial Catalog=master;");
            conn.Open();
            using var cmd = new SqlCommand(
                "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' ORDER BY name", conn);
            using var rdr = cmd.ExecuteReader();
            _dbCombo.Items.Add("(Please select a Database)");
            while (rdr.Read())
                _dbCombo.Items.Add(rdr.GetString(0));
            _dbCombo.SelectedIndex = 0;
            Log("Databases loaded.", Color.FromArgb(106, 153, 85));
        }
        catch (Exception ex)
        {
            Log($"Failed to load databases: {ex.Message}", Color.FromArgb(244, 71, 71));
        }
        finally
        {
            _refreshBtn.Enabled = true;
        }
    }

    // -------------------------------------------------------------------------
    // Operation Runner
    // -------------------------------------------------------------------------

    private string? SelectedDb
    {
        get
        {
            var val = InvokeRequired
                ? (string?)Invoke(() => _dbCombo.SelectedItem)
                : _dbCombo.SelectedItem as string;
            return val == "(Please select a Database)" ? null : val;
        }
    }



    private async Task RunAsync(Action operation)
    {
        SetButtons(false);
        try { await Task.Run(operation); }
        finally { SetButtons(true); }
    }

    private void SetButtons(bool enabled)
    {
        if (InvokeRequired) { Invoke(() => SetButtons(enabled)); return; }
        _initBtn.Enabled = enabled;
        _teardownBtn.Enabled = enabled;
        _applyMappingsBtn.Enabled = enabled;
        _exportMappingsBtn.Enabled = enabled;
        _generateImportsBtn.Enabled = enabled;
        _refreshBtn.Enabled = enabled;
    }

    // -------------------------------------------------------------------------
    // Operations
    // -------------------------------------------------------------------------

    private void RunInitialize()
    {
        var db = SelectedDb;
        if (db is null) { Log("No database selected.", Color.FromArgb(244, 71, 71)); return; }
        Log($"Initializing [{db}]...");
        try
        {
            using var conn = OpenDb(db);
            using var check = new SqlCommand(
                "SELECT COUNT(1) FROM sys.schemas WHERE name = 'MAP'", conn.GetConnection());
            if ((int)check.ExecuteScalar()! > 0)
            {
                Log("MAP schema already exists. Initialize has already been run. Aborting.", Color.FromArgb(244, 71, 71));
                return;
            }
            var initPath = Path.Combine(_scriptsFolderPath, "01_Initial_Mappings");
            if (!Directory.Exists(initPath))
            {
                Log($"Script folder not found: {initPath}", Color.FromArgb(244, 71, 71));
                return;
            }
            new ScriptRunner(conn).RunAll(initPath);
            Log("Initialize complete.", Color.FromArgb(106, 153, 85));
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.FromArgb(244, 71, 71)); }
    }

    private void RunTeardown()
    {
        var db = SelectedDb;
        if (db is null) { Log("No database selected.", Color.FromArgb(244, 71, 71)); return; }

        bool confirmed = false;
        Invoke(() =>
        {
            using var dlg = new TeardownConfirmDialog(db);
            confirmed = dlg.ShowDialog(this) == DialogResult.OK;
        });
        if (!confirmed) { Log("Teardown cancelled."); return; }

        Log($"Tearing down [{db}]...");
        try
        {
            using var conn = OpenDb(db);
            using var check = new SqlCommand(
                "SELECT COUNT(1) FROM sys.schemas WHERE name = 'MAP'", conn.GetConnection());
            if ((int)check.ExecuteScalar()! == 0)
            {
                Log("MAP schema does not exist. Nothing to tear down.");
                return;
            }

            foreach (var v in new[] { "[MAP].[T_MASTER_ACCOUNT]", "[MAP].[T_MASTER_EMPLOYEE]" })
            {
                using var cmd = new SqlCommand($"DROP VIEW IF EXISTS {v}", conn.GetConnection());
                cmd.ExecuteNonQuery();
                Log($"  Dropped view: {v}");
            }

            foreach (var t in new[]
            {
                "[MAP].[T_TRANS_DATA_FOLDER]", "[MAP].[T_TRANS_JOB]",    "[MAP].[T_TRANS_CUSTOMER]",
                "[MAP].[T_TRANS_VENDOR]",      "[MAP].[T_TRANS_COST_CODE]", "[MAP].[T_TRANS_COST_TYPE]",
                "[MAP].[T_TRANS_BASEACCT]",    "[MAP].[T_TRANS_ENTITY]", "[MAP].[T_TRANS_BATCH_COMBINE]",
                "[MAP].[T_TRANS_EMPLOYEE]",    "[MAP].[T_STATE]",        "[MAP].[T_1099_TYPE]",
                "[MAP].[T_TRANS_LOCATION]",    "[MAP].[T_TRANS_DEPARTMENT]", "[MAP].[E_USEFUL_FIELDS]",
                "[MAP].[T_TRANS_CLASS]",       "[MAP].[T_TRANS_ITEM]",   "[MAP].[T_TRANS_WAREHOUSE]",
            })
            {
                using var cmd = new SqlCommand($"DROP TABLE IF EXISTS {t}", conn.GetConnection());
                cmd.ExecuteNonQuery();
                Log($"  Dropped table: {t}");
            }

            using var countCmd = new SqlCommand(
                "SELECT COUNT(1) FROM sys.objects WHERE schema_id = SCHEMA_ID('MAP')", conn.GetConnection());
            var remaining = (int)countCmd.ExecuteScalar()!;
            if (remaining == 0)
            {
                using var drop = new SqlCommand("DROP SCHEMA [MAP]", conn.GetConnection());
                drop.ExecuteNonQuery();
                Log("  Dropped schema: MAP");
            }
            else
            {
                Log($"  MAP schema retained ({remaining} manually created object(s) remain).");
            }

            Log("Teardown complete.", Color.FromArgb(106, 153, 85));
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.FromArgb(244, 71, 71)); }
    }

    private void RunApplyMappings(string path)
    {
        var db = SelectedDb;
        if (db is null) { Log("No database selected.", Color.FromArgb(244, 71, 71)); return; }
        Log($"Applying mappings from: {path}");
        try
        {
            using var conn = OpenDb(db);
            new MappingApplier(conn).Apply(path);
            Log("Import mappings complete.", Color.FromArgb(106, 153, 85));
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.FromArgb(244, 71, 71)); }
    }

    private void RunExportMappings(string folder)
    {
        var db = SelectedDb;
        if (db is null) { Log("No database selected.", Color.FromArgb(244, 71, 71)); return; }
        Log($"Exporting mappings to: {folder}");
        try
        {
            using var conn = OpenDb(db);
            new MappingExporter(conn).Export(folder, db);
            Log("Export mappings complete.", Color.FromArgb(106, 153, 85));
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.FromArgb(244, 71, 71)); }
    }

    private void RunGenerateImports(string folder)
    {
        var db = SelectedDb;
        if (db is null) { Log("No database selected.", Color.FromArgb(244, 71, 71)); return; }
        Log($"Generating imports to: {folder}");
        try
        {
            using var conn = OpenDb(db);
            new ImportExporter(conn).Export(_scriptsFolderPath, folder, db);
            Log("Generate imports complete.", Color.FromArgb(106, 153, 85));
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.FromArgb(244, 71, 71)); }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void Log(string message, Color? color = null)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(message, color)); return; }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = color ?? Color.FromArgb(212, 212, 212);
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.ScrollToCaret();
    }

}
