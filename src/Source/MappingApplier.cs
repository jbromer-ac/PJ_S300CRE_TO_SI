using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using System.Data;

namespace S300CRE_to_SI.Source;

public class MappingApplier
{
    private readonly DatabaseConnection _db;

    public MappingApplier(DatabaseConnection db)
    {
        _db = db;
    }

    public void Apply(string xlsxPath)
    {
        if (!File.Exists(xlsxPath))
        {
            Console.WriteLine($"ERROR: File not found: {xlsxPath}");
            return;
        }

        Console.WriteLine($"Opening: {Path.GetFileName(xlsxPath)}");

        using var workbook = new XLWorkbook(xlsxPath);

        var handlers = new Dictionary<string, Action<IXLWorksheet>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Controls"]            = ProcessControls,
            ["Entity"]              = ProcessEntity,
            ["Job ID"]              = ProcessJobId,
            ["GL Account"]          = ProcessGLAccount,
            ["Location ID"]         = ProcessLocation,
            ["Department ID"]       = ProcessDepartment,
            ["Class ID"]            = ProcessClass,
            ["Vendor ID"]           = ProcessVendor,
            ["Customer ID"]         = ProcessCustomer,
            ["Cost Code ID"]        = ProcessCostCode,
            ["Cost Type ID"]        = ProcessCostType,
            ["Employee ID"]         = ProcessEmployee,
            ["Inventory Item ID"]   = ProcessInventoryItem,
            ["Warehouse ID"]        = ProcessWarehouse,
        };

