--MODEL AS OF 05/05/2026 The customer translation was missing the customer DATA_FOLDER_ID in join
--OPEN AR IMPORT
DECLARE @AgingDate DATE; --AS OF DATE
SET @AgingDate = (
    SELECT CONVERT(DATE, F.FIELD_VALUE, 23)  -- 23 = yyyy-mm-dd
    FROM [MAP].[E_USEFUL_FIELDS] F
    WHERE F.FIELD_NAME = 'GL03_DETAIL_STOP'
);

DECLARE @AgingBasis VARCHAR(50) = 'Accounting date'; --OPTIONS ARE 'Accounting date', 'Invoice date' OR 'Due date'
DECLARE @IncludeRetainage BIT = 1; --Use 1 for TRUE, 2 for FALSE
DECLARE @IncludeFinanceCharges BIT = 1; --Use 1 for TRUE, 2 for FALSE
DECLARE @PrimaryID VARCHAR(50) = ''; --OPTIONS ARE '' OR 'Invoice'

SELECT
	DONOTIMPORT1,
	TRANSACTIONTYPE,
	COALESCE(FORMAT([DATE], 'yyyy-MM-dd'),'') AS [DATE],
	COALESCE(FORMAT(GLPOSTINGDATE, 'yyyy-MM-dd'),'') AS GLPOSTINGDATE,
	DOCUMENTNO,
	CUSTOMER_ID,
	TERMNAME,
	COALESCE(FORMAT(DATEDUE, 'yyyy-MM-dd'),'') AS DATEDUE,
	STATE,
	LINE,
	ITEMID,
	QUANTITY,
	UNIT,
	SUM(PRICE) AS PRICE, --THIS NEEDS TO BE AGGREGATED
	LOCATIONID,
	DEPARTMENTID,
	SODOCUMENTENTRY_CLASSID,
	SODOCUMENTENTRY_PROJECTID,
	PROJECTID,
	SODOCUMENTENTRY_CUSTOMERID,
	MAX(MEMO) AS MEMO, --AGGREGATE THIS AS MAX
	CONVERSIONTYPE,
	SODOCUMENTENTRY_RETAINAGEPERCENTAGE,
	SUM(SODOCUMENTENTRY_AMOUNTRETAINED) AS SODOCUMENTENTRY_AMOUNTRETAINED, --THIS NEEDS TO BE AGGREGATED
	DONOTIMPORT2,
	DONOTIMPORT3,
	DONOTIMPORT4,
	DONOTIMPORT5,
	DONOTIMPORT6,
	DONOTIMPORT7,
	DONOTIMPORT8,
	DONOTIMPORT9,
	DONOTIMPORT10,
	DONOTIMPORT11,
	DONOTIMPORT12,
	DONOTIMPORT13,
	DONOTIMPORT14,
	DONOTIMPORT15,
	DONOTIMPORT16
