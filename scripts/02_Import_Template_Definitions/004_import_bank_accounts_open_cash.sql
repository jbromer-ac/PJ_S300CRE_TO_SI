SELECT
	RT.Data_Folder_Id AS DONOTIMPORT,
	RT.Bank_Account AS DONOTIMPORT,
	RT.Accounting_Date AS DATE,
	RT.[Check] AS DOC_NO,
	CASE WHEN RT.Subtraction > 0 THEN 'checkdebit' ELSE 'depositcredit' END AS TYPE,
	CASE WHEN RT.Subtraction > 0 THEN RT.Subtraction ELSE RT.Addition END AS AMOUNT,
	RT.Payee AS PAYEE,
	RT.Description AS DESCRIPTION,
	'LAST_RECON:' + COALESCE(FORMAT(MA.Last_Reconciled_Date, 'yyyy-MM-dd'), '') AS DONOTIMPORT,
	RT.[Subtraction] AS DONOTIMPORT,
	RT.Addition AS DONOTIMPORT,
	MA.General_Cash_Account AS DONOTIMPORT,
	RT.Reconcile_in_Progress AS DONOTIMPORT
FROM
	[s300].[CMT_REGISTER__TRANSACTION] RT
	LEFT JOIN [s300].[CMM_MASTER__BANK_ACCOUNT] MA ON RT.Data_Folder_Id = MA.Data_Folder_Id AND RT.Bank_Account = MA.Bank_Account
WHERE 
	RT.Reconciliation_Status IN ('C', 'O')
    --AND MA.Last_Reconciled_Date >= '2026-01-01'