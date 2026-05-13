using ClosedXML.Excel;
using Microsoft.Data.SqlClient;

// ── 1. Parse report ──────────────────────────────────────────────────────────
var path = @"D:\Project Files\PJ_S300CRE_TO_SI\Temp For Review - AR Aging Report (As of 04-30-2026).xlsx";
using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
var memStream = new MemoryStream();
fileStream.CopyTo(memStream);
memStream.Position = 0;
using var wb = new XLWorkbook(memStream);
var ws = wb.Worksheets.First();
var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

var report = new Dictionary<(string cust, string inv), (decimal amount, decimal retainage)>();
string currentCustomer = "";

for (int r = 1; r <= lastRow; r++)
{
    var col1 = ws.Cell(r, 1).GetString().Trim();
    var col2 = ws.Cell(r, 2).GetString().Trim();
    var col5 = ws.Cell(r, 5).GetString().Trim();
    var col14 = ws.Cell(r, 14).GetString().Trim();
    var col27 = ws.Cell(r, 27).GetString().Trim();

    if (!string.IsNullOrEmpty(col2) && col1 != "Invoice" && col1 != "Tran Type" && !col1.Contains("Penta"))
    {
        currentCustomer = col1;
        continue;
    }

    if (col1 == "Invoice" && !string.IsNullOrEmpty(col5))
    {
        static decimal Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var raw = s.Replace(",", "").Replace("*", "").Trim();
            bool neg = raw.EndsWith("-");
            raw = raw.TrimEnd('-');
            decimal.TryParse(raw, out var v);
            return neg ? -v : v;
        }
        var key = (currentCustomer, col5.Trim());
        var amt = Parse(col14);
        var ret = Parse(col27);
        if (report.ContainsKey(key))
            report[key] = (report[key].amount + amt, report[key].retainage + ret);
        else
            report[key] = (amt, ret);
    }
}
Console.WriteLine($"Report rows parsed: {report.Count}");

// Dump raw rows for 10-RIVER to see all transaction types
bool inRiver = false;
Console.WriteLine("\n── Raw report rows for 10-RIVER ───────────────────────────────────────");
for (int r = 1; r <= lastRow; r++)
{
    var c1 = ws.Cell(r, 1).GetString().Trim();
    var c2 = ws.Cell(r, 2).GetString().Trim();
    var c5 = ws.Cell(r, 5).GetString().Trim();
    var c14 = ws.Cell(r, 14).GetString().Trim();
    var c27 = ws.Cell(r, 27).GetString().Trim();
    if (!string.IsNullOrEmpty(c2) && c1 != "Invoice" && c1 != "Tran Type" && !c1.Contains("Penta"))
    {
        if (c1 == "10-RIVER") inRiver = true;
        else if (inRiver) break; // moved past 10-RIVER
    }
    if (inRiver)
        Console.WriteLine($"  col1={c1,-30} col2={c2,-30} col5={c5,-15} col14={c14,-15} col27={c27}");
}

var connStr = "Server=DataMigrationSe\\MIGRATION;Database=Penta_04-23-2026;Encrypt=True;TrustServerCertificate=True;User Id=sa;Password=@ccordant123$;";

// ── 1a. s300 tables ───────────────────────────────────────────────────────────
using (var connTbl = new SqlConnection(connStr))
{
    connTbl.Open();
    using var cmdTbl = new SqlCommand(
        "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 's300' ORDER BY TABLE_NAME", connTbl) { CommandTimeout = 30 };
    using var rTbl = cmdTbl.ExecuteReader();
    Console.WriteLine("\ns300 tables:");
    while (rTbl.Read()) Console.Write($"  {rTbl[0]}");
    Console.WriteLine();
}

// ── 1b. Distinct Amount_Types in ART ─────────────────────────────────────────
using (var connAT = new SqlConnection(connStr))
{
    connAT.Open();
    using var cmdAT = new SqlCommand(
        "SELECT DISTINCT Amount_Type, COUNT(*) AS Cnt FROM [s300].[ART_CURRENT__TRANSACTION] GROUP BY Amount_Type ORDER BY Cnt DESC", connAT) { CommandTimeout = 30 };
    using var rAT = cmdAT.ExecuteReader();
    Console.WriteLine("\nDistinct Amount_Types in ART:");
    while (rAT.Read()) Console.WriteLine($"  AmtType={rAT[0],-35} Cnt={rAT[1]}");
}

