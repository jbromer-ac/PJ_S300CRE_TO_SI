---JTD COST AS OF 04/09/26
DECLARE @Cutoff_Date DATE;
SET @Cutoff_Date = (
    SELECT CONVERT(DATE, F.FIELD_VALUE, 23)  -- 23 = yyyy-mm-dd
    FROM [MAP].[E_USEFUL_FIELDS] F
    WHERE F.FIELD_NAME = 'GL03_DETAIL_STOP'
);
DECLARE @BatchSize   INT  = 5000;  -- ← Adjust batch size here

;WITH AllData AS (
    -- =========================================================================
    -- Full result set — identical logic to original query.
    -- Internal aliases are unique so the batch CTEs below can reference them.
    -- The final SELECT re-aliases everything back to the original output names.
    -- Two helper columns added at the end (GROUPING_DESC, ENTRY_OFFSET_SORT)
    -- are used only for batch assignment and ORDER BY — not in final output.
    -- =========================================================================
    SELECT
        ''                                                                                          AS DONOTIMPORT_1,
        JOURNAL AS JOURNAL,
        FORMAT([DATE], 'yyyy-MM-dd') AS DATE_COL,
        'Cost Entry | ' + DESCRIPTION AS DESCRIPTION_OUT,
        REFERENCE_NO AS REFERENCE_NO,
        LINE_NO_REV                                                                                 AS LINE_NO,
        ACCT_NO,
        COALESCE(LOCATION_ID, '')                                                                   AS LOCATION_ID,
        COALESCE(DEPT_ID, '')                                                                       AS DEPT_ID,
        CLASS_ID,
        DOCUMENT,
        CASE WHEN ENTRY_OFFSET = '0' THEN MEMO               ELSE '' END                           AS MEMO_COL,
        DEBIT																					   AS DEBIT,
        CREDIT                                                                                     AS CREDIT,
        STATUS,
        CASE WHEN ENTRY_OFFSET = '0' THEN GLENTRY_PROJECTID  ELSE '' END                           AS GLENTRY_PROJECTID,
        CASE WHEN ENTRY_OFFSET = '0' THEN GLENTRY_TASKID     ELSE '' END                           AS GLENTRY_TASKID,
        CASE WHEN ENTRY_OFFSET = '0' THEN GLENTRY_COSTTYPEID ELSE '' END                           AS GLENTRY_COSTTYPEID,
        CASE WHEN ENTRY_OFFSET = '0' THEN COALESCE(CAST(GLENTRY_VENDORID AS VARCHAR(50)), '')
                                     ELSE '' END                                                    AS GLENTRY_VENDORID,
        COALESCE(CAST(GLENTRY_VENDORID AS VARCHAR(50)), '')                                         AS DONOTIMPORT_2,
        'PROJECT_ID | ' + GLENTRY_PROJECTID                                                           AS DONOTIMPORT_3,
        DONOTIMPORT3                                                                                AS DONOTIMPORT_4,
        DONOTIMPORT4                                                                                AS DONOTIMPORT_5,
        DONOTIMPORT5                                                                                AS DONOTIMPORT_6,
        DESCRIPTION                                                                                 AS DONOTIMPORT_7,
		DONOTIMPORT11                                                                               AS DONOTIMPORT_8,
		DONOTIMPORT12                                                                               AS DONOTIMPORT_9,
        -- ── Batch helper columns (excluded from final SELECT) ─────────────────
        DESCRIPTION                                                                                 AS GROUPING_DESC,
        ENTRY_OFFSET                                                                                AS ENTRY_OFFSET_SORT
    FROM (
        SELECT
            ENTRY.*,
            ROW_NUMBER() OVER (
                PARTITION BY ENTRY.DESCRIPTION
                ORDER BY ENTRY.DESCRIPTION, ENTRY.ENTRY_OFFSET, ENTRY.ACCT_NO
            ) AS LINE_NO_REV
        FROM (

            -- =================================================================
            -- DEBIT SIDE (ENTRY_OFFSET = '0')
            -- =================================================================
            SELECT
                JCT.Data_Folder_Id AS DONOTIMPORT1,
                'OBJ' AS JOURNAL,
                JCT.Accounting_Date AS DATE,
                ISNULL(JT.NEW_JOB_ID, '') + ' | Acct Date: ' + ISNULL(CONVERT(VARCHAR(10), JCT.Accounting_Date, 120), '') + ' | Application of Origin: ' +
                    ISNULL(JCT.Application_of_Origin, '') + ' | Batch: ' + ISNULL(JCT.Batch, '') AS DESCRIPTION,
                '' AS REFERENCE_NO,
                COALESCE(COALESCE(FFULL.NEW_BASE_ACCOUNT, FNF.NEW_BASE_ACCOUNT),'(MISSING)') AS ACCT_NO,
                COALESCE(JT.NEW_ENTITY_ID, '') AS LOCATION_ID,
                COALESCE(JT.NEW_DEPARTMENT_ID, '') AS DEPT_ID,
                COALESCE(JT.NEW_CLASS_ID, '') AS CLASS_ID,
                CASE WHEN JCT.Application_of_Origin = 'AP' THEN JCT.Invoice ELSE JCT.AR_INVOICE END AS DOCUMENT,
                ISNULL(REPLACE(JCT.Description, ',', ''), '') + CASE WHEN ISNULL(JCT.Vendor, '') <> '' THEN ' Vendor ID: ' + JCT.Vendor ELSE '' END + CASE WHEN ISNULL(JCT.Invoice, '') <> '' THEN ' AP Inv ID: ' + JCT.Invoice ELSE '' END + CASE WHEN ISNULL(JCT.Job, '') <> '' THEN ' Job ID: ' + JCT.Job ELSE '' END + CASE WHEN ISNULL(JCT.Customer, '') <> '' THEN ' Customer ID: ' + JCT.Customer ELSE '' END + CASE WHEN ISNULL(JCT.AR_Invoice, '') <> '' THEN ' Cust Inv ID: ' + JCT.AR_Invoice ELSE '' END AS MEMO,
                SUM(CASE WHEN JCT.Amount > 0 THEN JCT.Amount ELSE 0 END) AS DEBIT,
                SUM(CASE WHEN JCT.AMOUNT < 0 THEN (JCT.AMOUNT * -1) ELSE 0 END) AS CREDIT,
                SUM(CASE WHEN JCT.Amount > 0 THEN JCT.Amount ELSE 0 END) - SUM(CASE WHEN JCT.AMOUNT < 0 THEN (JCT.AMOUNT * -1) ELSE 0 END) AS DONOTIMPORT,
                'POSTED' AS STATUS,
                JT.NEW_JOB_ID AS GLENTRY_PROJECTID,
                COALESCE(TCC.NEW_COST_CODE_ID, JCT.Cost_Code, '') AS GLENTRY_TASKID,
                COALESCE(TCT.NEW_COST_TYPE_ID, JCT.Category, '') AS GLENTRY_COSTTYPEID,
                COALESCE(TV.NEW_VENDOR_ID, '') AS GLENTRY_VENDORID,
                'LGY_JOB | ' + JCT.Job AS DONOTIMPORT3,
                'LGY_EXTRA | ' + JCT.Extra AS DONOTIMPORT4,
                'LGY_DB_ACCT | ' + JCT.Debit_Account AS DONOTIMPORT5,
                'LGY_CR_ACCT | ' + JCT.Credit_Account AS DONOTIMPORT6,
                JCT.Extra AS DONOTIMPORT7,
                JCT.Cost_Code AS DONOTIMPORT8,
                JCT.Category AS DONOTIMPORT9,
                JCT.Vendor AS DONOTIMPORT10,
				'LGY_COST_CODE | ' + JCT.Cost_Code AS DONOTIMPORT11,
				'LGY_COST_TYPE | ' + JCT.Category AS DONOTIMPORT12,
                '0' AS ENTRY_OFFSET
            FROM
                [s300].[JCT_CURRENT__TRANSACTION] JCT
                LEFT JOIN [s300].[JCM_MASTER__JOB] MJ ON JCT.JOB = MJ.JOB AND JCT.Data_Folder_ID = MJ.Data_Folder_ID
                LEFT JOIN [MAP].[T_TRANS_JOB] JT ON JCT.Job = JT.LEGACY_JOB_ID AND JCT.Extra = JT.LEGACY_EXTRA_ID AND JCT.Data_Folder_Id = JT.DATA_FOLDER_ID
                LEFT JOIN [MAP].[T_MASTER_ACCOUNT] MA ON JCT.Debit_Account = MA.Account AND JCT.Data_Folder_Id = MA.Data_Folder_Id
                LEFT JOIN MAP.T_TRANS_BASEACCT AS FFULL
                    ON FFULL.ACCT_MATCH_TYPE = 'FULL'
                    AND FFULL.LEGACY_BASE_ACCOUNT = MA.ACCOUNT
                    AND FFULL.Data_Folder_Id = MA.Data_Folder_Id
                LEFT JOIN MAP.T_TRANS_BASEACCT AS FNF
                    ON FNF.ACCT_MATCH_TYPE = 'BASE'
                    AND FNF.LEGACY_BASE_ACCOUNT = MA.BASEACCOUNT
                    AND FNF.Data_Folder_Id = MA.Data_Folder_Id
                    AND FFULL.LEGACY_BASE_ACCOUNT IS NULL
                LEFT JOIN (
                    SELECT DISTINCT LEGACY_DEPARTMENT_ID, NEW_DEPARTMENT_ID, Data_Folder_Id
                    FROM [MAP].[T_TRANS_DEPARTMENT]
                    WHERE LEGACY_DEPARTMENT_ID != '' AND NEW_DEPARTMENT_ID != ''
                ) DEPT ON MA.PREFIXABC = DEPT.LEGACY_DEPARTMENT_ID AND MA.Data_Folder_Id = DEPT.Data_Folder_ID
                LEFT JOIN (
                    SELECT DISTINCT LEGACY_LOCATION_ID, NEW_LOCATION_ID, Data_Folder_Id
                    FROM [MAP].[T_TRANS_LOCATION]
                    WHERE LEGACY_LOCATION_ID != '' AND NEW_LOCATION_ID != ''
                ) LOC ON MA.PREFIXABC = LOC.LEGACY_LOCATION_ID AND MA.Data_Folder_Id = LOC.Data_Folder_Id
                LEFT JOIN [MAP].[T_TRANS_COST_CODE] TCC ON JCT.Cost_Code = TCC.LEGACY_COST_CODE_ID AND JCT.Data_Folder_Id = TCC.DATA_FOLDER_ID
                LEFT JOIN [MAP].[T_TRANS_COST_TYPE] TCT ON JCT.Category = TCT.LEGACY_COST_TYPE_ID AND JCT.Data_Folder_Id = TCT.DATA_FOLDER_ID
                LEFT JOIN [MAP].[T_TRANS_VENDOR] TV ON JCT.Vendor = TV.LEGACY_VENDOR_ID AND JCT.Data_Folder_Id = TV.DATA_FOLDER_ID
            WHERE
                JT.INCLUDE_JOB = 1
                AND JCT.Commitment = ''
                AND JCT.Accounting_Date <= @Cutoff_Date
                AND JCT.Transaction_Type IN ('AP cost', 'EQ cost', 'JC cost', 'PR cost', 'IV cost', 'SM cost')
                --AND JCT.Job LIKE('%BMSB-01-29%')
                AND JCT.Debit_Account != ''
            GROUP BY
                JCT.Data_Folder_Id,
                JCT.Accounting_Date,
                ISNULL(JT.NEW_JOB_ID, '') + ' | Acct Date: ' + ISNULL(CONVERT(VARCHAR(10), JCT.Accounting_Date, 120), '') + ' | Application of Origin: ' + ISNULL(JCT.Application_of_Origin, '') + ' | Batch: ' + ISNULL(JCT.Batch, ''),
                COALESCE(COALESCE(FFULL.NEW_BASE_ACCOUNT, FNF.NEW_BASE_ACCOUNT), '(MISSING)'),
                COALESCE(JT.NEW_ENTITY_ID, ''),
                COALESCE(JT.NEW_DEPARTMENT_ID, ''),
                COALESCE(JT.NEW_CLASS_ID, ''),
                CASE WHEN JCT.Application_of_Origin = 'AP' THEN JCT.Invoice ELSE JCT.AR_INVOICE END,
                ISNULL(REPLACE(JCT.Description, ',', ''), '') + CASE WHEN ISNULL(JCT.Vendor, '') <> '' THEN ' Vendor ID: ' + JCT.Vendor ELSE '' END + CASE WHEN ISNULL(JCT.Invoice, '') <> '' THEN ' AP Inv ID: ' + JCT.Invoice ELSE '' END + CASE WHEN ISNULL(JCT.Job, '') <> '' THEN ' Job ID: ' + JCT.Job ELSE '' END + CASE WHEN ISNULL(JCT.Customer, '') <> '' THEN ' Customer ID: ' + JCT.Customer ELSE '' END + CASE WHEN ISNULL(JCT.AR_Invoice, '') <> '' THEN ' Cust Inv ID: ' + JCT.AR_Invoice ELSE '' END,
                JT.NEW_JOB_ID,
                COALESCE(TCC.NEW_COST_CODE_ID, JCT.Cost_Code, ''),
                COALESCE(TCT.NEW_COST_TYPE_ID, JCT.Category, ''),
                COALESCE(TV.NEW_VENDOR_ID, ''),
                'LGY_JOB | ' + JCT.Job,
                'LGY_EXTRA | ' + JCT.Extra,
                'LGY_DB_ACCT | ' + JCT.Debit_Account,
                'LGY_CR_ACCT | ' + JCT.Credit_Account,
				'LGY_COST_CODE | ' + JCT.Cost_Code,
				'LGY_COST_TYPE | ' + JCT.Category,
                JCT.Extra,
                JCT.Cost_Code,
                JCT.Category,
                JCT.Vendor

            UNION ALL

            -- =================================================================
            -- CREDIT / OFFSET SIDE (ENTRY_OFFSET = '1')
            -- =================================================================
            SELECT
                JCT.Data_Folder_Id AS DONOTIMPORT1,
                'OBJ' AS JOURNAL,
                JCT.Accounting_Date AS DATE,
                ISNULL(JT.NEW_JOB_ID, '') + ' | Acct Date: ' + ISNULL(CONVERT(VARCHAR(10), JCT.Accounting_Date, 120), '') + ' | Application of Origin: ' +
                    ISNULL(JCT.Application_of_Origin, '') + ' | Batch: ' + ISNULL(JCT.Batch, '') AS DESCRIPTION,
                '' AS REFERENCE_NO,
                COALESCE(COALESCE(FFULL.NEW_BASE_ACCOUNT, FNF.NEW_BASE_ACCOUNT),'(MISSING)') AS ACCT_NO,
                COALESCE(JT.NEW_ENTITY_ID, '') AS LOCATION_ID,
                COALESCE(JT.NEW_DEPARTMENT_ID, '') AS DEPT_ID,
                COALESCE(JT.NEW_CLASS_ID, '') AS CLASS_ID,
                --CASE WHEN JCT.Application_of_Origin = 'AP' THEN JCT.Invoice ELSE JCT.AR_INVOICE END AS DOCUMENT,
				'' AS DOCUMENT,
                ISNULL(REPLACE(JCT.Description, ',', ''), '') + CASE WHEN ISNULL(JCT.Vendor, '') <> '' THEN ' Vendor ID: ' + JCT.Vendor ELSE '' END + CASE WHEN ISNULL(JCT.Invoice, '') <> '' THEN ' AP Inv ID: ' + JCT.Invoice ELSE '' END + CASE WHEN ISNULL(JCT.Job, '') <> '' THEN ' Job ID: ' + JCT.Job ELSE '' END + CASE WHEN ISNULL(JCT.Customer, '') <> '' THEN ' Customer ID: ' + JCT.Customer ELSE '' END + CASE WHEN ISNULL(JCT.AR_Invoice, '') <> '' THEN ' Cust Inv ID: ' + JCT.AR_Invoice ELSE '' END AS MEMO,
                SUM(CASE WHEN JCT.AMOUNT < 0 THEN (JCT.AMOUNT * -1) ELSE 0 END) AS DEBIT,
                SUM(CASE WHEN JCT.Amount > 0 THEN JCT.Amount ELSE 0 END) AS CREDIT,
                SUM(CASE WHEN JCT.AMOUNT < 0 THEN (JCT.AMOUNT * -1) ELSE 0 END) - SUM(CASE WHEN JCT.Amount > 0 THEN JCT.Amount ELSE 0 END) AS DONOTIMPORT,
                'POSTED' AS STATUS,
                JT.NEW_JOB_ID AS GLENTRY_PROJECTID,
                --COALESCE(TCC.NEW_COST_CODE_ID, '') AS GLENTRY_TASKID,
                --COALESCE(TCT.NEW_COST_TYPE_ID, '') AS GLENTRY_COSTTYPEID,
                --COALESCE(TV.NEW_VENDOR_ID, '') AS GLENTRY_VENDORID,

				--'' AS GLENTRY_PROJECTID,
                '' AS GLENTRY_TASKID,
                '' AS GLENTRY_COSTTYPEID,
                '' AS GLENTRY_VENDORID,
                --'LGY_JOB | ' + JCT.Job AS DONOTIMPORT3,
                --'LGY_EXTRA | ' + JCT.Extra AS DONOTIMPORT4,
                --'LGY_DB_ACCT | ' + JCT.Debit_Account AS DONOTIMPORT5,
                --'LGY_CR_ACCT | ' + JCT.Credit_Account AS DONOTIMPORT6,
                --JCT.Extra AS DONOTIMPORT7,
                --JCT.Cost_Code AS DONOTIMPORT8,
                --JCT.Category AS DONOTIMPORT9,
                --JCT.Vendor AS DONOTIMPORT10,
				'' AS DONOTIMPORT3,
                '' AS DONOTIMPORT4,
                '' AS DONOTIMPORT5,
                '' AS DONOTIMPORT6,
                '' AS DONOTIMPORT7,
                '' AS DONOTIMPORT8,
                '' AS DONOTIMPORT9,
                '' AS DONOTIMPORT10,
				'' DONOTIMPORT11,
				'' DONOTIMPORT12,
                '1' AS ENTRY_OFFSET
            FROM
                [s300].[JCT_CURRENT__TRANSACTION] JCT
                LEFT JOIN [s300].[JCM_MASTER__JOB] MJ ON JCT.JOB = MJ.JOB AND JCT.Data_Folder_ID = MJ.Data_Folder_ID
                LEFT JOIN [MAP].[T_TRANS_JOB] JT ON JCT.Job = JT.LEGACY_JOB_ID AND JCT.Extra = JT.LEGACY_EXTRA_ID AND JCT.Data_Folder_Id = JT.DATA_FOLDER_ID
                LEFT JOIN [MAP].[T_MASTER_ACCOUNT] MA ON JCT.Debit_Account = MA.Account AND JCT.Data_Folder_Id = MA.Data_Folder_Id
                LEFT JOIN MAP.T_TRANS_BASEACCT AS FFULL
                    ON FFULL.ACCT_MATCH_TYPE = 'FULL'
                    AND FFULL.LEGACY_BASE_ACCOUNT = MA.ACCOUNT
                    AND FFULL.Data_Folder_Id = MA.Data_Folder_Id
                LEFT JOIN MAP.T_TRANS_BASEACCT AS FNF
                    ON FNF.ACCT_MATCH_TYPE = 'BASE'
                    AND FNF.LEGACY_BASE_ACCOUNT = MA.BASEACCOUNT
                    AND FNF.Data_Folder_Id = MA.Data_Folder_Id
                    AND FFULL.LEGACY_BASE_ACCOUNT IS NULL
                LEFT JOIN (
                    SELECT DISTINCT LEGACY_DEPARTMENT_ID, NEW_DEPARTMENT_ID, Data_Folder_Id
                    FROM [MAP].[T_TRANS_DEPARTMENT]
                    WHERE LEGACY_DEPARTMENT_ID != '' AND NEW_DEPARTMENT_ID != ''
                ) DEPT ON MA.PREFIXABC = DEPT.LEGACY_DEPARTMENT_ID AND MA.Data_Folder_Id = DEPT.Data_Folder_ID
                LEFT JOIN (
                    SELECT DISTINCT LEGACY_LOCATION_ID, NEW_LOCATION_ID, Data_Folder_Id
                    FROM [MAP].[T_TRANS_LOCATION]
                    WHERE LEGACY_LOCATION_ID != '' AND NEW_LOCATION_ID != ''
                ) LOC ON MA.PREFIXABC = LOC.LEGACY_LOCATION_ID AND MA.Data_Folder_Id = LOC.Data_Folder_Id
                LEFT JOIN [MAP].[T_TRANS_COST_CODE] TCC ON JCT.Cost_Code = TCC.LEGACY_COST_CODE_ID AND JCT.Data_Folder_Id = TCC.DATA_FOLDER_ID
                LEFT JOIN [MAP].[T_TRANS_COST_TYPE] TCT ON JCT.Category = TCT.LEGACY_COST_TYPE_ID AND JCT.Data_Folder_Id = TCT.DATA_FOLDER_ID
                LEFT JOIN [MAP].[T_TRANS_VENDOR] TV ON JCT.Vendor = TV.LEGACY_VENDOR_ID AND JCT.Data_Folder_Id = TV.DATA_FOLDER_ID
            WHERE
                JT.INCLUDE_JOB = 1
                AND JCT.Commitment = ''
                AND JCT.Accounting_Date <= @Cutoff_Date
                AND JCT.Transaction_Type IN ('AP cost', 'EQ cost', 'JC cost', 'PR cost', 'IV cost', 'SM cost')
                --AND JCT.Job LIKE('%BMSB-01-29%')
                AND JCT.Debit_Account != ''
            GROUP BY
                JCT.Data_Folder_Id,
                JCT.Accounting_Date,
                ISNULL(JT.NEW_JOB_ID, '') + ' | Acct Date: ' + ISNULL(CONVERT(VARCHAR(10), JCT.Accounting_Date, 120), '') + ' | Application of Origin: ' + ISNULL(JCT.Application_of_Origin, '') + ' | Batch: ' + ISNULL(JCT.Batch, ''),
                COALESCE(COALESCE(FFULL.NEW_BASE_ACCOUNT, FNF.NEW_BASE_ACCOUNT), '(MISSING)'),
                COALESCE(JT.NEW_ENTITY_ID, ''),
                COALESCE(JT.NEW_DEPARTMENT_ID, ''),
                COALESCE(JT.NEW_CLASS_ID, ''),
                CASE WHEN JCT.Application_of_Origin = 'AP' THEN JCT.Invoice ELSE JCT.AR_INVOICE END,
                ISNULL(REPLACE(JCT.Description, ',', ''), '') + CASE WHEN ISNULL(JCT.Vendor, '') <> '' THEN ' Vendor ID: ' + JCT.Vendor ELSE '' END + CASE WHEN ISNULL(JCT.Invoice, '') <> '' THEN ' AP Inv ID: ' + JCT.Invoice ELSE '' END + CASE WHEN ISNULL(JCT.Job, '') <> '' THEN ' Job ID: ' + JCT.Job ELSE '' END + CASE WHEN ISNULL(JCT.Customer, '') <> '' THEN ' Customer ID: ' + JCT.Customer ELSE '' END + CASE WHEN ISNULL(JCT.AR_Invoice, '') <> '' THEN ' Cust Inv ID: ' + JCT.AR_Invoice ELSE '' END,
                JT.NEW_JOB_ID
                --COALESCE(TCC.NEW_COST_CODE_ID, ''),
                --COALESCE(TCT.NEW_COST_TYPE_ID, ''),
                --COALESCE(TV.NEW_VENDOR_ID, ''),
                --'LGY_JOB | ' + JCT.Job,
                --'LGY_EXTRA | ' + JCT.Extra,
                --'LGY_DB_ACCT | ' + JCT.Debit_Account,
                --'LGY_CR_ACCT | ' + JCT.Credit_Account,
                --JCT.Extra,
                --JCT.Cost_Code,
                --JCT.Category,
                --JCT.Vendor

        ) ENTRY
    ) ENTRY_MAIN
),

