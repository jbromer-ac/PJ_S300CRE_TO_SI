using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;

namespace S300CRE_to_SI.Source;

public class ImportExporter
{
    private readonly DatabaseConnection _db;

    public ImportExporter(DatabaseConnection db)
    {
        _db = db;
    }

    public void Export(string scriptsFolderPath, string outputFolderPath, string databaseName)
    {
        var importScriptsPath = Path.Combine(scriptsFolderPath, "02_Import_Template_Definitions");

        if (!Directory.Exists(importScriptsPath))
        {
            Console.WriteLine($"ERROR: Script folder not found: {importScriptsPath}");
            return;
        }

        if (!Directory.Exists(outputFolderPath))
        {
            Directory.CreateDirectory(outputFolderPath);
            Console.WriteLine($"Created output folder: {outputFolderPath}");
        }

        var scripts = Directory.GetFiles(importScriptsPath, "*.sql")
            .OrderBy(Path.GetFileName)
            .ToArray();

        if (scripts.Length == 0)
        {
            Console.WriteLine("No SQL scripts found in 02_Import_Template_Definitions.");
            return;
        }

        var dateStamp = DateTime.Today.ToString("yyyy-MM-dd");
        var createdFiles = new List<string>();

        foreach (var scriptPath in scripts)
        {
            var scriptName = Path.GetFileNameWithoutExtension(scriptPath);

            // Build output name: "001_import_employees" -> "001_Migration-Schimenti_employees"
            var parts = scriptName.Split("_import_", 2);
            var baseName = parts.Length == 2
                ? $"{parts[0]}_{databaseName}_{parts[1]}"
                : $"{scriptName}_{databaseName}";

            // Find the next sequence number for this baseName+date+database combination
            var prefix = $"{baseName} (V{dateStamp} ";
            var existingMax = Directory
                .GetFiles(outputFolderPath, "*.xlsx")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name != null && name.StartsWith(prefix) && name.EndsWith(")"))
                .Select(name =>
                {
                    var inner = name![prefix.Length..^1]; // strip prefix and trailing ")"
                    return int.TryParse(inner, out var n) ? n : 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            var seq = (existingMax + 1).ToString("000");
            var outputFileName = $"{baseName} (V{dateStamp} {seq}).xlsx";
            var outputPath = Path.Combine(outputFolderPath, outputFileName);

            Console.WriteLine($"\nProcessing: {Path.GetFileName(scriptPath)}");

            var sql = File.ReadAllText(scriptPath);

            try
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Import");

                using var cmd = new SqlCommand(sql, _db.GetConnection()) { CommandTimeout = 1200 }; // 20 minutes
                using var reader = cmd.ExecuteReader();

                var columnCount = reader.FieldCount;

                // Determine which columns are string types (should be formatted as text)
                var isStringColumn = new bool[columnCount];
                for (int col = 0; col < columnCount; col++)
                {
                    var fieldType = reader.GetFieldType(col);
                    isStringColumn[col] = fieldType == typeof(string);
                }

                // Write headers with formatting
                for (int col = 0; col < columnCount; col++)
                {
                    var cell = worksheet.Cell(1, col + 1);
                    cell.Value = reader.GetName(col);
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#BFBFBF");
                    cell.Style.Border.TopBorder    = XLBorderStyleValues.Thin;
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.LeftBorder   = XLBorderStyleValues.Thin;
                    cell.Style.Border.RightBorder  = XLBorderStyleValues.Thin;
                }

                // Write data rows
                int rowNum = 2;
                while (reader.Read())
                {
                    for (int col = 0; col < columnCount; col++)
                    {
                        var cell = worksheet.Cell(rowNum, col + 1);

                        if (reader.IsDBNull(col))
                        {
                            cell.Value = "";
                        }
                        else if (isStringColumn[col])
                        {
                            cell.Value = reader.GetString(col);
                        }
                        else
                        {
                            // Use string representation for non-string types to preserve exact values
                            cell.Value = reader.GetValue(col)?.ToString() ?? "";
                        }

                        if (isStringColumn[col])
                            cell.Style.NumberFormat.Format = "@";
                    }
                    rowNum++;
                }

                reader.Close();

                worksheet.Columns().AdjustToContents();
                worksheet.RangeUsed()!.SetAutoFilter();
                worksheet.SheetView.FreezeRows(1);

                workbook.SaveAs(outputPath);
                createdFiles.Add(outputPath);
                Console.WriteLine($"  -> Saved: {outputFileName} ({rowNum - 2} rows)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR: {ex.Message}");
            }
        }

        if (createdFiles.Count > 0)
        {
            var zipName = $"{databaseName}_imports (V{dateStamp}).zip";
            var zipPath = Path.Combine(outputFolderPath, zipName);

            if (File.Exists(zipPath)) File.Delete(zipPath);

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var file in createdFiles)
                zip.CreateEntryFromFile(file, Path.GetFileName(file));

            Console.WriteLine($"\nZipped {createdFiles.Count} file(s) to: {zipName}");
        }
    }
}

