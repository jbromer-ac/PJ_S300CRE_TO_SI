$path = "D:\Project Files\PJ_S300CRE_TO_SI\Temp For Review - AR Aging Report (As of 04-30-2026).xlsx"
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$wb = $excel.Workbooks.Open($path)
$ws = $wb.Sheets.Item(1)
$lastRow = $ws.UsedRange.Rows.Count
$lastCol = $ws.UsedRange.Columns.Count
Write-Host "Rows: $lastRow, Cols: $lastCol"
for ($r = 1; $r -le [Math]::Min(50, $lastRow); $r++) {
    $row = "Row ${r}: "
    for ($c = 1; $c -le $lastCol; $c++) {
        $val = $ws.Cells.Item($r, $c).Text
        if ($val -ne "") { $row += "[col$c]=$val  " }
    }
    Write-Host $row
}
$wb.Close($false)
$excel.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
