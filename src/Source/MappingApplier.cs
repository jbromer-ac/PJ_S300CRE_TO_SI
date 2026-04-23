using ClosedXML.Excel;
using Microsoft.Data.SqlClient;

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

    /// <summary>
    /// Returns true if the hide/show column (last column) equals "Hide".
    /// </summary>
    private static bool IsHidden(IXLWorksheet ws, int row)
    {
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        return lastCol > 0 &&
               ws.Cell(row, lastCol).GetString().Trim()
                 .Equals("Hide", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Executes a parameterized SQL statement against the open connection.
    /// </summary>
    private void Exec(string sql, params SqlParameter[] parameters)
    {
        using var cmd = new SqlCommand(sql, _db.GetConnection());
        cmd.Parameters.AddRange(parameters);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Iterates data rows on a sheet, calling rowProcessor for each non-empty row.
    /// rowProcessor returns (skip, execute) — if skip is true or execute is null,
    /// the row is counted as skipped. Otherwise execute() is called.
    /// </summary>
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

                if (skip || execute == null)
                {
                    skipped++;
                    continue;
                }

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

    // -------------------------------------------------------------------------
    // Sheet Handlers
    // -------------------------------------------------------------------------

    private void ProcessControls(IXLWorksheet ws)
    {
        // Header: row 5 | Data: row 6+
        // Col A: FIELD_NAME | Col B: FIELD_VALUE
        // No hide/show column on this sheet.
        ProcessRows(ws, dataStartRow: 6, row =>
        {
            var fieldName = Str(ws, row, 1);
            if (string.IsNullOrEmpty(fieldName)) return (skip: true, execute: null);

            // Values for date fields are stored as Excel date serials.
            // ClosedXML may return them as DateTime or as a number depending on cell format.
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
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | C: NEW_ENTITY_ID
        // D: PKG_BASE | E: PKG_CONSTR_SUM | F: PKG_CONSTR_DET
        // G: PKG_PAYROLL (not in table — skipped)
        // H: PKG_NPC_COMMIT | I: PKG_PC_COMMIT
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var newEntityId   = Str(ws, row, 3);
            var pkgBase       = ToInt(Str(ws, row, 4));
            var pkgConstrSum  = ToInt(Str(ws, row, 5));
            var pkgConstrDet  = ToInt(Str(ws, row, 6));
            var pkgNpcCommit  = ToInt(Str(ws, row, 8));
            var pkgPcCommit   = ToInt(Str(ws, row, 9));

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_ENTITY]
                       SET NEW_ENTITY_ID    = @newEntityId,
                           PKG_BASE         = @pkgBase,
                           PKG_CONSTR_SUM   = @pkgConstrSum,
                           PKG_CONSTR_DET   = @pkgConstrDet,
                           PKG_NPC_COMMIT   = @pkgNpcCommit,
                           PKG_PC_COMMIT    = @pkgPcCommit
                       WHERE DATA_FOLDER_ID = @dataFolderId",
                    new SqlParameter("@newEntityId",   newEntityId),
                    new SqlParameter("@pkgBase",       pkgBase),
                    new SqlParameter("@pkgConstrSum",  pkgConstrSum),
                    new SqlParameter("@pkgConstrDet",  pkgConstrDet),
                    new SqlParameter("@pkgNpcCommit",  pkgNpcCommit),
                    new SqlParameter("@pkgPcCommit",   pkgPcCommit),
                    new SqlParameter("@dataFolderId",  dataFolderId)));
        });
    }

    private void ProcessJobId(IXLWorksheet ws)
    {
        // Pre-step: reset all INCLUDE_JOB to 0 before applying template selections
        Console.WriteLine("  Resetting INCLUDE_JOB = 0 for all jobs...");
        Exec("UPDATE [MAP].[T_TRANS_JOB] SET INCLUDE_JOB = 0");

        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: LEGACY_JOB_ID | C: LEGACY_EXTRA_ID | D: Description
        // E: Include? | F: NEW_JOB_ID | G: NEW_ENTITY_ID | H: NEW_DEPARTMENT_ID
        // I: NEW_CLASS_ID | J: NEW_CUSTOMER_ID | K: NEW_PM_ID
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId  = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyJobId   = Str(ws, row, 2);
            if (string.IsNullOrEmpty(legacyJobId)) return (skip: true, execute: null);

            var include       = Str(ws, row, 5);
            var includeJob    = include.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            var legacyExtraId = Str(ws, row, 3);
            var newJobId      = Str(ws, row, 6);
            var newEntityId   = Str(ws, row, 7);
            var newDeptId     = Str(ws, row, 8);
            var newClassId    = Str(ws, row, 9);
            var newCustomerId = Str(ws, row, 10);
            var newPmId       = Str(ws, row, 11);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_JOB]
                       SET NEW_JOB_ID        = @newJobId,
                           INCLUDE_JOB       = @includeJob,
                           NEW_ENTITY_ID     = @newEntityId,
                           NEW_DEPARTMENT_ID = @newDeptId,
                           NEW_CLASS_ID      = @newClassId,
                           NEW_CUSTOMER_ID   = @newCustomerId,
                           NEW_PM_ID         = @newPmId
                       WHERE LEGACY_JOB_ID   = @legacyJobId
                         AND LEGACY_EXTRA_ID = @legacyExtraId
                         AND DATA_FOLDER_ID  = @dataFolderId",
                    new SqlParameter("@newJobId",      newJobId),
                    new SqlParameter("@includeJob",    includeJob),
                    new SqlParameter("@newEntityId",   newEntityId),
                    new SqlParameter("@newDeptId",     newDeptId),
                    new SqlParameter("@newClassId",    newClassId),
                    new SqlParameter("@newCustomerId", newCustomerId),
                    new SqlParameter("@newPmId",       newPmId),
                    new SqlParameter("@legacyJobId",   legacyJobId),
                    new SqlParameter("@legacyExtraId", legacyExtraId),
                    new SqlParameter("@dataFolderId",  dataFolderId)));
        });
    }

    private void ProcessGLAccount(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy GL Account | C: Description | D: New GL Account
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyAcct = Str(ws, row, 2);
            var newAcct    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_BASEACCT]
                       SET New_Base_Account    = @newAcct
                       WHERE Legacy_Base_Account = @legacyAcct
                         AND Data_Folder_Id    = @dataFolderId",
                    new SqlParameter("@newAcct",      newAcct),
                    new SqlParameter("@legacyAcct",   legacyAcct),
                    new SqlParameter("@dataFolderId", dataFolderId)));
        });
    }

    private void ProcessLocation(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Location | C: Description | D: New Location
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyLoc = Str(ws, row, 2);
            var newLoc    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_LOCATION]
                       SET New_Location_Id       = @newLoc
                       WHERE Legacy_Location_Id  = @legacyLoc
                         AND Data_Folder_Id      = @dataFolderId",
                    new SqlParameter("@newLoc",       newLoc),
                    new SqlParameter("@legacyLoc",    legacyLoc),
                    new SqlParameter("@dataFolderId", dataFolderId)));
        });
    }

    private void ProcessDepartment(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Department | C: Description | D: New Department
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyDept = Str(ws, row, 2);
            var newDept    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_DEPARTMENT]
                       SET New_Department_Id       = @newDept
                       WHERE Legacy_Department_Id  = @legacyDept
                         AND Data_Folder_Id        = @dataFolderId",
                    new SqlParameter("@newDept",      newDept),
                    new SqlParameter("@legacyDept",   legacyDept),
                    new SqlParameter("@dataFolderId", dataFolderId)));
        });
    }

    private void ProcessClass(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Class | C: Description | D: New Class
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyClass = Str(ws, row, 2);
            var newClass    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_CLASS]
                       SET NEW_CLASS_ID        = @newClass
                       WHERE LEGACY_CLASS_ID   = @legacyClass
                         AND DATA_FOLDER_ID    = @dataFolderId",
                    new SqlParameter("@newClass",     newClass),
                    new SqlParameter("@legacyClass",  legacyClass),
                    new SqlParameter("@dataFolderId", dataFolderId)));
        });
    }

    private void ProcessVendor(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Vendor | C: Description | D: New Vendor
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyVendor = Str(ws, row, 2);
            var newVendor    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_VENDOR]
                       SET NEW_VENDOR_ID        = @newVendor
                       WHERE LEGACY_VENDOR_ID   = @legacyVendor
                         AND DATA_FOLDER_ID     = @dataFolderId",
                    new SqlParameter("@newVendor",    newVendor),
                    new SqlParameter("@legacyVendor", legacyVendor),
                    new SqlParameter("@dataFolderId", dataFolderId)));
        });
    }

    private void ProcessCustomer(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Customer | C: Description | D: New Customer
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyCustomer = Str(ws, row, 2);
            var newCustomer    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_CUSTOMER]
                       SET NEW_CUSTOMER_ID        = @newCustomer
                       WHERE LEGACY_CUSTOMER_ID   = @legacyCustomer
                         AND DATA_FOLDER_ID       = @dataFolderId",
                    new SqlParameter("@newCustomer",    newCustomer),
                    new SqlParameter("@legacyCustomer", legacyCustomer),
                    new SqlParameter("@dataFolderId",   dataFolderId)));
        });
    }

    private void ProcessCostCode(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Cost Code | C: Description | D: New Cost Code | E: New Item ID
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId   = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyCostCode = Str(ws, row, 2);
            var newCostCode    = Str(ws, row, 4);
            var newItemId      = Str(ws, row, 5);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_COST_CODE]
                       SET NEW_COST_CODE_ID       = @newCostCode,
                           NEW_ITEM_ID            = @newItemId
                       WHERE LEGACY_COST_CODE_ID  = @legacyCostCode
                         AND DATA_FOLDER_ID       = @dataFolderId",
                    new SqlParameter("@newCostCode",    newCostCode),
                    new SqlParameter("@newItemId",      newItemId),
                    new SqlParameter("@legacyCostCode", legacyCostCode),
                    new SqlParameter("@dataFolderId",   dataFolderId)));
        });
    }

    private void ProcessCostType(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Cost Type | C: Description
        // D: New Cost Type | E: New Item ID | F: New GL Account
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId   = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyCostType = Str(ws, row, 2);
            var newCostType    = Str(ws, row, 4);
            var newItemId      = Str(ws, row, 5);
            var newGlAccount   = Str(ws, row, 6);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_COST_TYPE]
                       SET NEW_COST_TYPE_ID        = @newCostType,
                           NEW_ITEM_ID             = @newItemId,
                           NEW_INTACCT_GL_ACCOUNT  = @newGlAccount
                       WHERE LEGACY_COST_TYPE_ID   = @legacyCostType
                         AND DATA_FOLDER_ID        = @dataFolderId",
                    new SqlParameter("@newCostType",    newCostType),
                    new SqlParameter("@newItemId",      newItemId),
                    new SqlParameter("@newGlAccount",   newGlAccount),
                    new SqlParameter("@legacyCostType", legacyCostType),
                    new SqlParameter("@dataFolderId",   dataFolderId)));
        });
    }

    private void ProcessEmployee(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Employee | C: Description | D: New Employee
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyEmp = Str(ws, row, 2);
            var newEmp    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_EMPLOYEE]
                       SET NEW_EMPLOYEE_ID        = @newEmp
                       WHERE LEGACY_EMPLOYEE_ID   = @legacyEmp
                         AND DATA_FOLDER_ID       = @dataFolderId",
                    new SqlParameter("@newEmp",       newEmp),
                    new SqlParameter("@legacyEmp",    legacyEmp),
                    new SqlParameter("@dataFolderId", dataFolderId)));
        });
    }

    private void ProcessInventoryItem(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Item | C: Description | D: New Item
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyItem = Str(ws, row, 2);
            var newItem    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_ITEM]
                       SET NEW_ITEM_ID        = @newItem
                       WHERE LEGACY_ITEM_ID   = @legacyItem
                         AND DATA_FOLDER_ID   = @dataFolderId",
                    new SqlParameter("@newItem",      newItem),
                    new SqlParameter("@legacyItem",   legacyItem),
                    new SqlParameter("@dataFolderId", dataFolderId)));
        });
    }

    private void ProcessWarehouse(IXLWorksheet ws)
    {
        // Header: row 3 | Data: row 4+ | Last col: hide/show
        // A: DATA_FOLDER_ID | B: Legacy Warehouse | C: Description | D: New Warehouse
        ProcessRows(ws, dataStartRow: 4, row =>
        {
            if (IsHidden(ws, row)) return (skip: true, execute: null);

            var dataFolderId    = Str(ws, row, 1);
            if (string.IsNullOrEmpty(dataFolderId)) return (skip: true, execute: null);

            var legacyWarehouse = Str(ws, row, 2);
            var newWarehouse    = Str(ws, row, 4);

            return (skip: false, execute: () =>
                Exec(@"UPDATE [MAP].[T_TRANS_WAREHOUSE]
                       SET NEW_WAREHOUSE_ID        = @newWarehouse
                       WHERE LEGACY_WAREHOUSE_ID   = @legacyWarehouse
                         AND DATA_FOLDER_ID        = @dataFolderId",
                    new SqlParameter("@newWarehouse",    newWarehouse),
                    new SqlParameter("@legacyWarehouse", legacyWarehouse),
                    new SqlParameter("@dataFolderId",    dataFolderId)));
        });
    }
}
