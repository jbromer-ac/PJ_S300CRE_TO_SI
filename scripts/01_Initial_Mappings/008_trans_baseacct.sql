--ACCOUNT TRANSLATION
--Updated 05/28/2026 for bank account handling with full match
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'MAP' AND TABLE_NAME = 'T_TRANS_BASEACCT') BEGIN
CREATE TABLE MAP.T_TRANS_BASEACCT (	[Data_Folder_Id] [varchar](100) NULL, [Legacy_Base_Account] [varchar](100) NULL, [New_Base_Account] [varchar](100) NULL, [Note] [varchar](100) NULL, [ACCT_MATCH_TYPE] [nvarchar](50) NULL);
INSERT INTO MAP.T_TRANS_BASEACCT ([Data_Folder_Id], [Legacy_Base_Account], [New_Base_Account], [Note], [ACCT_MATCH_TYPE])
SELECT 
	COMB.* 
FROM (
	SELECT DISTINCT
		COA.Data_Folder_Id AS Data_Folder_Id,
		COA.BaseAccount AS Legacy_Base_Account,
		COA.BaseAccount AS New_Base_Account,
		COA.Account_Title AS Note,
		'BASE' AS ACCT_MATCH_TYPE
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
		'FULL' AS ACCT_MATCH_TYPE
	FROM
		[s300].[CMM_MASTER__BANK_ACCOUNT] BA) COMB
END
GO