// ── 1d. Data folders ──────────────────────────────────────────────────────────
using (var conn0 = new SqlConnection(connStr))
{
    conn0.Open();
    using var cmd0 = new SqlCommand("SELECT DISTINCT Data_Folder_Id FROM [s300].[ART_CURRENT__TRANSACTION] ORDER BY 1", conn0) { CommandTimeout = 30 };
    using var r0 = cmd0.ExecuteReader();
    Console.WriteLine("Data folders in ART_CURRENT__TRANSACTION:");
    while (r0.Read()) Console.WriteLine($"  {r0[0]}");
}

// ── 1e. All Amount_Types for 10-RIVER by invoice ─────────────────────────────
using (var connRV = new SqlConnection(connStr))
{
    connRV.Open();
    using var cmdRV = new SqlCommand("""
        SELECT Invoice, Amount_Type, Transaction_Type,
               COUNT(*) AS Cnt, SUM(Amount) AS TotalAmt, SUM(Retainage) AS TotalRet
        FROM [s300].[ART_CURRENT__TRANSACTION]
        WHERE Customer = '10-RIVER'
        GROUP BY Invoice, Amount_Type, Transaction_Type
        ORDER BY Invoice, Amount_Type, Transaction_Type
        """, connRV) { CommandTimeout = 30 };
    using var rRV = cmdRV.ExecuteReader();
    Console.WriteLine("\n── 10-RIVER: All Amount_Types by invoice ───────────────────────────────");
    while (rRV.Read())
        Console.WriteLine($"  Inv={rRV[0],-10} AmtType={rRV[1],-25} TranType={rRV[2],-25} Cnt={rRV[3],3} TotalAmt={rRV[4],14} TotalRet={rRV[5],14}");
}

// ── 1f. ART columns + raw rows for 10-RIVER/21063 Retainage released ─────────
using (var connC = new SqlConnection(connStr))
{
    connC.Open();
    using var cmdC = new SqlCommand("SELECT TOP 1 * FROM [s300].[ART_CURRENT__TRANSACTION] WHERE Customer = '10-RIVER'", connC) { CommandTimeout = 30 };
    using var rC = cmdC.ExecuteReader();
    Console.WriteLine("\n── ART full column list ────────────────────────────────────────────────");
    for (int i = 0; i < rC.FieldCount; i++) Console.Write($"{rC.GetName(i)}  ");
    Console.WriteLine();
}

using (var connD = new SqlConnection(connStr))
{
    connD.Open();
    using var cmdD = new SqlCommand("""
        SELECT TOP 20 Invoice, Draw, Amount_Type, Transaction_Type,
               Amount, Retainage, Accounting_Date, Job, Extra, Cost_Code, Contract, Contract_Item
        FROM [s300].[ART_CURRENT__TRANSACTION]
        WHERE Customer = '10-RIVER' AND Invoice = '21063'
          AND Amount_Type IN ('Retainage released', 'Retainage billed')
        ORDER BY Amount_Type, Draw
        """, connD) { CommandTimeout = 30 };
    using var rD = cmdD.ExecuteReader();
    Console.WriteLine("\n── ART Retainage released/billed rows for 10-RIVER/21063 (top 20) ────");
    while (rD.Read())
        Console.WriteLine($"  Inv={rD[0],-8} Draw={rD[1],-5} AmtType={rD[2],-25} TranType={rD[3],-20} Amt={rD[4],10} Ret={rD[5],12} Date={rD[6],-12} Job={rD[7],-10} Extra={rD[8],-8} CC={rD[9],-10} Ctr={rD[10],-12} Item={rD[11]}");
}