FROM
	(SELECT
		ACT.Data_Folder_Id AS DONOTIMPORT1,
		'Startup AR' AS TRANSACTIONTYPE,
		CASE WHEN ACT.Transaction_Type IN ('Invoice','Invoice adjustment','Issued invoice') THEN CASE WHEN ACT.Amount_Type IN ('Retainage billed','Retainage released') THEN ACT.Related_Status_Date ELSE ARA.Status_Date END ELSE ACT.Accounting_Date END AS DATE,
		ACT.Accounting_Date AS GLPOSTINGDATE,
		CASE
			WHEN ACT.Transaction_Type IN ('TT_IINV','TT_MINV','Issued invoice','Invoice') THEN
				CASE
					WHEN @IncludeRetainage = 1
						AND ACT.Amount_Type IN ('AT_RETR','AT_TRR','Retainage released','Tax retainage relsed')
					THEN CONCAT('RetBill: ', P.PrimaryIdValue)
					ELSE P.PrimaryIdValue
				END

			WHEN ACT.Transaction_Type IN ('TT_IADJ','TT_PADJ','Invoice adjustment','Cash rcpt adjustmnt') THEN
				COALESCE(CASE WHEN ACT.Edit_Type = 'Edit' THEN P.PrimaryIdValue ELSE ACT.Adjustment END, '')
			ELSE COALESCE(ACT.Cash_Receipt,'') END AS DOCUMENTNO,
		TC.NEW_CUSTOMER_ID AS CUSTOMER_ID,
		'' AS TERMNAME, --DONT NEED THIS IF WE PROVIDE DATEDUE
		ACT.Due_Date AS DATEDUE,
		'Pending' AS STATE,
		'1' AS LINE,
		'Revenue' AS ITEMID,
		'1' AS QUANTITY,
		'Each' AS UNIT,
		(CASE
			 WHEN ACT.Amount_Type NOT IN ('RETR','TRR')
				 THEN ROUND(ACT.Amount + ACT.Retainage, 2)
			 ELSE 0 END) +
			(CASE
				 WHEN @IncludeRetainage = 1
					  AND ACT.Amount_Type NOT IN ('RETG','TRET','ROB','TROB')
					 THEN ROUND(-ACT.Retainage, 2)
				 ELSE 0
			 END) AS PRICE, --THIS NEEDS TO BE AGGREGATED
		TE.NEW_ENTITY_ID AS LOCATIONID,
		'' AS DEPARTMENTID,
		'' AS SODOCUMENTENTRY_CLASSID,
		COALESCE(CASE WHEN TJ.INCLUDE_JOB = 1 THEN TJ.NEW_JOB_ID ELSE '' END, '') AS SODOCUMENTENTRY_PROJECTID,
		COALESCE(CASE WHEN TJ.INCLUDE_JOB = 1 THEN TJ.NEW_JOB_ID ELSE '' END, '') AS PROJECTID,
		TC.NEW_CUSTOMER_ID AS SODOCUMENTENTRY_CUSTOMERID,
		CASE WHEN ACT.Transaction_Type IN ('Invoice', 'Invoice adjustment', 'Issued invoice') THEN ACT.Description ELSE '' END AS MEMO, --AGGREGATE THIS AS MAX
		'Price' AS CONVERSIONTYPE,
		'' AS SODOCUMENTENTRY_RETAINAGEPERCENTAGE,
		CASE WHEN 1 = 1 AND ACT.Amount_Type NOT IN ('RETG','TRET','ROB','TROB') THEN ROUND(-ACT.Retainage, 2) ELSE 0 END  AS SODOCUMENTENTRY_AMOUNTRETAINED, --THIS NEEDS TO BE AGGREGATED
		'LG_JOB_ID|' + ACT.Job AS DONOTIMPORT2,
		'LG_JOB_DESC|' + J.Description AS DONOTIMPORT3,
		'LG_TRANS_TYPE|' + CASE WHEN ACT.Transaction_Type IN('Issued invoice', 'Invoice') THEN (CASE WHEN ACT.Amount_Type = 'Tax retainage relsed' THEN 'Tax Ret. Rel.' ELSE ((CASE WHEN ACT.Amount_Type = 'Retainage released' THEN 'Ret. Released' ELSE ('Invoice') END)) END) ELSE (CASE WHEN ACT.Transaction_Type = 'Invoice adjustment' THEN (CASE WHEN ACT.EDIT_Type = 'Edit' THEN 'Invoice' ELSE ACT.Adjustment_Type END) ELSE (CASE WHEN ACT.Transaction_Type = 'Cash recpt adjustmnt' THEN (CASE WHEN ACT.Status_Type = 'Customer cash recpt' THEN 'Cust Cash Recpt' ELSE ACT.Adjustment_Type END)ELSE ACT.Transaction_Type END) END) END AS DONOTIMPORT4,
		'LG_STS_TYP|' + ACT.Status_Type AS DONOTIMPORT5,
		'LG_TRN_TYP|' + ACT.Transaction_Type AS DONOTIMPORT6,
		'LG_AMT_TYP|' + ACT.Amount_Type AS DONOTIMPORT7,
		'LG_EDT_TYP|' + ACT.EDIT_Type AS DONOTIMPORT8,
		'LG_ACT_TYP|' + ARA.Activity_Type AS DONOTIMPORT9,
		'LG_EXTRA|' + ACT.Extra AS DONOTIMPORT10,
		'LG_CRD_ACCT|' + ACT.Credit_Account__Accrual AS DONOTIMPORT11,
		'LG_TRAN_TYPE|' + CASE WHEN ACT.Transaction_Type IN ('Issued invoice','Invoice') THEN CASE ACT.Amount_Type
						 WHEN 'Tax retainage relsed' THEN 'Tax Ret. Rel.'
						 WHEN 'Retainage released'   THEN 'Ret. Released'
						 ELSE 'Invoice'
						 END
						 WHEN ACT.Transaction_Type = 'Invoice adjustment'
						 THEN CASE WHEN ACT.Edit_Type = 'Edit' THEN 'Invoice' ELSE COALESCE(ACT.Adjustment_Type,'') END
						 WHEN ACT.Transaction_Type = 'Cash recpt adjustmnt'
						 THEN CASE WHEN ACT.Status_Type = 'Customer cash recpt' THEN 'Cust Cash Recpt' ELSE COALESCE(ACT.Adjustment_Type,'') END
						 ELSE ACT.Transaction_Type
						 END AS DONOTIMPORT12,
		'LG_CUST|' + ACT.Customer AS DONOTIMPORT13,
		'LG_CUST_NME|' + C.Name AS DONOTIMPORT14,
		'LG_CONT|' + ACT.Contract AS DONOTIMPORT15,
		'LG_CONT_ITM|' + ACT.Contract_Item AS DONOTIMPORT16
	FROM
		[s300].[ART_CURRENT__TRANSACTION] ACT
		CROSS APPLY (SELECT PrimaryIdValue = COALESCE(CASE WHEN @PrimaryID = 'Invoice' THEN ACT.Invoice ELSE ACT.Draw END, '')) P
		LEFT JOIN [s300].[ARM_MASTER__CUSTOMER] C ON ACT.Customer = C.Customer AND ACT.Data_Folder_Id = C.Data_Folder_Id
		LEFT JOIN [s300].[ARA_ACTIVITY__ACTIVITY] ARA ON
			ACT.Customer = ARA.Customer and
			ACT.Status_Type = ARA.Status_Type and
			ACT.Status_Date = ARA.Status_Date and
			ACT.Status_Seq = ARA.Status_Seq and
			ACT.Actvty_Seq = ARA.Actvty_Seq and
			ACT.Data_Folder_Id = ARA.Data_Folder_Id
		LEFT JOIN [s300].[JCM_MASTER__JOB] J ON ACT.Job = J.Job and ACT.Data_Folder_Id = J.Data_Folder_Id
		LEFT JOIN [MAP].[T_TRANS_JOB] TJ ON ACT.Data_Folder_Id = TJ.DATA_FOLDER_ID AND ACT.Job = TJ.LEGACY_JOB_ID
		LEFT JOIN [MAP].[T_TRANS_ENTITY] TE ON ACT.Data_Folder_Id = TE.DATA_FOLDER_ID
		LEFT JOIN [MAP].[T_TRANS_CUSTOMER] TC ON ACT.Customer = TC.LEGACY_CUSTOMER_ID AND ACT.Data_Folder_Id = TC.DATA_FOLDER_ID
	WHERE
		--1ST FILTER
		(CASE
			WHEN @AgingBasis = 'Invoice date' THEN
				CASE
					WHEN ACT.Transaction_Type IN ('Issued invoice','Invoice') THEN
						CASE
							WHEN ACT.Amount_Type IN ('Retainage released','Tax retainage relsed') THEN ACT.Related_Status_Date
							ELSE ARA.Status_Date
						END
					WHEN ACT.Transaction_Type IN ('Cash receipt','Cash recpt adjustmnt') THEN ACT.Deposit_Date
					WHEN ACT.Transaction_Type = 'Invoice adjustment' THEN
						CASE
							WHEN ACT.Adjustment_Type = 'Not used' AND ACT.Amount_Type NOT IN ('Retainage released','Tax retainage relsed') THEN ARA.Status_Date
							WHEN ACT.Adjustment_Type = 'Not used' AND ACT.Amount_Type     IN ('Retainage released','Tax retainage relsed') THEN ACT.Related_Status_Date
							ELSE ACT.Reference_Date
						END
					ELSE ACT.Reference_Date
				END

			WHEN @AgingBasis = 'Accounting date' THEN
				ISNULL(ACT.Accounting_Date, CONVERT(date,'19000101'))  -- Crystal Date(0)

			WHEN @AgingBasis = 'Due date' THEN
				CASE
					WHEN ARA.Due_Date <> CONVERT(date,'19000101') THEN ARA.Due_Date  -- Crystal Date(0)
					ELSE ACT.Due_Date
				END

			ELSE CONVERT(date,'19000101')  -- fallback if @AgingBasis is unexpected
		END <= @AgingDate)

		AND

		--2ND FILTER
		(CASE
			WHEN @IncludeFinanceCharges = 0 AND ACT.Amount_Type = 'FC' THEN 0
			ELSE 1
		 END = 1)

		 AND

		--3RD Filter
		ACT.Amount_Type NOT IN('NSF Bank charge', 'Stored materals')) A1
GROUP BY
	DONOTIMPORT1,
	TRANSACTIONTYPE,
	[DATE],
	GLPOSTINGDATE,
	DOCUMENTNO,
	CUSTOMER_ID,
	TERMNAME,
	DATEDUE,
	STATE,
	LINE,
	ITEMID,
	QUANTITY,
	UNIT,
	LOCATIONID,
	DEPARTMENTID,
	SODOCUMENTENTRY_CLASSID,
	SODOCUMENTENTRY_PROJECTID,
	PROJECTID,
	SODOCUMENTENTRY_CUSTOMERID,
	CONVERSIONTYPE,
	SODOCUMENTENTRY_RETAINAGEPERCENTAGE,
	DONOTIMPORT2,
	DONOTIMPORT3,
	DONOTIMPORT4,
	DONOTIMPORT5,
	DONOTIMPORT6,
	DONOTIMPORT7,
	DONOTIMPORT8,
	DONOTIMPORT9,
	DONOTIMPORT10,
	DONOTIMPORT11,
	DONOTIMPORT12,
	DONOTIMPORT13,
	DONOTIMPORT14,
	DONOTIMPORT15,
	DONOTIMPORT16