        foreach (var sheet in workbook.Worksheets)
        {
            Console.WriteLine($"\n[{sheet.Name}]");

            if (handlers.TryGetValue(sheet.Name, out var handler))
                handler(sheet);
            else
                Console.WriteLine("  Skipping: no handler defined for this sheet.");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string Str(IXLWorksheet ws, int row, int col) =>
        ws.Cell(row, col).GetString().Trim();

    private static int ToInt(string value) =>
        int.TryParse(value, out var i) ? i : 0;

    private static bool IsHidden(IXLWorksheet ws, int row)
    {
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        return lastCol > 0 &&
               ws.Cell(row, lastCol).GetString().Trim()
                 .Equals("Hide", StringComparison.OrdinalIgnoreCase);
    }

    /// Executes a single SQL statement immediately.
    private void Exec(string sql, params SqlParameter[] parameters)
    {
        using var cmd = new SqlCommand(sql, _db.GetConnection());
        cmd.Parameters.AddRange(parameters);
        cmd.ExecuteNonQuery();
    }

    /// Used only for small/special sheets (Controls). Executes one UPDATE per row.
    private void ProcessRows(
        IXLWorksheet ws,
        int dataStartRow,
        Func<int, (bool skip, Action? execute)> rowProcessor)
    {
        int executed = 0, skipped = 0, errors = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (int row = dataStartRow; row <= lastRow; row++)
        {
            if (ws.Row(row).IsEmpty()) continue;

            try
            {
                var (skip, execute) = rowProcessor(row);
                if (skip || execute == null) { skipped++; continue; }
                execute();
                executed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR row {row}: {ex.Message}");
                errors++;
            }
        }

        Console.WriteLine($"  {executed} executed, {skipped} skipped, {errors} errors.");
    }

    /// Reads a worksheet into a DataTable then bulk-updates the target table in 3 round-trips:
    /// CREATE temp, SqlBulkCopy, UPDATE FROM join. Columns: (excelCol 1-based, dbColumnName, isKey).
    /// The first key column is used for the "skip if empty" check.
    /// valueTransform: optional (excelCol, rawValue) => transformedValue
    private void BulkProcessSheet(
        IXLWorksheet ws,
        int dataStartRow,
        string targetTable,
        (int ExcelCol, string DbColumn, bool IsKey)[] columns,
        Func<int, string, string>? valueTransform = null)
    {
        var dt = new DataTable();
        foreach (var (_, dbCol, _) in columns)
            dt.Columns.Add(dbCol, typeof(string));

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        int skipped = 0;
        var firstKey = columns.First(c => c.IsKey);

        for (int row = dataStartRow; row <= lastRow; row++)
        {
            if (ws.Row(row).IsEmpty()) continue;
            if (IsHidden(ws, row)) { skipped++; continue; }
            if (string.IsNullOrEmpty(Str(ws, row, firstKey.ExcelCol))) { skipped++; continue; }

            var dr = dt.NewRow();
            foreach (var (excelCol, dbCol, _) in columns)
            {
                var val = Str(ws, row, excelCol);
                dr[dbCol] = valueTransform != null ? valueTransform(excelCol, val) : val;
            }
            dt.Rows.Add(dr);
        }

        Console.Write($"  {dt.Rows.Count} rows read, {skipped} skipped — updating... ");

        if (dt.Rows.Count == 0)
        {
            Console.WriteLine("done.");
            return;
        }

        var keyColumns   = columns.Where(c =>  c.IsKey).Select(c => c.DbColumn).ToArray();
        var valueColumns = columns.Where(c => !c.IsKey).Select(c => c.DbColumn).ToArray();

        int updated = BulkUpdate(targetTable, dt, keyColumns, valueColumns);
        Console.WriteLine($"{updated} rows updated.");
    }

    private int BulkUpdate(string targetTable, DataTable data, string[] keyColumns, string[] updateColumns)
    {
        var conn    = _db.GetConnection();
        var tmpName = $"#bu{Guid.NewGuid():N}";

        var colDefs = string.Join(", ",
            data.Columns.Cast<DataColumn>().Select(c => $"[{c.ColumnName}] NVARCHAR(500) NULL"));

        using (var cmd = new SqlCommand($"CREATE TABLE {tmpName} ({colDefs})", conn))
            cmd.ExecuteNonQuery();

        try
        {
            using (var bc = new SqlBulkCopy(conn))
            {
                bc.DestinationTableName = tmpName;
                bc.WriteToServer(data);
            }

            var set = string.Join(", ",    updateColumns.Select(c => $"t.[{c}] = s.[{c}]"));
            var on  = string.Join(" AND ", keyColumns.Select(c => $"t.[{c}] = s.[{c}]"));

            using var update = new SqlCommand(
                $"UPDATE t SET {set} FROM {targetTable} AS t INNER JOIN {tmpName} AS s ON {on}",
                conn);
            return update.ExecuteNonQuery();
        }
        finally
        {
            using var drop = new SqlCommand($"DROP TABLE IF EXISTS {tmpName}", conn);
            drop.ExecuteNonQuery();
        }
    }

    // -------------------------------------------------------------------------
    // Sheet Handlers
    // -------------------------------------------------------------------------

    private void ProcessControls(IXLWorksheet ws)
    {
        // Header: row 5 | Data: row 6+
        // Col A: FIELD_NAME | Col B: FIELD_VALUE
        ProcessRows(ws, dataStartRow: 6, row =>
        {
            var fieldName = Str(ws, row, 1);
            if (string.IsNullOrEmpty(fieldName)) return (skip: true, execute: null);

            string fieldValue;
            var cell = ws.Cell(row, 2);
            if (cell.DataType == XLDataType.DateTime)
            {
                fieldValue = cell.GetDateTime().ToString("yyyy-MM-dd");
            }
            else if (cell.DataType == XLDataType.Number &&
                     (fieldName.Contains("_END", StringComparison.OrdinalIgnoreCase) ||
                      fieldName.Contains("_START", StringComparison.OrdinalIgnoreCase) ||
                      fieldName.Contains("_STOP", StringComparison.OrdinalIgnoreCase)))
            {
                fieldValue = DateTime.FromOADate(cell.GetDouble()).ToString("yyyy-MM-dd");
            }
            else
            {
                fieldValue = Str(ws, row, 2);
            }

            return (skip: false, execute: () =>
                Exec("UPDATE [MAP].[E_USEFUL_FIELDS] SET FIELD_VALUE = @v WHERE FIELD_NAME = @n",
                    new SqlParameter("@v", fieldValue),
                    new SqlParameter("@n", fieldName)));
        });
    }

    private void ProcessEntity(IXLWorksheet ws)
    {
        // A: DATA_FOLDER_ID(key) | C: NEW_ENTITY_ID | D: PKG_BASE | E: PKG_CONSTR_SUM
        // F: PKG_CONSTR_DET | (G: PKG_PAYROLL — not in table, skipped) | H: PKG_NPC_COMMIT | I: PKG_PC_COMMIT
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_ENTITY]",
        [
            (1, "DATA_FOLDER_ID",  true),
            (3, "NEW_ENTITY_ID",   false),
            (4, "PKG_BASE",        false),
            (5, "PKG_CONSTR_SUM",  false),
            (6, "PKG_CONSTR_DET",  false),
            (8, "PKG_NPC_COMMIT",  false),
            (9, "PKG_PC_COMMIT",   false),
        ]);
    }

    private void ProcessJobId(IXLWorksheet ws)
    {
        // Pre-step: reset all INCLUDE_JOB to 0 before applying template selections
        Console.WriteLine("  Resetting INCLUDE_JOB = 0 for all jobs...");
        Exec("UPDATE [MAP].[T_TRANS_JOB] SET INCLUDE_JOB = 0");

        // A: DATA_FOLDER_ID | B: LEGACY_JOB_ID | C: LEGACY_EXTRA_ID | D: Description
        // E: Include? (Yes→1, No→0) | F: NEW_JOB_ID | G: NEW_ENTITY_ID | H: NEW_DEPARTMENT_ID
        // I: NEW_CLASS_ID | J: NEW_CUSTOMER_ID | K: NEW_PM_ID
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_JOB]",
        [
            (1,  "DATA_FOLDER_ID",    true),
            (2,  "LEGACY_JOB_ID",     true),
            (3,  "LEGACY_EXTRA_ID",   true),
            (5,  "INCLUDE_JOB",       false),
            (6,  "NEW_JOB_ID",        false),
            (7,  "NEW_ENTITY_ID",     false),
            (8,  "NEW_DEPARTMENT_ID", false),
            (9,  "NEW_CLASS_ID",      false),
            (10, "NEW_CUSTOMER_ID",   false),
            (11, "NEW_PM_ID",         false),
        ],
        valueTransform: (col, val) => col == 5
            ? (val.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? "1" : "0")
            : val);
    }

    private void ProcessGLAccount(IXLWorksheet ws)
    {
        // Legacy (cols 1-9): DATA_FOLDER_ID, Legacy GL Account, Note, FIN_STMT, BALANCE, CLOSEABLE, ACCT_TYPE, ACCT_MATCH_TYPE, REQUIRED — read-only reference
        // New    (cols 10-12): New_Base_Account, CMO, CATEGORY — written back
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_BASEACCT]",
        [
            (1,  "Data_Folder_Id",      true),
            (2,  "Legacy_Base_Account", true),
            (10, "New_Base_Account",    false),
            (11, "CMO",                 false),
            (12, "CATEGORY",            false),
        ]);
    }

    private void ProcessLocation(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_LOCATION]",
        [
            (1, "DATA_FOLDER_ID",     true),
            (2, "LEGACY_LOCATION_ID", true),
            (4, "NEW_LOCATION_ID",    false),
        ]);
    }

    private void ProcessDepartment(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_DEPARTMENT]",
        [
            (1, "DATA_FOLDER_ID",       true),
            (2, "LEGACY_DEPARTMENT_ID", true),
            (4, "NEW_DEPARTMENT_ID",    false),
        ]);
    }

    private void ProcessClass(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_CLASS]",
        [
            (1, "DATA_FOLDER_ID",  true),
            (2, "LEGACY_CLASS_ID", true),
            (4, "NEW_CLASS_ID",    false),
        ]);
    }

    private void ProcessVendor(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_VENDOR]",
        [
            (1, "DATA_FOLDER_ID",   true),
            (2, "LEGACY_VENDOR_ID", true),
            (5, "NEW_VENDOR_ID",    false),
        ]);
    }

    private void ProcessCustomer(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_CUSTOMER]",
        [
            (1, "DATA_FOLDER_ID",     true),
            (2, "LEGACY_CUSTOMER_ID", true),
            (5, "NEW_CUSTOMER_ID",    false),
        ]);
    }

    private void ProcessCostCode(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_COST_CODE]",
        [
            (1, "DATA_FOLDER_ID",      true),
            (2, "LEGACY_COST_CODE_ID", true),
            (4, "NEW_COST_CODE_ID",    false),
            (5, "NEW_ITEM_ID",         false),
        ]);
    }

    private void ProcessCostType(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_COST_TYPE]",
        [
            (1, "DATA_FOLDER_ID",         true),
            (2, "LEGACY_COST_TYPE_ID",    true),
            (4, "NEW_COST_TYPE_ID",       false),
            (5, "NEW_ITEM_ID",            false),
            (6, "NEW_INTACCT_GL_ACCOUNT", false),
        ]);
    }

    private void ProcessEmployee(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_EMPLOYEE]",
        [
            (1, "DATA_FOLDER_ID",     true),
            (2, "LEGACY_EMPLOYEE_ID", true),
            (4, "NEW_EMPLOYEE_ID",    false),
        ]);
    }

    private void ProcessInventoryItem(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_ITEM]",
        [
            (1, "DATA_FOLDER_ID",  true),
            (2, "LEGACY_ITEM_ID",  true),
            (4, "NEW_ITEM_ID",     false),
        ]);
    }

    private void ProcessWarehouse(IXLWorksheet ws)
    {
        BulkProcessSheet(ws, dataStartRow: 4, "[MAP].[T_TRANS_WAREHOUSE]",
        [
            (1, "DATA_FOLDER_ID",      true),
            (2, "LEGACY_WAREHOUSE_ID", true),
            (4, "NEW_WAREHOUSE_ID",    false),
        ]);
    }
}