// ── 1h. Retainage released rows: Related_Status fields for ALL 10-RIVER ──────
using (var connE = new SqlConnection(connStr))
{
    connE.Open();
    using var cmdE = new SqlCommand("""
        SELECT TOP 10 Invoice, Draw, Amount_Type,
               Retainage, Accounting_Date,
               Status_Type, Status_Date, Status_Seq, Actvty_Seq,
               Related_Status_Type, Related_Status_Date, Related_Status_Seq, Related_Actvty_Seq
        FROM [s300].[ART_CURRENT__TRANSACTION]
        WHERE Customer = '10-RIVER'
          AND Amount_Type IN ('Retainage released', 'Retainage billed')
        ORDER BY Invoice, Amount_Type, Draw
        """, connE) { CommandTimeout = 30 };
    using var rE = cmdE.ExecuteReader();
    Console.WriteLine("\n── 10-RIVER Ret.Released/Billed rows (top 10) with Related_Status ────");
    while (rE.Read())
        Console.WriteLine($"  Inv={rE[0],-8} Draw={rE[1],-5} AmtType={rE[2],-25} Ret={rE[3],12} AcctDate={rE[4],-12}  Status={rE[5]}/{rE[6]}/{rE[7]}  Related={rE[9]}/{rE[10]}/{rE[11]} RelActvty={rE[12]}");
}

// What ARA_ACTIVITY__ACTIVITY looks like for invoice 21063 (the retainage release invoice)
using (var connF = new SqlConnection(connStr))
{
    connF.Open();
    using var cmdF = new SqlCommand("""
        SELECT Invoice, Draw, Activity_Type, Retainage_Held, Retainage_Billed,
               Status_Type, Status_Date, Status_Seq, Actvty_Seq
        FROM [s300].[ARA_ACTIVITY__ACTIVITY]
        WHERE Customer = '10-RIVER' AND Invoice = '21063'
        ORDER BY Draw, Activity_Type
        """, connF) { CommandTimeout = 30 };
    using var rF = cmdF.ExecuteReader();
    Console.WriteLine("\n── ARA for 10-RIVER/21063 ──────────────────────────────────────────────");
    while (rF.Read())
        Console.WriteLine($"  Inv={rF[0],-8} Draw={rF[1],-5} ActType={rF[2],-25} RetHeld={rF[3],12} RetBilled={rF[4],12}  Status={rF[5]}/{rF[6]}/{rF[7]} Actvty={rF[8]}");
}

// ── 1i. ARA_ACTIVITY__STATUS for 10-BOCLO: check invoice 211093 and 211112 ───
using (var connG = new SqlConnection(connStr))
{
    connG.Open();
    using var cmdG = new SqlCommand("""
        SELECT Invoice, Status_Type, Status_Date, Status_Seq, Status,
               Invoice_Amount, Retainage_Held, Retainage_Billed,
               Outstanding_Amount, Amount_Paid
        FROM [s300].[ARA_ACTIVITY__STATUS]
        WHERE Customer = '10-BOCLO'
        ORDER BY Status_Date DESC, Status_Seq DESC
        """, connG) { CommandTimeout = 30 };
    using var rG = cmdG.ExecuteReader();
    Console.WriteLine("\n── ARA_ACTIVITY__STATUS for 10-BOCLO ───────────────────────────────────");
    while (rG.Read())
        Console.WriteLine($"  Inv={rG[0],-10} StatusDate={rG[2],-12} StatusSeq={rG[3],-4} Status={rG[4],-12} InvAmt={rG[5],14} RetHeld={rG[6],12} Outstanding={rG[8],14}");
}

// ── 1j. Check ART for 10-BOCLO/211112 to see if it has future accounting dates ─
using (var connH = new SqlConnection(connStr))
{
    connH.Open();
    using var cmdH = new SqlCommand("""
        SELECT Invoice, Amount_Type, Transaction_Type, Accounting_Date,
               SUM(Amount) AS TotalAmt, SUM(Retainage) AS TotalRet, COUNT(*) AS Cnt
        FROM [s300].[ART_CURRENT__TRANSACTION]
        WHERE Customer = '10-BOCLO' AND Invoice IN ('211093', '211112')
        GROUP BY Invoice, Amount_Type, Transaction_Type, Accounting_Date
        ORDER BY Invoice, Accounting_Date
        """, connH) { CommandTimeout = 30 };
    using var rH = cmdH.ExecuteReader();
    Console.WriteLine("\n── ART for 10-BOCLO invoices 211093 and 211112 ────────────────────────");
    while (rH.Read())
        Console.WriteLine($"  Inv={rH[0],-10} AmtType={rH[1],-25} TranType={rH[2],-20} AcctDate={rH[3],-12} Cnt={rH[6],3} Amt={rH[4],14} Ret={rH[5],12}");
}

