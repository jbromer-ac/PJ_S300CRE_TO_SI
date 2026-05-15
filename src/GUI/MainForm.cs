using System.Text;
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
    private TextBox _mappingFilePath = null!;
    private TextBox _outputFolderPath = null!;
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
        Size = new Size(740, 600);
        MinimumSize = new Size(620, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // database picker
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // operations group
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // log
        Controls.Add(outer);

        outer.Controls.Add(BuildDatabasePanel());
        outer.Controls.Add(BuildOperationsGroup());
        outer.Controls.Add(BuildLogGroup());
    }

    private Control BuildDatabasePanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 8),
        };

        panel.Controls.Add(new Label
        {
            Text = "Database:",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 5, 8, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        });

        _dbCombo = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        panel.Controls.Add(_dbCombo);

        _refreshBtn = new Button { Text = "↻ Refresh", AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
        _refreshBtn.Click += (_, _) => LoadDatabases();
        panel.Controls.Add(_refreshBtn);

        return panel;
    }

    private GroupBox BuildOperationsGroup()
    {
        var group = new GroupBox
        {
            Text = "Operations",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(10, 14, 10, 10),
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(2),
        };
        for (int i = 0; i < 5; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.Controls.Add(layout);

        // Row 0: Initialize + Teardown (no path needed)
        var row0 = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 0, 0, 10) };
        _initBtn = MakeBtn("Initialize", Color.FromArgb(0, 120, 212));
        _initBtn.Click += async (_, _) => await RunAsync(RunInitialize);
        _teardownBtn = MakeBtn("Teardown", Color.FromArgb(196, 43, 28));
        _teardownBtn.Click += async (_, _) => await RunAsync(RunTeardown);
        row0.Controls.AddRange(new Control[] { _initBtn, _teardownBtn });
        layout.Controls.Add(row0);

        // Row 1: Mapping file label
        layout.Controls.Add(new Label
        {
            Text = "Mapping File  (Apply Mappings):",
            AutoSize = true,
            Padding = new Padding(0, 2, 0, 2),
        });

        // Row 2: Mapping file picker + Apply Mappings button
        var row2 = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3 };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _mappingFilePath = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 6) };
        var browseFile = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(0, 3, 8, 6) };
        browseFile.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*", Title = "Select Mapping File" };
            if (dlg.ShowDialog() == DialogResult.OK) _mappingFilePath.Text = dlg.FileName;
        };
        _applyMappingsBtn = MakeBtn("Apply Mappings", Color.FromArgb(16, 124, 16));
        _applyMappingsBtn.Margin = new Padding(0, 2, 0, 6);
        _applyMappingsBtn.Click += async (_, _) => await RunAsync(RunApplyMappings);
        row2.Controls.AddRange(new Control[] { _mappingFilePath, browseFile, _applyMappingsBtn });
        layout.Controls.Add(row2);

        // Row 3: Output folder label
        layout.Controls.Add(new Label
        {
            Text = "Output Folder  (Export Mappings / Generate Imports):",
            AutoSize = true,
            Padding = new Padding(0, 2, 0, 2),
        });

        // Row 4: Output folder picker + Export Mappings + Generate Imports buttons
        var row4 = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4 };
        row4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row4.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row4.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row4.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _outputFolderPath = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 3) };
        var browseFolder = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(0, 3, 8, 3) };
        browseFolder.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Select Output Folder" };
            if (dlg.ShowDialog() == DialogResult.OK) _outputFolderPath.Text = dlg.SelectedPath;
        };
        _exportMappingsBtn = MakeBtn("Export Mappings", Color.FromArgb(16, 124, 16));
        _exportMappingsBtn.Click += async (_, _) => await RunAsync(RunExportMappings);
        _generateImportsBtn = MakeBtn("Generate Imports", Color.FromArgb(16, 124, 16));
        _generateImportsBtn.Click += async (_, _) => await RunAsync(RunGenerateImports);
        row4.Controls.AddRange(new Control[] { _outputFolderPath, browseFolder, _exportMappingsBtn, _generateImportsBtn });
        layout.Controls.Add(row4);

        return group;
    }

    private GroupBox BuildLogGroup()
    {
        var group = new GroupBox { Text = "Output", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.Controls.Add(layout);

        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(212, 212, 212),
            Font = new Font("Consolas", 9f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
        };
        layout.Controls.Add(_log);

        var btnRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 4, 0, 0),
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
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 0),
        Padding = new Padding(10, 4, 10, 4),
        BackColor = back,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
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
            while (rdr.Read())
                _dbCombo.Items.Add(rdr.GetString(0));
            if (_dbCombo.Items.Count > 0)
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

    private string? SelectedDb => InvokeRequired
        ? (string?)Invoke(() => _dbCombo.SelectedItem)
        : _dbCombo.SelectedItem as string;

    private string GetMappingFile() => InvokeRequired
        ? (string)Invoke(() => _mappingFilePath.Text.Trim())!
        : _mappingFilePath.Text.Trim();

    private string GetOutputFolder() => InvokeRequired
        ? (string)Invoke(() => _outputFolderPath.Text.Trim())!
        : _outputFolderPath.Text.Trim();

    private async Task RunAsync(Action operation)
    {
        SetButtons(false);
        // Redirect Console output so ScriptRunner/Exporter messages appear in the log
        var prevOut = Console.Out;
        Console.SetOut(new LogWriter(msg => Log(msg)));
        try { await Task.Run(operation); }
        finally
        {
            Console.SetOut(prevOut);
            SetButtons(true);
        }
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

    private void RunApplyMappings()
    {
        var db = SelectedDb;
        var path = GetMappingFile();
        if (db is null) { Log("No database selected.", Color.FromArgb(244, 71, 71)); return; }
        if (string.IsNullOrEmpty(path)) { Log("No mapping file selected.", Color.FromArgb(244, 71, 71)); return; }
        if (!File.Exists(path)) { Log($"File not found: {path}", Color.FromArgb(244, 71, 71)); return; }
        Log($"Applying mappings from: {path}");
        try
        {
            using var conn = OpenDb(db);
            new MappingApplier(conn).Apply(path);
            Log("Apply mappings complete.", Color.FromArgb(106, 153, 85));
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.FromArgb(244, 71, 71)); }
    }

    private void RunExportMappings()
    {
        var db = SelectedDb;
        var folder = GetOutputFolder();
        if (db is null) { Log("No database selected.", Color.FromArgb(244, 71, 71)); return; }
        if (string.IsNullOrEmpty(folder)) { Log("No output folder selected.", Color.FromArgb(244, 71, 71)); return; }
        Log($"Exporting mappings to: {folder}");
        try
        {
            using var conn = OpenDb(db);
            new MappingExporter(conn).Export(folder, db);
            Log("Export mappings complete.", Color.FromArgb(106, 153, 85));
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.FromArgb(244, 71, 71)); }
    }

    private void RunGenerateImports()
    {
        var db = SelectedDb;
        var folder = GetOutputFolder();
        if (db is null) { Log("No database selected.", Color.FromArgb(244, 71, 71)); return; }
        if (string.IsNullOrEmpty(folder)) { Log("No output folder selected.", Color.FromArgb(244, 71, 71)); return; }
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
        if (InvokeRequired) { Invoke(() => Log(message, color)); return; }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = color ?? Color.FromArgb(212, 212, 212);
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.ScrollToCaret();
    }

    // Pipes Console.WriteLine output from ScriptRunner/Exporters into the log panel
    private sealed class LogWriter(Action<string> log) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => log(value ?? string.Empty);
        public override void Write(string? value) { if (!string.IsNullOrEmpty(value)) log(value); }
    }
}
