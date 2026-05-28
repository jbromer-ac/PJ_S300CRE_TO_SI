--ACCOUNT TRANSLATION
--Updated 05/28/2026 for bank account handling with full match
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'MAP' AND TABLE_NAME = 'T_TRANS_BASEACCT') BEGIN
CREATE TABLE MAP.T_TRANS_BASEACCT (	
	[Data_Folder_Id] [varchar](100) NULL, 
	[Legacy_Base_Account] [varchar](100) NULL, 
	[Note] [varchar](100) NULL,
	[FIN_STMT] [varchar](2) NULL,
	[BALANCE] [varchar](2) NULL,	
	[CLOSEABLE] [varchar](1) NULL,
	[ACCT_TYPE] [varchar](75) NULL,
	[ACCT_MATCH_TYPE] [nvarchar](50) NULL,
	[REQUIRED] [nvarchar](1) NULL,
	[New_Base_Account] [varchar](100) NULL, 
	[CMO] [varchar](50) NULL, 
	[CATEGORY] [nvarchar](75) NULL);
INSERT INTO MAP.T_TRANS_BASEACCT (
	[Data_Folder_Id], 
	[Legacy_Base_Account], 
	[Note],
	[FIN_STMT],
	[BALANCE],	
	[CLOSEABLE],
	[ACCT_TYPE],
	[ACCT_MATCH_TYPE],
	[REQUIRED],
	[New_Base_Account], 
	[CMO], 
	[CATEGORY]
)
SELECT 
	COMB.Data_Folder_Id,
	COMB.Legacy_Base_Account,
	COMB.Note AS Description,
	CASE WHEN ACTP.ACCT_TYPE = 'N' THEN 'BS' ELSE 'IS' END AS FIN_STMT,
	ACTP.NORMAL_BALANCE AS BALANCE,
	ACTP.CLOSEABLE AS CLOSEABLE,
	ACTP.BASE_ACCOUNT_TYPE AS ACCT_TYPE,
	COMB.ACCT_MATCH_TYPE AS MAP_TYPE,
	1 AS [REQUIRED],
	COMB.New_Base_Account,
	'' AS CMO, --DROP DOWN WITH 3 options: Create, Merge, Omit
	'' AS CATEGORY --THIS WILL BE THE QUICKSTART CATEGORY
FROM (
	SELECT DISTINCT
		COA.Data_Folder_Id AS Data_Folder_Id,
		COA.BaseAccount AS Legacy_Base_Account,
		COA.BaseAccount AS New_Base_Account,
		COA.Account_Title AS Note,
		'BASE' AS ACCT_MATCH_TYPE,
		COA.BaseAccount
	FROM
		[MAP].[T_MASTER_ACCOUNT] COA
		--LEFT JOIN [s300].[GLM_MASTER__ACCOUNT_FORMAT] COAF ON COA.Data_Folder_Id = COAF.Data_Folder_Id;
		LEFT JOIN (SELECT DISTINCT BA.Data_Folder_Id, BA.General_Cash_Account FROM [s300].[CMM_MASTER__BANK_ACCOUNT] BA) BANK_EXCL 
			ON COA.Data_Folder_Id = BANK_EXCL.Data_Folder_Id AND COA.Account = BANK_EXCL.General_Cash_Account
	WHERE
		BANK_EXCL.General_Cash_Account IS NULL --This excludes BASE Account mapping for any accounts tied to bank accounts

	UNION ALL

	SELECT
		BA.Data_Folder_Id AS Data_Folder_Id,
		BA.General_Cash_Account AS Legacy_Base_Account,
		'(New GL Account Needed)' AS New_Base_Account,
		BA.Account_Type + ' | ' + BA.Description + ' | ' + BA.Bank_Name + ' | ' + BA.Bank_Account AS Note,
		'FULL' AS ACCT_MATCH_TYPE,
		COA.BaseAccount
	FROM
		[s300].[CMM_MASTER__BANK_ACCOUNT] BA
		LEFT JOIN [MAP].[T_MASTER_ACCOUNT] COA ON BA.Data_Folder_Id = COA.Data_Folder_Id AND BA.General_Cash_Account = COA.Account) COMB
	LEFT JOIN [s300].[GLM_MASTER__BASE_ACCOUNT] MBA ON COMB.Data_Folder_Id = MBA.Data_Folder_Id AND COMB.BaseAccount = MBA.Base_Account
	LEFT JOIN [MAP].[E_ACCT_TYPE] ACTP ON MBA.Base_Account_Type = ACTP.Base_Account_Type --NO NEED FOR DATA FOLDER ID HERE
		
END
GO