// ── 1g. Spot-check retainage mismatch: 10-RIVER ──────────────────────────────
// Check distinct Amount_Types and Transaction_Types
var spotSql = """
    SELECT DISTINCT Transaction_Type, Amount_Type,
           COUNT(*) AS Cnt,
           SUM(Amount) AS TotalAmt, SUM(Retainage) AS TotalRet
    FROM [s300].[ART_CURRENT__TRANSACTION]
    WHERE Customer = '10-RIVER' AND Invoice = '20441'
    GROUP BY Transaction_Type, Amount_Type
    ORDER BY Transaction_Type, Amount_Type
    """;
using (var conn2 = new SqlConnection(connStr))
{
    conn2.Open();
    using var cmd2 = new SqlCommand(spotSql, conn2) { CommandTimeout = 30 };
    using var r2 = cmd2.ExecuteReader();
    Console.WriteLine("\n── 10-RIVER/20441: Amount_Types breakdown ──────────────────────────────");
    while (r2.Read())
        Console.WriteLine($"  TranType={r2[0],-25} AmtType={r2[1],-30} Cnt={r2[2],4} TotalAmt={r2[3],14} TotalRet={r2[4],14}");
}

// Check ARA columns and data for 10-RIVER/20441
var spotSql2 = """
    SELECT TOP 1 * FROM [s300].[ARA_ACTIVITY__ACTIVITY]
    WHERE Customer = '10-RIVER'
    """;
using (var conn3 = new SqlConnection(connStr))
{
    conn3.Open();
    using var cmd3 = new SqlCommand(spotSql2, conn3) { CommandTimeout = 30 };
    using var r3 = cmd3.ExecuteReader();
    Console.WriteLine("\n── ARA columns ────────────────────────────────────────────────────────");
    for (int i = 0; i < r3.FieldCount; i++) Console.Write($"{r3.GetName(i)}  ");
    Console.WriteLine();
    if (r3.Read())
    {
        for (int i = 0; i < r3.FieldCount; i++) Console.Write($"{r3[i]}  ");
        Console.WriteLine();
    }
}

// Sum ARA Retainage_Held for 10-RIVER/20441
var spotSql3 = """
    SELECT Activity_Type, Invoice, Draw, COUNT(*) AS Cnt,
           SUM(Amount) AS TotalAmt, SUM(Retainage_Held) AS TotalRetHeld, SUM(Retainage_Billed) AS TotalRetBilled
    FROM [s300].[ARA_ACTIVITY__ACTIVITY]
    WHERE Customer = '10-RIVER' AND Invoice = '20441'
    GROUP BY Activity_Type, Invoice, Draw
    ORDER BY Activity_Type
    """;
using (var conn4 = new SqlConnection(connStr))
{
    conn4.Open();
    using var cmd4 = new SqlCommand(spotSql3, conn4) { CommandTimeout = 30 };
    using var r4 = cmd4.ExecuteReader();
    Console.WriteLine("\n── ARA for 10-RIVER/20441 ──────────────────────────────────────────────");
    while (r4.Read())
        Console.WriteLine($"  ActType={r4[0],-20} Inv={r4[1],-10} Draw={r4[2],-8} Cnt={r4[3],4} TotalAmt={r4[4],14} RetHeld={r4[5],14} RetBilled={r4[6],14}");
}

// Show individual ART rows for 10-RIVER/20441
var spotSql4a = """
    SELECT Transaction_Type, Draw, Amount_Type, Accounting_Date,
           Amount, Retainage, Status_Type, Status_Date, Status_Seq, Actvty_Seq
    FROM [s300].[ART_CURRENT__TRANSACTION]
    WHERE Customer = '10-RIVER' AND Invoice = '20441'
    ORDER BY Draw, Transaction_Type, Accounting_Date
    """;
using (var connX = new SqlConnection(connStr))
{
    connX.Open();
    using var cmdX = new SqlCommand(spotSql4a, connX) { CommandTimeout = 30 };
    using var rX = cmdX.ExecuteReader();
    Console.WriteLine("\n── ART raw rows for 10-RIVER/20441 ────────────────────────────────────");
    while (rX.Read())
        Console.WriteLine($"  TranType={rX[0],-25} Draw={rX[1],-6} AmtType={rX[2],-30} Date={rX[3],-12} Amt={rX[4],12} Ret={rX[5],12}  StatusType={rX[6]} StatusDate={rX[7]} StatusSeq={rX[8]} ActvtySeq={rX[9]}");
}

