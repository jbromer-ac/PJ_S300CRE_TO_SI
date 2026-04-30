CREATE SCHEMA MAP;
GO

CREATE VIEW [MAP].[T_MASTER_ACCOUNT] AS
SELECT
    A.Data_Folder_Id,
    A.Account,
    A.Account_Title,
    A.Account_Type,

    X.PrefixA,
    X.PrefixB,
    X.PrefixC,
    X.PrefixAB,
    X.PrefixABC,
    X.BaseAccount,
    X.Suffix,

    CASE
        WHEN X.Suffix = '' THEN X.BaseAccount
        ELSE X.BaseAccount + '.' + X.Suffix
    END AS ACCT

FROM [s300].[GLM_MASTER__ACCOUNT] A
JOIN [s300].[GLM_MASTER__ACCOUNT_FORMAT] AM
    ON AM.Data_Folder_Id = A.Data_Folder_Id

-- Split left/right of decimal
CROSS APPLY (
    SELECT
        LeftPart  = LEFT(A.Account, CHARINDEX('.', A.Account + '.') - 1),
        RightPart = SUBSTRING(A.Account, CHARINDEX('.', A.Account + '.') + 1, 8000)
) P

-- Get Suffix (fixed)
CROSS APPLY (
    SELECT
        Suffix = RIGHT(P.RightPart, AM.Suffix_Length)
) S

-- Remove BaseAccount from right side of LeftPart
CROSS APPLY (
    SELECT
        BaseAccount = RIGHT(P.LeftPart, AM.Base_Account_Length),
        PrefixPart  = LEFT(P.LeftPart, LEN(P.LeftPart) - AM.Base_Account_Length)
) B

-- Now work RIGHT → LEFT through PrefixPart
CROSS APPLY (
    SELECT
        -- PrefixC (if exists)
        PrefixC = CASE
                    WHEN AM.Account_Prefix_ABC_Length <= AM.Account_Prefix_AB_Length THEN ''
                    ELSE RIGHT(B.PrefixPart, AM.Account_Prefix_ABC_Length - AM.Account_Prefix_AB_Length)
                  END
) C

CROSS APPLY (
    SELECT
        PrefixPart2 = CASE
                        WHEN AM.Account_Prefix_ABC_Length = 0 THEN B.PrefixPart
                        ELSE LEFT(B.PrefixPart, LEN(B.PrefixPart) - LEN(C.PrefixC))
                      END
) P2

CROSS APPLY (
    SELECT
        -- PrefixB
        PrefixB = CASE
                    WHEN AM.Account_Prefix_AB_Length <= AM.Account_Prefix_A_Length THEN ''
                    ELSE RIGHT(P2.PrefixPart2, AM.Account_Prefix_AB_Length - AM.Account_Prefix_A_Length)
                  END
) B2

CROSS APPLY (
    SELECT
        -- Remaining is PrefixA (variable length)
        PrefixA = LEFT(P2.PrefixPart2, LEN(P2.PrefixPart2) - LEN(B2.PrefixB))
) A2

CROSS APPLY (
    SELECT
        PrefixA = RTRIM(REPLACE(A2.PrefixA, '-', '')),
        PrefixB = RTRIM(REPLACE(B2.PrefixB, '-', '')),
        PrefixC = RTRIM(REPLACE(C.PrefixC, '-', '')),

        PrefixAB =
            CASE
                WHEN B2.PrefixB = '' THEN RTRIM(REPLACE(A2.PrefixA, '-', ''))
                ELSE RTRIM(REPLACE(A2.PrefixA, '-', '')) + '-' + RTRIM(REPLACE(B2.PrefixB, '-', ''))
            END,

        PrefixABC =
            CASE
                WHEN C.PrefixC = '' THEN
                    CASE
                        WHEN B2.PrefixB = '' THEN RTRIM(REPLACE(A2.PrefixA, '-', ''))
                        ELSE RTRIM(REPLACE(A2.PrefixA, '-', '')) + '-' + RTRIM(REPLACE(B2.PrefixB, '-', ''))
                    END
                ELSE RTRIM(REPLACE(A2.PrefixA, '-', '')) + '-' + RTRIM(REPLACE(B2.PrefixB, '-', '')) + '-' + RTRIM(REPLACE(C.PrefixC, '-', ''))
            END,

        BaseAccount = B.BaseAccount,
        Suffix = S.Suffix
) X;

GO

CREATE OR ALTER VIEW [MAP].[T_MASTER_EMPLOYEE]
AS
SELECT
    E.*,
    N.LAST_NAME,
    N.FIRST_NAME,
    N.MI
FROM [s300].[PRM_MASTER__EMPLOYEE] E
CROSS APPLY (
    SELECT
        DelimPos =
            CASE
                WHEN CHARINDEX(';', E.EMPLOYEE_NAME) > 0 THEN CHARINDEX(';', E.EMPLOYEE_NAME)
                WHEN CHARINDEX(',', E.EMPLOYEE_NAME) > 0 THEN CHARINDEX(',', E.EMPLOYEE_NAME)
                ELSE 0
            END
) D
CROSS APPLY (
    SELECT
        LastNameRaw =
            CASE
                WHEN D.DelimPos > 0 THEN LEFT(E.EMPLOYEE_NAME, D.DelimPos - 1)
                ELSE LTRIM(RTRIM(E.EMPLOYEE_NAME))
            END,
        RemainderRaw =
            CASE
                WHEN D.DelimPos > 0 THEN LTRIM(RTRIM(SUBSTRING(E.EMPLOYEE_NAME, D.DelimPos + 1, LEN(E.EMPLOYEE_NAME))))
                ELSE ''
            END
) P
CROSS APPLY (
    SELECT
        SpacePos = CHARINDEX(' ', P.RemainderRaw)
) S
CROSS APPLY (
    SELECT
        LAST_NAME  = NULLIF(LTRIM(RTRIM(P.LastNameRaw)), ''),
        FIRST_NAME =
            CASE
                WHEN P.RemainderRaw = '' THEN ''
                WHEN S.SpacePos = 0 THEN P.RemainderRaw
                ELSE LEFT(P.RemainderRaw, S.SpacePos - 1)
            END,
        MI =
            CASE
                WHEN P.RemainderRaw = '' THEN ''
                WHEN S.SpacePos = 0 THEN ''
                ELSE LTRIM(RTRIM(SUBSTRING(P.RemainderRaw, S.SpacePos + 1, LEN(P.RemainderRaw))))
            END
) N;
GO
