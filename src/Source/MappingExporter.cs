using ClosedXML.Excel;
using Microsoft.Data.SqlClient;

namespace S300CRE_to_SI.Source;

public class MappingExporter
{
    private readonly DatabaseConnection _db;

    private record SheetDef(string Name, int HeaderRow, string[] Headers, string Sql);

    public MappingExporter(DatabaseConnection db)
    {
        _db = db;
    }

    public void Export(string outputFolderPath, string databaseName)
    {
        if (!Directory.Exists(outputFolderPath))
        {
            Directory.CreateDirectory(outputFolderPath);
            Console.WriteLine($"Created output folder: {outputFolderPath}");
        }

        var dateStamp = DateTime.Today.ToString("yyyy-MM-dd");
        var baseName = $"ETL_Mapping_Export_{databaseName}";
        var prefix = $"{baseName} (V{dateStamp} ";

        var existingMax = Directory
            .GetFiles(outputFolderPath, "*.xlsx")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != null && name.StartsWith(prefix) && name.EndsWith(")"))
            .Select(name =>
            {
                var inner = name![prefix.Length..^1];
                return int.TryParse(inner, out var n) ? n : 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        var seq = (existingMax + 1).ToString("000");
        var outputFileName = $"{baseName} (V{dateStamp} {seq}).xlsx";
        var outputPath = Path.Combine(outputFolderPath, outputFileName);

        var sheets = BuildSheetDefinitions();

        Console.WriteLine($"\nExporting mapping data to: {outputFileName}");

        using var workbook = new XLWorkbook();

        foreach (var sheet in sheets)
        {
            Console.Write($"  [{sheet.Name}]... ");
            var rowCount = WriteSheet(workbook, sheet);
            Console.WriteLine($"{rowCount} rows");
        }

        workbook.SaveAs(outputPath);
        Console.WriteLine($"\nSaved: {outputFileName}");
    }

    private int WriteSheet(XLWorkbook workbook, SheetDef sheet)
    {
        var ws = workbook.Worksheets.Add(sheet.Name);

        // Write headers at the specified row
        for (int col = 0; col < sheet.Headers.Length; col++)
        {
            var cell = ws.Cell(sheet.HeaderRow, col + 1);
            cell.Value = sheet.Headers[col];
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#BFBFBF");
            cell.Style.Border.TopBorder    = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.LeftBorder   = XLBorderStyleValues.Thin;
            cell.Style.Border.RightBorder  = XLBorderStyleValues.Thin;
        }

        // Write data
        int rowNum = sheet.HeaderRow + 1;

        using var cmd = new SqlCommand(sheet.Sql, _db.GetConnection());
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            for (int col = 0; col < reader.FieldCount; col++)
            {
                var cell = ws.Cell(rowNum, col + 1);
                var value = reader.IsDBNull(col) ? "" : reader.GetValue(col)?.ToString() ?? "";
                cell.Value = value;
                cell.Style.NumberFormat.Format = "@";
            }
            rowNum++;
        }

        reader.Close();

        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();
        ws.SheetView.FreezeRows(sheet.HeaderRow);

        return rowNum - sheet.HeaderRow - 1;
    }

    private static SheetDef[] BuildSheetDefinitions() =>
    [
        new SheetDef(
            Name: "Controls",
            HeaderRow: 5,
            Headers: ["Field", "Value"],
            Sql: """
                SELECT FIELD_NAME, FIELD_VALUE
                FROM [MAP].[E_USEFUL_FIELDS]
                ORDER BY FIELD_NAME
                """
        ),

        new SheetDef(
            Name: "Entity",
            HeaderRow: 3,
            Headers: ["Data Folder", "Data Folder Description", "Entity ID", "PKG_BASE", "PKG_CONSTR_SUM", "PKG_CONSTR_DET", "PKG_PAYROLL", "PKG_NPC_COMMIT", "PKG_PC_COMMIT"],
            Sql: """
                SELECT TE.DATA_FOLDER_ID,
                       TDF.LEGACY_DATA_FOLDER_NAME,
                       TE.NEW_ENTITY_ID,
                       TE.PKG_BASE,
                       TE.PKG_CONSTR_SUM,
                       TE.PKG_CONSTR_DET,
                       '' AS PKG_PAYROLL,
                       TE.PKG_NPC_COMMIT,
                       TE.PKG_PC_COMMIT
                FROM [MAP].[T_TRANS_ENTITY] TE
                LEFT JOIN [MAP].[T_TRANS_DATA_FOLDER] TDF ON TE.DATA_FOLDER_ID = TDF.LEGACY_DATA_FOLDER_ID
                ORDER BY TE.DATA_FOLDER_ID
                """
        ),

        new SheetDef(
            Name: "Job ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Job", "Job Extra", "Job Description", "Include?", "Job", "Entity/Loc ID", "Department ID", "Class ID", "Customer ID", "PM ID"],
            Sql: """
                SELECT TJ.DATA_FOLDER_ID,
                       TJ.LEGACY_JOB_ID,
                       TJ.LEGACY_EXTRA_ID,
                       TJ.LEGACY_JOB_NAME,
                       CASE WHEN TJ.INCLUDE_JOB = 1 THEN 'Yes' ELSE 'No' END,
                       TJ.NEW_JOB_ID,
                       TJ.NEW_ENTITY_ID,
                       TJ.NEW_DEPARTMENT_ID,
                       TJ.NEW_CLASS_ID,
                       TJ.NEW_CUSTOMER_ID,
                       TJ.NEW_PM_ID
                FROM [MAP].[T_TRANS_JOB] TJ
                ORDER BY TJ.DATA_FOLDER_ID, TJ.LEGACY_JOB_ID, TJ.LEGACY_EXTRA_ID
                """
        ),

