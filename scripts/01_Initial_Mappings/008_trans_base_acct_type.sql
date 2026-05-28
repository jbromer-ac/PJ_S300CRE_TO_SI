--GL ACCOUNT TYPE TRANSLATION
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'MAP' AND TABLE_NAME = 'E_ACCT_TYPE') BEGIN
CREATE TABLE MAP.E_ACCT_TYPE (Base_Account_Type VARCHAR(50) NOT NULL PRIMARY KEY, ACCT_TYPE CHAR(1) NOT NULL, NORMAL_BALANCE CHAR(2) NOT NULL, CLOSEABLE CHAR(1) NOT NULL);

INSERT INTO MAP.E_ACCT_TYPE (Base_Account_Type, ACCT_TYPE, NORMAL_BALANCE, CLOSEABLE)
VALUES
    ('Current assets',    'N', 'DB', 'N'),
    ('Noncurrent assets', 'N', 'DB', 'N'),
    ('Current liab',      'N', 'CR', 'N'),
    ('Noncurrent liab',   'N', 'CR', 'N'),
    ('Equity',            'N', 'CR', 'N'),
    ('Retained earnings', 'N', 'CR', 'R'),
    ('Income',            'I', 'CR', 'C'),
    ('Cost',              'I', 'DB', 'C'),
    ('Expense',           'I', 'DB', 'C'),
    ('Other income',      'I', 'CR', 'C')
END
GO