-- =========================================================================
-- For each unique DESCRIPTION group, compute the cumulative row count of
-- ALL groups that come before it alphabetically.  The first group gets 0.
-- =========================================================================
DescGroups AS (
    SELECT
        GROUPING_DESC,
        COALESCE(
            SUM(COUNT(*)) OVER (
                ORDER BY GROUPING_DESC
                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
            ),
            0
        ) AS PriorRows
    FROM AllData
    GROUP BY GROUPING_DESC
),

-- =========================================================================
-- Assign a batch number to every DESCRIPTION group.
-- Groups are never split — if a group straddles a boundary it stays whole,
-- so a batch may slightly exceed @BatchSize in that case.
-- =========================================================================
BatchAssign AS (
    SELECT
        GROUPING_DESC,
        FLOOR(PriorRows * 1.0 / @BatchSize) + 1 AS BatchNum
    FROM DescGroups
)

-- =========================================================================
-- Final output: ALL records, BATCH_NUM as first column, then original cols
-- =========================================================================
SELECT
    b.BatchNum           AS DONOTIMPORT, --THIS IS THE BATCH NUMBER IT WILL BE BROKEN OUT BY
    a.DONOTIMPORT_1      AS DONOTIMPORT,
    a.JOURNAL,
    a.DATE_COL           AS [DATE],
    a.DESCRIPTION_OUT    AS DESCRIPTION,
    a.REFERENCE_NO,
    a.LINE_NO,
    a.ACCT_NO,
    a.LOCATION_ID,
    a.DEPT_ID,
    a.CLASS_ID,
    a.DOCUMENT,
    a.MEMO_COL           AS MEMO,
	CASE WHEN a.DEBIT - a.CREDIT >= 0 THEN FORMAT(a.DEBIT - a.CREDIT, '0.00') ELSE '' END AS DEBIT,
	CASE WHEN a.DEBIT - a.CREDIT <  0 THEN FORMAT((a.DEBIT - a.CREDIT)*-1, '0.00') ELSE '' END AS CREDIT,
	a.DEBIT - a.CREDIT AS DONOTIMPORT,
    a.STATUS,
    a.GLENTRY_PROJECTID,
    a.GLENTRY_TASKID,
    a.GLENTRY_COSTTYPEID,
    a.GLENTRY_VENDORID,
    a.DONOTIMPORT_2      AS DONOTIMPORT,
    a.DONOTIMPORT_3      AS DONOTIMPORT,
    a.DONOTIMPORT_4      AS DONOTIMPORT,
    a.DONOTIMPORT_5      AS DONOTIMPORT,
    a.DONOTIMPORT_6      AS DONOTIMPORT,
    a.DONOTIMPORT_7      AS DONOTIMPORT,
    a.DONOTIMPORT_8      AS DONOTIMPORT,
	a.DONOTIMPORT_9      AS DONOTIMPORT
FROM 
	AllData a
	JOIN BatchAssign b ON a.GROUPING_DESC = b.GROUPING_DESC
WHERE
	a.DEBIT - a.CREDIT != 0
ORDER BY 
	b.BatchNum, 
	a.GROUPING_DESC, 
	a.ENTRY_OFFSET_SORT, 
	a.ACCT_NO;