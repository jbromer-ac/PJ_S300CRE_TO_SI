--MODEL AS OF 02/06/2026
--VENDORS ACH IMPORT
SELECT
	V.Data_Folder_Id AS DONOTIMPORT,
	TV.NEW_VENDOR_ID AS VENDOR_ID,
	CASE WHEN V.Default_Payment_Type = 'Electronic' THEN 'ACH' WHEN V.Default_Payment_Type = 'Check' THEN 'Printed Check' ELSE 'Unknown Type' END AS PAYMENT_METHOD,
	CASE WHEN V.Default_Payment_Type = 'Electronic' THEN 'T' WHEN V.Default_Payment_Type = 'Check' THEN 'F' ELSE 'Unknown Type' END AS ACH_ENABLE,
	RIGHT('000000000' + CAST(V.Bank_ID AS VARCHAR(20)), 9) AS ACH_BANK_ROUTING_NUMBER,
	BANK_ACCOUNT AS ACH_ACCOUNT_NUMBER,
	CASE WHEN V.Account_Type = 'Checking' THEN 'Checking Account' ELSE 'Savings Account' END AS ACH_ACCOUNT_TYPE,
	COALESCE(CASE WHEN V.Default_Payment_Type = 'Electronic' THEN 'CCD' END, '') AS ACH_SEC_CODE,
	CASE WHEN V.Default_Payment_Type = 'Electronic' THEN 'T' ELSE 'F' END AS PAYMENT_NOTIFICATION,
	TV.LEGACY_VENDOR_ID AS DONOTIMPORT
FROM
	[s300].[APM_MASTER__VENDOR] V
	LEFT JOIN [MAP].[T_TRANS_VENDOR] TV ON V.Vendor = TV.LEGACY_VENDOR_ID AND V.Data_Folder_Id = TV.Data_Folder_Id
	LEFT JOIN [MAP].[T_1099_TYPE] TTEN ON V.Form_Type_1099 = TTEN.FORM_TYPE_1099_DESC
	LEFT JOIN [MAP].[T_STATE] TS ON V.State = TS.STATE_ID
WHERE
	TV.INCLUDE_VENDOR = '1'
	AND CASE WHEN V.Default_Payment_Type = 'Electronic' THEN 'T' WHEN V.Default_Payment_Type = 'Check' THEN 'F' ELSE 'Unknown Type' END = 'T';