// Show individual ARA rows for 10-RIVER/20441
var spotSql4b = """
    SELECT Activity_Type, Draw, Invoice_Date, Amount, Retainage_Held, Retainage_Billed,
           Status_Type, Status_Date, Status_Seq, Actvty_Seq
    FROM [s300].[ARA_ACTIVITY__ACTIVITY]
    WHERE Customer = '10-RIVER' AND Invoice = '20441'
    ORDER BY Draw, Activity_Type
    """;
using (var connY = new SqlConnection(connStr))
{
    connY.Open();
    using var cmdY = new SqlCommand(spotSql4b, connY) { CommandTimeout = 30 };
    using var rY = cmdY.ExecuteReader();
    Console.WriteLine("\n── ARA raw rows for 10-RIVER/20441 ────────────────────────────────────");
    while (rY.Read())
        Console.WriteLine($"  ActType={rY[0],-25} Draw={rY[1],-6} Date={rY[2],-12} Amt={rY[3],12} RetHeld={rY[4],12} RetBilled={rY[5],12}  StatusType={rY[6]} StatusDate={rY[7]} StatusSeq={rY[8]} ActvtySeq={rY[9]}");
}

// Now test: join ART to ARA and use ARA Retainage_Held
var spotSql4 = """
    DECLARE @AgingDate DATE = '2026-04-30';
    SELECT ACT.Customer, ACT.Invoice,
           SUM(ACT.Amount + ACT.Retainage)    AS ART_AMOUNT,
           SUM(ACT.Retainage)                 AS ART_RETAINAGE,
           SUM(ARA.Retainage_Held)            AS ARA_RET_HELD,
           SUM(ARA.Retainage_Billed)          AS ARA_RET_BILLED
    FROM [s300].[ART_CURRENT__TRANSACTION] ACT
    LEFT JOIN [s300].[ARA_ACTIVITY__ACTIVITY] ARA
        ON  ACT.Customer     = ARA.Customer
        AND ACT.Status_Type  = ARA.Status_Type
        AND ACT.Status_Date  = ARA.Status_Date
        AND ACT.Status_Seq   = ARA.Status_Seq
        AND ACT.Actvty_Seq   = ARA.Actvty_Seq
        AND ACT.Data_Folder_Id = ARA.Data_Folder_Id
    WHERE ACT.Customer = '10-RIVER' AND ACT.Invoice = '20441'
      AND ISNULL(ACT.Accounting_Date, '1900-01-01') <= @AgingDate
    GROUP BY ACT.Customer, ACT.Invoice
    """;
using (var conn5 = new SqlConnection(connStr))
{
    conn5.Open();
    using var cmd5 = new SqlCommand(spotSql4, conn5) { CommandTimeout = 30 };
    using var r5 = cmd5.ExecuteReader();
    Console.WriteLine("\n── ART+ARA joined for 10-RIVER/20441 ──────────────────────────────────");
    while (r5.Read())
        Console.WriteLine($"  Cust={r5[0],-12} Inv={r5[1],-10} ARTAmt={r5[2],12} ARTRet={r5[3],12} ARARetHeld={r5[4],12} ARARetBilled={r5[5],12}");
}

// ── 1d. Check ARA_ACTIVITY__STATUS for 10-RIVER ──────────────────────────────
using (var connS = new SqlConnection(connStr))
{
    connS.Open();
    // First see columns
    using var cmdS = new SqlCommand("SELECT TOP 1 * FROM [s300].[ARA_ACTIVITY__STATUS] WHERE Customer = '10-RIVER'", connS) { CommandTimeout = 30 };
    using var rS = cmdS.ExecuteReader();
    Console.WriteLine("\n── ARA_ACTIVITY__STATUS columns ────────────────────────────────────────");
    for (int i = 0; i < rS.FieldCount; i++) Console.Write($"{rS.GetName(i)}  ");
    Console.WriteLine();
    if (rS.Read()) { for (int i = 0; i < rS.FieldCount; i++) Console.Write($"{rS[i]}  "); Console.WriteLine(); }
}

