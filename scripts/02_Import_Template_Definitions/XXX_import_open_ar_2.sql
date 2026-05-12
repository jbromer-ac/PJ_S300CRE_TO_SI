-- OPEN AR IMPORT (WITH PROJECT ID)
-- Same as XXX_import_open_ar.sql but with PROJECTID and SODOCUMENTENTRY_PROJECTID
-- populated from [MAP].[T_TRANS_JOB] where INCLUDE_JOB = 1
--
-- Reconciliation method: ties to Sage 300 CRE "Aging Summary by Invoice" canned report
--   Amount   (col N)  = SUM(ART.Amount + ART.Retainage) excl. Amount_Type 'Retainage released'
--   Retainage (col AA) = SUM(ARA_ACTIVITY__ACTIVITY.Retainage_Held) per Status
--                        (ARA naturally nets original withheld minus retainage-release adjustments)
--
-- Retainage breakdown DONOTIMPORT columns:
--   LG_RET_NET  = net retainage outstanding (= col AA; withheld minus released)
--   LG_RET_HELD = original retainage withheld on invoices (before any releases)
--   LG_RET_REL  = cumulative retainage released/reduced (positive = released back)
--   LG_RET_BIL  = retainage already billed to customer for collection

DECLARE @AgingDate DATE;
SET @AgingDate = (
    SELECT CONVERT(DATE, F.FIELD_VALUE, 23)
    FROM [MAP].[E_USEFUL_FIELDS] F
    WHERE F.FIELD_NAME = 'GL03_DETAIL_STOP');

