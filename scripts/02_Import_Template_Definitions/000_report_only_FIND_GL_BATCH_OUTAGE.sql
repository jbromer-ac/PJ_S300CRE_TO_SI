SELECT
	CT.Data_Folder_Id,
	CT.Batch,
	EOMONTH(CT.Accounting_Date) AS Period,
	CT.Application_of_Origin,
	SUM(CT.Debit) AS DEBIT_AMT,
	SUM(CT.Credit)*-1 AS CREDIT_AMT,
	ROUND(SUM(CT.Debit + CT.Credit),2) AS NET_AMT
FROM   
	[s300].[GLT_CURRENT__TRANSACTION] CT
WHERE
	CT."Accrual_or_Cash" = 'Accrual'
	--AND CT.Batch = '130019'
	AND CT.Accounting_Date <= '2023-12-31'
GROUP BY 
	CT.Data_Folder_Id,
	CT.Batch,
	EOMONTH(CT.Accounting_Date),
	CT.Application_of_Origin
HAVING
	ROUND(SUM(CT.Debit + CT.Credit),2) != 0
ORDER BY
	Data_Folder_Id,
	Batch,
	Period