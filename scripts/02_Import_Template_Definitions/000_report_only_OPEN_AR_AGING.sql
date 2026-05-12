-- OPEN AR AGING REPORT (REPORT ONLY - NOT AN IMPORT)
-- Mirrors the Sage 300 CRE "Aging Summary by Invoice" calculation
-- Useful for reconciliation and identifying outages before migration
--
-- Amount   = net outstanding (excl. Retainage released) = col N of canned report
-- Ret_Net  = net retainage outstanding (withheld minus released) = col AA of canned report
-- Ret_Held = original retainage withheld on invoices (before any releases)
-- Ret_Rel  = cumulative retainage released so far (positive = released back)
-- Ret_Bil  = retainage already billed to customer for collection

DECLARE @AgingDate DATE;
SET @AgingDate = (
    SELECT CONVERT(DATE, F.FIELD_VALUE, 23)
    FROM [MAP].[E_USEFUL_FIELDS] F
    WHERE F.FIELD_NAME = 'GL03_DETAIL_STOP');

WITH ART_BY_STATUS AS (
    SELECT
        ACT.Customer,
        ACT.Status_Type,
        ACT.Status_Date,
        ACT.Status_Seq,
        ACT.Data_Folder_Id,
        SUM(ACT.Amount + ACT.Retainage)                              AS Net_Amount,
        MIN(ISNULL(ACT.Due_Date,        CONVERT(DATE,'1900-01-01'))) AS Due_Date,
        MIN(ISNULL(ACT.Accounting_Date, CONVERT(DATE,'1900-01-01'))) AS Accounting_Date
    FROM [s300].[ART_CURRENT__TRANSACTION] ACT
    WHERE ACT.Amount_Type <> 'Retainage released'
      AND ISNULL(ACT.Accounting_Date, CONVERT(DATE,'1900-01-01')) <= @AgingDate
    GROUP BY ACT.Customer, ACT.Status_Type, ACT.Status_Date, ACT.Status_Seq, ACT.Data_Folder_Id
),
ARA_BY_STATUS AS (
    SELECT
        ARA.Customer,
        ARA.Status_Type,
        ARA.Status_Date,
        ARA.Status_Seq,
        ARA.Data_Folder_Id,
        SUM(ARA.Retainage_Held)                                                                     AS Ret_Net,
        SUM(CASE WHEN ARA.Activity_Type = 'Invoice'            THEN ARA.Retainage_Held ELSE 0 END)  AS Ret_Held,
        SUM(CASE WHEN ARA.Activity_Type = 'Retainage released' THEN ARA.Retainage_Held ELSE 0 END)  AS Ret_Rel,
        SUM(ARA.Retainage_Billed)                                                                   AS Ret_Bil
    FROM [s300].[ARA_ACTIVITY__ACTIVITY] ARA
    GROUP BY ARA.Customer, ARA.Status_Type, ARA.Status_Date, ARA.Status_Seq, ARA.Data_Folder_Id
)
SELECT
    ART.Customer                                                        AS Customer,
    COALESCE(C.Name, '')                                                AS Customer_Name,
    ARS.Invoice                                                         AS Invoice,
    FORMAT(ARS.Status_Date, 'yyyy-MM-dd')                               AS Invoice_Date,
    FORMAT(NULLIF(ART.Due_Date, CONVERT(DATE,'1900-01-01')), 'yyyy-MM-dd') AS Due_Date,
    ARS.Status                                                          AS Status,
    ROUND(ART.Net_Amount, 2)                                            AS Amount,
    ROUND(ISNULL(ARA.Ret_Net,  0), 2)                                   AS Ret_Net,
    ROUND(ISNULL(ARA.Ret_Held, 0), 2)                                   AS Ret_Held,
    ROUND(ISNULL(ARA.Ret_Rel,  0), 2)                                   AS Ret_Rel,
    ROUND(ISNULL(ARA.Ret_Bil,  0), 2)                                   AS Ret_Bil,
    ROUND(ART.Net_Amount + ISNULL(ARA.Ret_Net, 0), 2)                  AS Total_Outstanding,
    COALESCE(TC.NEW_CUSTOMER_ID, '')                                    AS New_Customer_ID
FROM ART_BY_STATUS ART
INNER JOIN [s300].[ARA_ACTIVITY__STATUS] ARS
    ON  ART.Customer       = ARS.Customer
    AND ART.Status_Type    = ARS.Status_Type
    AND ART.Status_Date    = ARS.Status_Date
    AND ART.Status_Seq     = ARS.Status_Seq
    AND ART.Data_Folder_Id = ARS.Data_Folder_Id
LEFT JOIN ARA_BY_STATUS ARA
    ON  ART.Customer       = ARA.Customer
    AND ART.Status_Type    = ARA.Status_Type
    AND ART.Status_Date    = ARA.Status_Date
    AND ART.Status_Seq     = ARA.Status_Seq
    AND ART.Data_Folder_Id = ARA.Data_Folder_Id
LEFT JOIN [s300].[ARM_MASTER__CUSTOMER] C
    ON  ART.Customer       = C.Customer
    AND ART.Data_Folder_Id = C.Data_Folder_Id
LEFT JOIN [MAP].[T_TRANS_CUSTOMER] TC
    ON  ART.Customer       = TC.LEGACY_CUSTOMER_ID
    AND ART.Data_Folder_Id = TC.DATA_FOLDER_ID
WHERE ART.Net_Amount <> 0 OR ISNULL(ARA.Ret_Net, 0) <> 0

UNION ALL

-- Summary totals row
SELECT
    '~~~~ TOTALS ~~~~', '', '', '', '', '',
    ROUND(SUM(ART.Net_Amount), 2),
    ROUND(SUM(ISNULL(ARA.Ret_Net,  0)), 2),
    ROUND(SUM(ISNULL(ARA.Ret_Held, 0)), 2),
    ROUND(SUM(ISNULL(ARA.Ret_Rel,  0)), 2),
    ROUND(SUM(ISNULL(ARA.Ret_Bil,  0)), 2),
    ROUND(SUM(ART.Net_Amount + ISNULL(ARA.Ret_Net, 0)), 2),
    ''
FROM ART_BY_STATUS ART
INNER JOIN [s300].[ARA_ACTIVITY__STATUS] ARS
    ON  ART.Customer       = ARS.Customer
    AND ART.Status_Type    = ARS.Status_Type
    AND ART.Status_Date    = ARS.Status_Date
    AND ART.Status_Seq     = ARS.Status_Seq
    AND ART.Data_Folder_Id = ARS.Data_Folder_Id
LEFT JOIN ARA_BY_STATUS ARA
    ON  ART.Customer       = ARA.Customer
    AND ART.Status_Type    = ARA.Status_Type
    AND ART.Status_Date    = ARA.Status_Date
    AND ART.Status_Seq     = ARA.Status_Seq
    AND ART.Data_Folder_Id = ARA.Data_Folder_Id
WHERE ART.Net_Amount <> 0 OR ISNULL(ARA.Ret_Net, 0) <> 0

ORDER BY Customer, Invoice;