using (var connS2 = new SqlConnection(connStr))
{
    connS2.Open();
    using var cmdS2 = new SqlCommand("""
        SELECT Invoice, Status_Type, Status_Date, Status_Seq, Status,
               Invoice_Amount, Retainage_Held, Retainage_Billed,
               Outstanding_Amount, Amount_Paid, Adjustment
        FROM [s300].[ARA_ACTIVITY__STATUS]
        WHERE Customer = '10-RIVER' AND Invoice = '20441'
        ORDER BY Status_Date, Status_Seq
        """, connS2) { CommandTimeout = 30 };
    using var rS2 = cmdS2.ExecuteReader();
    Console.WriteLine("\n── ARA_ACTIVITY__STATUS for 10-RIVER/20441 ────────────────────────────");
    while (rS2.Read())
        Console.WriteLine($"  Inv={rS2[0],-10} StatusType={rS2[1],-12} StatusDate={rS2[2],-12} StatusSeq={rS2[3],-4} Status={rS2[4],-10} InvAmt={rS2[5],12} RetHeld={rS2[6],12} RetBilled={rS2[7],12} Outstanding={rS2[8],12} Paid={rS2[9],12} Adj={rS2[10],12}");
}

// ── 2. Run SQL ────────────────────────────────────────────────────────────────
var sql = """
    DECLARE @AgingDate DATE = '2026-04-30';
    WITH ART_AMT AS (
        SELECT Customer, Status_Type, Status_Date, Status_Seq, Data_Folder_Id,
               SUM(Amount + Retainage) AS Net_Amount
        FROM [s300].[ART_CURRENT__TRANSACTION]
        WHERE Amount_Type <> 'Retainage released'
          AND ISNULL(Accounting_Date, '1900-01-01') <= @AgingDate
        GROUP BY Customer, Status_Type, Status_Date, Status_Seq, Data_Folder_Id
    ),
    ARA_RET AS (
        SELECT Customer, Status_Type, Status_Date, Status_Seq, Data_Folder_Id,
               SUM(Retainage_Held) AS Net_Retainage
        FROM [s300].[ARA_ACTIVITY__ACTIVITY]
        GROUP BY Customer, Status_Type, Status_Date, Status_Seq, Data_Folder_Id
    )
    SELECT
        A.Customer,
        S.Invoice   AS INVOICE_ID,
        A.Net_Amount        AS AMOUNT,
        ISNULL(R.Net_Retainage, 0) AS RETAINAGE
    FROM ART_AMT A
    INNER JOIN [s300].[ARA_ACTIVITY__STATUS] S
        ON A.Customer      = S.Customer
        AND A.Status_Type  = S.Status_Type
        AND A.Status_Date  = S.Status_Date
        AND A.Status_Seq   = S.Status_Seq
        AND A.Data_Folder_Id = S.Data_Folder_Id
    LEFT JOIN ARA_RET R
        ON A.Customer      = R.Customer
        AND A.Status_Type  = R.Status_Type
        AND A.Status_Date  = R.Status_Date
        AND A.Status_Seq   = R.Status_Seq
        AND A.Data_Folder_Id = R.Data_Folder_Id
    WHERE A.Net_Amount <> 0 OR ISNULL(R.Net_Retainage, 0) <> 0
    ORDER BY A.Customer, S.Invoice
    """;

var dbRows = new Dictionary<(string cust, string inv), (decimal amount, decimal retainage)>();
using (var conn = new SqlConnection(connStr))
{
    conn.Open();
    using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var cust = reader.GetString(0).Trim();
        var inv  = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString()?.Trim() ?? "";
        var amt  = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2));
        var ret  = reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3));
        dbRows[(cust, inv)] = (amt, ret);
    }
}
Console.WriteLine($"DB rows returned:    {dbRows.Count}");

// ── 3. Compare + export differences to xlsx ───────────────────────────────────
int matched = 0, amtMismatch = 0, retMismatch = 0, missingInDb = 0, extraInDb = 0;

var diffRows = new List<(string type, string cust, string inv,
    decimal rptAmt, decimal dbAmt, decimal amtDiff,
    decimal rptRet, decimal dbRet, decimal retDiff)>();