        new SheetDef(
            Name: "GL Account",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "GL Account", "GL Account Description", "GL Account"],
            Sql: """
                SELECT TBA.Data_Folder_Id,
                       TBA.Legacy_Base_Account,
                       (SELECT TOP 1 MA.Account_Title
                        FROM [MAP].[T_MASTER_ACCOUNT] MA
                        WHERE MA.BaseAccount = TBA.Legacy_Base_Account
                          AND MA.Data_Folder_Id = TBA.Data_Folder_Id) AS Description,
                       TBA.New_Base_Account
                FROM [MAP].[T_TRANS_BASEACCT] TBA
                ORDER BY TBA.Data_Folder_Id, TBA.Legacy_Base_Account
                """
        ),

        new SheetDef(
            Name: "Location ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Location", "Location Description", "Location"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_LOCATION_ID, '' AS Description, NEW_LOCATION_ID
                FROM [MAP].[T_TRANS_LOCATION]
                ORDER BY DATA_FOLDER_ID, LEGACY_LOCATION_ID
                """
        ),

        new SheetDef(
            Name: "Department ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Department", "Department Description", "Department"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_DEPARTMENT_ID, '' AS Description, NEW_DEPARTMENT_ID
                FROM [MAP].[T_TRANS_DEPARTMENT]
                ORDER BY DATA_FOLDER_ID, LEGACY_DEPARTMENT_ID
                """
        ),

        new SheetDef(
            Name: "Class ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Class", "Class Description", "Class"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_CLASS_ID, '' AS Description, NEW_CLASS_ID
                FROM [MAP].[T_TRANS_CLASS]
                ORDER BY DATA_FOLDER_ID, LEGACY_CLASS_ID
                """
        ),

        new SheetDef(
            Name: "Vendor ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Vendor", "Vendor Description", "Vendor"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_VENDOR_ID, LEGACY_VENDOR_NAME, NEW_VENDOR_ID
                FROM [MAP].[T_TRANS_VENDOR]
                ORDER BY DATA_FOLDER_ID, LEGACY_VENDOR_ID
                """
        ),

        new SheetDef(
            Name: "Customer ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Customer", "Customer Description", "Customer"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_CUSTOMER_ID, LEGACY_CUSTOMER_NAME, NEW_CUSTOMER_ID
                FROM [MAP].[T_TRANS_CUSTOMER]
                ORDER BY DATA_FOLDER_ID, LEGACY_CUSTOMER_ID
                """
        ),

        new SheetDef(
            Name: "Cost Code ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Cost Code", "Cost Code Description", "Cost Code", "Item ID"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_COST_CODE_ID, LEGACY_COST_CODE_NAME, NEW_COST_CODE_ID, NEW_ITEM_ID
                FROM [MAP].[T_TRANS_COST_CODE]
                ORDER BY DATA_FOLDER_ID, LEGACY_COST_CODE_ID
                """
        ),

        new SheetDef(
            Name: "Cost Type ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Cost Type", "Cost Type Description", "Cost Type", "Item ID", "GL Account"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_COST_TYPE_ID, LEGACY_COST_TYPE_NAME, NEW_COST_TYPE_ID, NEW_ITEM_ID, NEW_INTACCT_GL_ACCOUNT
                FROM [MAP].[T_TRANS_COST_TYPE]
                ORDER BY DATA_FOLDER_ID, LEGACY_COST_TYPE_ID
                """
        ),

        new SheetDef(
            Name: "Employee ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Employee", "Employee Description", "Employee"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_EMPLOYEE_ID, LEGACY_EMPLOYEE_NAME, NEW_EMPLOYEE_ID
                FROM [MAP].[T_TRANS_EMPLOYEE]
                ORDER BY DATA_FOLDER_ID, LEGACY_EMPLOYEE_ID
                """
        ),

        new SheetDef(
            Name: "Inventory Item ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Inventory Item", "Inventory Item Description", "Inventory Item"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_ITEM_ID, '' AS Description, NEW_ITEM_ID
                FROM [MAP].[T_TRANS_ITEM]
                ORDER BY DATA_FOLDER_ID, LEGACY_ITEM_ID
                """
        ),

        new SheetDef(
            Name: "Warehouse ID",
            HeaderRow: 3,
            Headers: ["Data Folder ID", "Warehouse", "Warehouse Description", "Warehouse", "Location ID"],
            Sql: """
                SELECT DATA_FOLDER_ID, LEGACY_WAREHOUSE_ID, '' AS Description, NEW_WAREHOUSE_ID, '' AS LOCATION_ID
                FROM [MAP].[T_TRANS_WAREHOUSE]
                ORDER BY DATA_FOLDER_ID, LEGACY_WAREHOUSE_ID
                """
        ),
    ];
}
