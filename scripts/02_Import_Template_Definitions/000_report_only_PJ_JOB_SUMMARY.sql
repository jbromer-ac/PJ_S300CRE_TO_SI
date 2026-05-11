SELECT
	J.Data_Folder_Id AS DATA_FOLDER_ID,
	J.Job AS LEGACY_JOB_ID,
	TJ.NEW_JOB_ID AS JOB_ID,
	J.Description AS Description,
	J.Cost_Account_Prefix AS Cost_Account_Prefix,
	J.Original_Contract_Amount AS Original_Contract_Amount,
	J.JTD_Aprvd_Contract_Chgs AS JTD_Aprvd_Contract_Chgs,
	J.Revised_Contract_Amount AS Revised_Contract_Amount,
	J.JTD_Cost AS JTD_Cost,
	J.Extras_JTD_Cost AS Extras_JTD_Cost,
	J.Original_Estimate AS Original_Estimate,
	J.JTD_Aprvd_Estimate_Chgs AS JTD_Aprvd_Estimate_Chgs,
	J.Total_Estimate AS Total_Estimate,
	--J.Extras_Original_Estimate AS Extras_Original_Estimate,
	--J.Extras_JTD_Aprvd_Est_Chgs AS Extras_JTD_Aprvd_Est_Chgs,
	J.Extras_Total_Estimate AS Extras_Total_Estimate,
	J.JTD_Work_Billed AS JTD_Work_Billed,
	J.Extras_JTD_Work_Billed AS Extras_JTD_Work_Billed,
	J.Original_Commitment AS Original_Commitment,
	--J.Approved_Commitment_Change AS Approved_Commitment_Change,
	J.Revised_Commitment AS Revised_Commitment,
	J.Commitment_Invoiced AS Commitment_Invoiced,
	--J.Extra_Original_Commitment AS Extra_Original_Commitment,
	--J.Extra_Approved_Commitment_Changes AS Extra_Approved_Commitment_Changes,
	--J.Extra_Revised_Commitment AS Extra_Revised_Commitment,
	--J.Extra_Commitment_Invoiced AS Extra_Commitment_Invoiced,
	J.Extras_JTD_Aprvd_Cntrc_Chgs AS Extras_JTD_Aprvd_Cntrc_Chgs,
	J.Extras_Orig_Contract_Amount AS Extras_Orig_Contract_Amount,
	J.Extras_Revised_Contract_Amt  AS Extra_Revised_Contract_Amt,
	J.JTD_Retainage_Held AS JTD_Retainage_Held
FROM 
	[s300].[JCM_MASTER__JOB] J
	LEFT JOIN [MAP].[T_TRANS_JOB] TJ ON J.Data_Folder_Id = TJ.Data_Folder_Id AND J.Job = TJ.LEGACY_JOB_ID
WHERE
	TJ.INCLUDE_JOB = 1
ORDER BY
	J.Data_Folder_Id,
	TJ.NEW_JOB_ID;