WITH ART_BY_STATUS AS (
    -- Net outstanding amount per invoice status, excluding Retainage released rows
    SELECT
        ACT.Customer,
        ACT.Status_Type,
        ACT.Status_Date,
        ACT.Status_Seq,
        ACT.Data_Folder_Id,
        SUM(ACT.Amount + ACT.Retainage)                              AS Net_Amount,
        MIN(ISNULL(ACT.Due_Date,        CONVERT(DATE,'1900-01-01'))) AS Due_Date,
        MIN(ISNULL(ACT.Accounting_Date, CONVERT(DATE,'1900-01-01'))) AS Accounting_Date,
        MAX(ACT.Job)                                                  AS Job
    FROM [s300].[ART_CURRENT__TRANSACTION] ACT
    WHERE ACT.Amount_Type <> 'Retainage released'
      AND ISNULL(ACT.Accounting_Date, CONVERT(DATE,'1900-01-01')) <= @AgingDate
    GROUP BY ACT.Customer, ACT.Status_Type, ACT.Status_Date, ACT.Status_Seq, ACT.Data_Folder_Id
),
ARA_BY_STATUS AS (
    -- Retainage balances per invoice status
    -- Summing all activity types means releases (positive Retainage_Held) reduce the net total
    SELECT
        ARA.Customer,
        ARA.Status_Type,
        ARA.Status_Date,
        ARA.Status_Seq,
        ARA.Data_Folder_Id,
        SUM(ARA.Retainage_Held)                                                                     AS Retainage_Net,
        SUM(CASE WHEN ARA.Activity_Type = 'Invoice'            THEN ARA.Retainage_Held ELSE 0 END)  AS Retainage_Held_Gross,
        SUM(CASE WHEN ARA.Activity_Type = 'Retainage released' THEN ARA.Retainage_Held ELSE 0 END)  AS Retainage_Released,
        SUM(ARA.Retainage_Billed)                                                                   AS Retainage_Billed
    FROM [s300].[ARA_ACTIVITY__ACTIVITY] ARA
    GROUP BY ARA.Customer, ARA.Status_Type, ARA.Status_Date, ARA.Status_Seq, ARA.Data_Folder_Id
)
SELECT
    ART.Data_Folder_Id                                                              AS DONOTIMPORT,
    'LG_CUST | ' + ART.Customer                                                     AS DONOTIMPORT,
    'Startup AR'                                                                    AS TRANSACTIONTYPE,
    FORMAT(ARS.Status_Date, 'yyyy-MM-dd')                                           AS [DATE],
    FORMAT(NULLIF(ART.Accounting_Date, CONVERT(DATE,'1900-01-01')), 'yyyy-MM-dd')   AS GLPOSTINGDATE,
    ARS.Invoice                                                                     AS DOCUMENTNO,
    COALESCE(TC.NEW_CUSTOMER_ID, '')                                                AS CUSTOMER_ID,
    ''                                                                              AS TERMNAME,
    FORMAT(NULLIF(ART.Due_Date, CONVERT(DATE,'1900-01-01')), 'yyyy-MM-dd')          AS DATEDUE,
    'Pending'                                                                       AS STATE,
    1                                                                               AS LINE_NO,
    'Revenue'                                                                       AS ITEMID,
    1                                                                               AS QUANTITY,
    'Each'                                                                          AS UNIT,
    -- PRICE = net outstanding amount (= col N of canned report)
    ROUND(ART.Net_Amount, 2)                                                        AS PRICE,
    COALESCE(TE.NEW_ENTITY_ID, '')                                                  AS LOCATIONID,
    ''                                                                              AS DEPARTMENTID,
    ''                                                                              AS SODOCUMENTENTRY_CLASSID,
    COALESCE(CASE WHEN TJ.INCLUDE_JOB = 1 THEN TJ.NEW_JOB_ID ELSE '' END, '')      AS SODOCUMENTENTRY_PROJECTID,
    COALESCE(CASE WHEN TJ.INCLUDE_JOB = 1 THEN TJ.NEW_JOB_ID ELSE '' END, '')      AS PROJECTID,
    COALESCE(TC.NEW_CUSTOMER_ID, '')                                                AS SODOCUMENTENTRY_CUSTOMERID,
    COALESCE(C.Name, '')                                                            AS MEMO,
    'Price'                                                                         AS CONVERSIONTYPE,
    ''                                                                              AS SODOCUMENTENTRY_RETAINAGEPERCENTAGE,
    -- AMOUNTRETAINED = net retainage outstanding (positive = held back; = -col AA of canned report)
    ROUND(-ISNULL(ARA.Retainage_Net, 0), 2)                                        AS SODOCUMENTENTRY_AMOUNTRETAINED,
    -- Retainage breakdown reference columns
    'LG_RET_NET | '  + CAST(ROUND(ISNULL(ARA.Retainage_Net, 0), 2)          AS VARCHAR(30))  AS DONOTIMPORT,
    'LG_RET_HELD | ' + CAST(ROUND(ISNULL(ARA.Retainage_Held_Gross, 0), 2)   AS VARCHAR(30))  AS DONOTIMPORT,
    'LG_RET_REL | '  + CAST(ROUND(ISNULL(ARA.Retainage_Released, 0), 2)     AS VARCHAR(30))  AS DONOTIMPORT,
    'LG_RET_BIL | '  + CAST(ROUND(ISNULL(ARA.Retainage_Billed, 0), 2)       AS VARCHAR(30))  AS DONOTIMPORT,
    'LG_CUST_NME | ' + COALESCE(C.Name, '')                                                   AS DONOTIMPORT
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
LEFT JOIN [MAP].[T_TRANS_ENTITY] TE
    ON  ART.Data_Folder_Id = TE.DATA_FOLDER_ID
LEFT JOIN [MAP].[T_TRANS_JOB] TJ
    ON  ART.Data_Folder_Id = TJ.DATA_FOLDER_ID
    AND ART.Job            = TJ.LEGACY_JOB_ID
    AND ISNULL(TJ.LEGACY_EXTRA_ID, '') = ''
WHERE ART.Net_Amount <> 0 OR ISNULL(ARA.Retainage_Net, 0) <> 0
ORDER BY ART.Customer, ARS.Invoice;