foreach (var (key, rpt) in report.OrderBy(x => x.Key.cust).ThenBy(x => x.Key.inv))
{
    if (!dbRows.TryGetValue(key, out var db))
    {
        missingInDb++;
        diffRows.Add(("MISSING IN DB", key.cust, key.inv,
            rpt.amount, 0, rpt.amount - 0,
            rpt.retainage, 0, rpt.retainage - 0));
        continue;
    }
    bool amtOk = Math.Abs(rpt.amount    - db.amount)    < 0.02m;
    bool retOk = Math.Abs(rpt.retainage - db.retainage) < 0.02m;
    if (amtOk && retOk) { matched++; continue; }
    if (!amtOk) amtMismatch++;
    if (!retOk) retMismatch++;
    diffRows.Add(("MISMATCH", key.cust, key.inv,
        rpt.amount, db.amount, rpt.amount - db.amount,
        rpt.retainage, db.retainage, rpt.retainage - db.retainage));
}
foreach (var (key, db) in dbRows.OrderBy(x => x.Key.cust))
{
    if (!report.ContainsKey(key))
    {
        extraInDb++;
        diffRows.Add(("EXTRA IN DB", key.cust, key.inv,
            0, db.amount, 0 - db.amount,
            0, db.retainage, 0 - db.retainage));
    }
}

Console.WriteLine($"\n── SUMMARY ─────────────────────────────────────────────────────────────");
Console.WriteLine($"  Matched:        {matched}");
Console.WriteLine($"  Amount mismatch:{amtMismatch}");
Console.WriteLine($"  Retainage mismatch:{retMismatch}");
Console.WriteLine($"  Missing in DB:  {missingInDb}");
Console.WriteLine($"  Extra in DB (not in report): {extraInDb}");

// ── 4. Export differences to xlsx ─────────────────────────────────────────────
var xlsxPath = @"D:\Project Files\PJ_S300CRE_TO_SI\Temp - AR Aging Differences (DB vs Report).xlsx";
using var wbDiff = new XLWorkbook();
var ws2 = wbDiff.Worksheets.Add("Differences");

string[] headers = ["Type", "Customer", "Invoice",
    "Report_Amount", "DB_Amount", "Amount_Diff",
    "Report_Retainage", "DB_Retainage", "Retainage_Diff"];

for (int c = 0; c < headers.Length; c++)
{
    var cell = ws2.Cell(1, c + 1);
    cell.Value = headers[c];
    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#BFBFBF");
    cell.Style.Font.Bold = true;
    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
}

int row = 2;
foreach (var d in diffRows)
{
    ws2.Cell(row, 1).Value = d.type;
    ws2.Cell(row, 2).Value = d.cust;
    ws2.Cell(row, 3).Value = d.inv;
    ws2.Cell(row, 4).Value = (double)d.rptAmt;
    ws2.Cell(row, 5).Value = (double)d.dbAmt;
    ws2.Cell(row, 6).Value = (double)d.amtDiff;
    ws2.Cell(row, 7).Value = (double)d.rptRet;
    ws2.Cell(row, 8).Value = (double)d.dbRet;
    ws2.Cell(row, 9).Value = (double)d.retDiff;
    for (int c = 4; c <= 9; c++)
        ws2.Cell(row, c).Style.NumberFormat.Format = "#,##0.00";
    row++;
}

ws2.Columns().AdjustToContents();
ws2.RangeUsed()!.SetAutoFilter();
ws2.SheetView.FreezeRows(1);
wb.SaveAs(xlsxPath);
Console.WriteLine($"\nDifferences exported to: {Path.GetFileName(xlsxPath)} ({diffRows.Count} rows)");

// Verify the written file
using var fsVerify = new FileStream(xlsxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
var msVerify = new MemoryStream(); fsVerify.CopyTo(msVerify); msVerify.Position = 0;
using var wbVerify = new XLWorkbook(msVerify);
var wsVerify = wbVerify.Worksheets.First();
var verifyLastRow = wsVerify.LastRowUsed()?.RowNumber() ?? 0;
var verifyLastCol = wsVerify.LastColumnUsed()?.ColumnNumber() ?? 0;
Console.WriteLine($"  Verified: {verifyLastRow} rows x {verifyLastCol} cols in '{wsVerify.Name}' sheet");
Console.WriteLine($"  Headers: {string.Join(", ", Enumerable.Range(1, verifyLastCol).Select(c => wsVerify.Cell(1, c).GetString()))}");
Console.WriteLine($"  First data row: {string.Join(", ", Enumerable.Range(1, verifyLastCol).Select(c => wsVerify.Cell(2, c).GetString()))}");
