--GL BASE ACCOUNT IMPORT
--AS OF 05-28-2026
SELECT
    GLB.Data_Folder_Id AS DONOTIMPORT,
    GLB.Base_Account AS GL_LEGACY_ID,
    GLB.Base_Account AS ACCT_NO,
    GLB.Base_Account_Title AS ACCT_NAME,
    EAT.ACCT_TYPE,
    EAT.NORMAL_BALANCE,
    EAT.CLOSEABLE,
    CASE
        WHEN GLB.Base_Account_Type IN ('Income', 'Cost', 'Expense', 'Other income')
            THEN 'Retained Earnings Account Here Later'
        ELSE ''
    END AS CLOSETOACCT_NO,
    '' AS CATEGORY,
    'T' AS ACTIVE,
    GLB.Base_Account_Type AS DONOTIMPORT
FROM
    [s300].[GLM_MASTER__BASE_ACCOUNT] GLB
    LEFT JOIN MAP.E_ACCT_TYPE EAT
        ON GLB.Base_Account_Type = EAT.Base_Account_Type;