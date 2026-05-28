--ENTITY--------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'MAP'
      AND TABLE_NAME = 'T_TRANS_ENTITY'
)
BEGIN
    CREATE TABLE MAP.T_TRANS_ENTITY (
        DATA_FOLDER_ID       VARCHAR(50),
        LEGACY_ENTITY_ID     VARCHAR(50),
        LEGACY_ENTITY_NAME   VARCHAR(200),
        NEW_ENTITY_ID        VARCHAR(50),
        INCLUDE_ENTITY       BIT NOT NULL DEFAULT 0,
        PKG_BASE             BIT NOT NULL DEFAULT 0,
        PKG_CONSTR_SUM       BIT NOT NULL DEFAULT 0,
        PKG_CONSTR_DET       BIT NOT NULL DEFAULT 0,
        PKG_NPC_COMMIT       BIT NOT NULL DEFAULT 0,
        PKG_PC_COMMIT        BIT NOT NULL DEFAULT 0
    );
    INSERT INTO MAP.T_TRANS_ENTITY (
        DATA_FOLDER_ID,
        LEGACY_ENTITY_ID,
        LEGACY_ENTITY_NAME,
        NEW_ENTITY_ID,
        INCLUDE_ENTITY,
        PKG_BASE,
        PKG_CONSTR_SUM,
        PKG_CONSTR_DET,
        PKG_NPC_COMMIT,
        PKG_PC_COMMIT
    )
    SELECT
        E.DATA_FOLDER_ID,
        E.Account_Prefix,
        E.Description,
        E.Account_Prefix,
        0, 0, 0, 0, 0, 0
	FROM
		[s300].[GLM_MASTER__ACCOUNT_PREFIX_A] E;
END
GO
----------------------------------------------------------------------------
