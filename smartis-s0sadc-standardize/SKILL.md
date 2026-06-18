---
name: smartis-s0sadc-standardize
description: >-
  Standardize the SHARED Smartis SAP Business One (SAP B1) add-on registration table
  `S0SADC` and its stored procedure `S0ADDONDATA_SP` to ONE canonical schema across all
  Smartis add-ons (e-Invoice, VASManager, Consolidation, and any new add-on) that run on
  the same SAP company database. Use this to fix the init errors "Invalid column name
  'INSTALLGUSER'" and "Procedure or function S0ADDONDATA_SP has too many arguments
  specified", or whenever an add-on must share S0SADC with the others. Performs a
  NON-DESTRUCTIVE migration (rename the legacy INSTALLGUSER column, add missing columns,
  widen INSTALLKEY — never DROP or DELETE data), aligns the proc to the canonical
  4-parameter signature, fixes the C# query layer (4-arg proc call + CAST INSTALLKEY on
  read), and switches the init flow to "record-on-success". Use whenever the user wants to
  "chuẩn hóa S0SADC", "chuẩn hóa DB chung", "chuẩn hóa bảng S0SADC và store",
  "fix S0ADDONDATA_SP", or mentions "INSTALLGUSER" / "too many arguments S0ADDONDATA_SP" /
  sharing S0SADC across add-ons. Supports SQL Server + SAP HANA.
---

# Smartis Shared DB — Standardize `S0SADC` + `S0ADDONDATA_SP`

`S0SADC` (add-on registration table) and `S0ADDONDATA_SP` (version-register procedure) are
**single DB objects SHARED by every Smartis SAP B1 add-on** on the same company database.
Each add-on re-creates them at startup (`CREATE OR ALTER` / `CREATE OR REPLACE`), so **all
add-ons must ship an identical table schema and an identical proc signature + body** —
otherwise whichever add-on loads last overwrites the proc and breaks the others.

Complementary to the [smartis-b1-license-check] skill, which reads/writes the license columns
of the same `S0SADC` row.

## When to use — symptoms
- Init log shows **`Invalid column name 'INSTALLGUSER'`** and/or
  **`S0ADDONDATA_SP has too many arguments specified`** (often together; the column error makes
  `CREATE OR ALTER PROCEDURE` fail, leaving the previously-created proc whose arg count no longer
  matches the caller).
- A new add-on must coexist with the others on one company DB.
- "chuẩn hóa S0SADC / DB chung / bảng S0SADC và store".

**Root cause:** an add-on shipped the legacy typo column `INSTALLGUSER` and/or a 5-parameter proc
(with a `@status`/`action` delete branch), while the shared table already has `INSTALLUSER` and the
shared proc is the canonical 4-parameter version (or vice-versa). Fix = bring EVERY add-on to the
one canonical schema below.

## Canonical reference (source of truth)
Reference implementation = **e-Invoice** (`D:\Code\Project\ProjectAddon\e-Invoice\Smartis.EInvoice.B1Addon`;
the live table+proc are stored Base64 in `setup.json` → `tableversion`/`storeversion`). VASManager
(`D:\Code\Project\ProjectAddon\VASAddon`) and Consolidation
(`D:\Code\Project\ProjectAddon\Consolidation\Consolidation`) are aligned to it.

`S0SADC` — exactly **13 columns**; user column is **`INSTALLUSER`** (NOT `INSTALLGUSER`);
`INSTALLKEY` is `NVARCHAR(MAX)` / `NCLOB`:

| # | Column | Type |
|---|---|---|
| 1 | `ID` | INT IDENTITY / INTEGER IDENTITY (PK) |
| 2 | `NAME` | NVARCHAR(20) NOT NULL |
| 3 | `VERSION` | NVARCHAR(10) NOT NULL |
| 4 | `PROVIDER` | NVARCHAR(10) DEFAULT 'SMARTIS' |
| 5 | `TYPE` | NVARCHAR(10) DEFAULT 'LightAddon' |
| 6 | `INSTCOMPNY` | NVARCHAR(50) |
| 7 | `INSTALLDATE` | DATE NOT NULL |
| 8 | `INSTALLUSER` | NVARCHAR(50) NOT NULL |
| 9 | `INSTALLKEY` | NVARCHAR(MAX) / NCLOB |
| 10 | `SHKEY` | NVARCHAR(50) |
| 11 | `STARTDATE` | DATE |
| 12 | `ENDDATE` | DATE |
| 13 | `CUSTOMERNAME` | NVARCHAR(100) |

`S0ADDONDATA_SP` — exactly **4 parameters** (`@ExtName, @ExtVersion, @Partner, @pUser`), NO
`@status`/`action`. Inserts a new row (with `INSTALLKEY = NULL`) only when
`@ExtVersion > MAX(VERSION)` for this NAME+PROVIDER+INSTCOMPNY; returns `0` if inserted (new
version), else `1`. It does NOT carry the license key forward and has NO delete branch.

## Procedure

### 1. Locate every copy in the target add-on
The table + proc SQL can live in several places — update ALL of them and keep in sync:
- `DataScripts/SQLSERVER/00.INIT/00.S0SADC.sql` + `01.S0ADDONDATA_SP.sql` (VAS/Consolidation style;
  run at startup — embedded resource for VAS, copied-to-output filesystem for Consolidation).
- `DataScripts/HANADB/00.INIT/...` (mirror — ALWAYS update both engines).
- `DataScripts/.../01.TABLE/01.00.S0SADC.sql` (e-Invoice keeps a "sync copy" here).
- `setup.json` → `tableversion` / `storeversion` (e-Invoice; **Base64** — decode, edit, re-encode;
  this copy runs FIRST at startup so it MUST match).

Also find the C# layer: the query builder (`Helpers/AddonSqlQueries.cs` or `Database/QueryStore.cs`)
and the init flow (`InitialSetting.cs`).

### 2. Table script → canonical idempotent migration (NON-DESTRUCTIVE)
Creates on a fresh DB; reconciles an existing one (rename typo column, add missing columns, widen
INSTALLKEY) **without dropping the table, any column, or any row**.

**SQL Server** (`00.S0SADC.sql`):
```sql
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'S0SADC')
BEGIN
    CREATE TABLE [S0SADC] (
        [ID] INT IDENTITY(1,1) PRIMARY KEY,
        [NAME] NVARCHAR(20) NOT NULL,
        [VERSION] NVARCHAR(10) NOT NULL,
        [PROVIDER] NVARCHAR(10) NOT NULL DEFAULT 'SMARTIS',
        [TYPE] NVARCHAR(10) NOT NULL DEFAULT 'LightAddon',
        [INSTCOMPNY] NVARCHAR(50) NULL,
        [INSTALLDATE] DATE NOT NULL,
        [INSTALLUSER] NVARCHAR(50) NOT NULL,
        [INSTALLKEY] NVARCHAR(MAX) NULL,
        [SHKEY] NVARCHAR(50) NULL,
        [STARTDATE] DATE NULL,
        [ENDDATE] DATE NULL,
        [CUSTOMERNAME] NVARCHAR(100)
    );
END;

IF COL_LENGTH('dbo.S0SADC', 'INSTALLUSER') IS NULL AND COL_LENGTH('dbo.S0SADC', 'INSTALLGUSER') IS NOT NULL
    EXEC sp_rename 'dbo.S0SADC.INSTALLGUSER', 'INSTALLUSER', 'COLUMN';

IF COL_LENGTH('dbo.S0SADC', 'SHKEY') IS NULL        ALTER TABLE [S0SADC] ADD [SHKEY] NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.S0SADC', 'STARTDATE') IS NULL    ALTER TABLE [S0SADC] ADD [STARTDATE] DATE NULL;
IF COL_LENGTH('dbo.S0SADC', 'ENDDATE') IS NULL      ALTER TABLE [S0SADC] ADD [ENDDATE] DATE NULL;
IF COL_LENGTH('dbo.S0SADC', 'CUSTOMERNAME') IS NULL ALTER TABLE [S0SADC] ADD [CUSTOMERNAME] NVARCHAR(100) NULL;

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'S0SADC' AND COLUMN_NAME = 'INSTALLKEY'
             AND (DATA_TYPE = 'ntext' OR (DATA_TYPE = 'nvarchar' AND CHARACTER_MAXIMUM_LENGTH <> -1)))
    ALTER TABLE [S0SADC] ALTER COLUMN [INSTALLKEY] NVARCHAR(MAX) NULL;
```

**HANA** (`00.S0SADC.sql`):
```sql
DO
BEGIN
    If not exists (Select top 1 table_name from sys.tables where schema_name = CURRENT_SCHEMA and lower(table_name) = lower('S0SADC')) then
        EXEC 'CREATE COLUMN TABLE "S0SADC"
            ("ID" INTEGER CS_INT GENERATED BY DEFAULT AS IDENTITY NOT NULL ,
             "NAME" NVARCHAR(20) NOT NULL , "VERSION" NVARCHAR(10) NOT NULL ,
             "PROVIDER" NVARCHAR(10) NOT NULL DEFAULT ''SMARTIS'',
             "TYPE" NVARCHAR(10) NOT NULL DEFAULT ''LightAddon'',
             "INSTCOMPNY" NVARCHAR(50) NULL, "INSTALLDATE" DATE CS_DAYDATE NOT NULL ,
             "INSTALLUSER" NVARCHAR(50) NOT NULL , "INSTALLKEY" NCLOB,
             "SHKEY" NVARCHAR(50), "STARTDATE" DATE CS_DAYDATE, "ENDDATE" DATE CS_DAYDATE,
             "CUSTOMERNAME" NVARCHAR(100), PRIMARY KEY ("ID"))';
    End if;

    If exists (Select top 1 column_name from table_columns where schema_name = CURRENT_SCHEMA and table_name = 'S0SADC' and column_name = 'INSTALLGUSER')
       and not exists (Select top 1 column_name from table_columns where schema_name = CURRENT_SCHEMA and table_name = 'S0SADC' and column_name = 'INSTALLUSER') then
        EXEC 'RENAME COLUMN "S0SADC"."INSTALLGUSER" TO "INSTALLUSER"';
    End if;

    If not exists (Select top 1 column_name from table_columns where schema_name = CURRENT_SCHEMA and table_name = 'S0SADC' and column_name = 'SHKEY') then        EXEC 'ALTER TABLE "S0SADC" ADD ("SHKEY" NVARCHAR(50))'; End if;
    If not exists (Select top 1 column_name from table_columns where schema_name = CURRENT_SCHEMA and table_name = 'S0SADC' and column_name = 'STARTDATE') then    EXEC 'ALTER TABLE "S0SADC" ADD ("STARTDATE" DATE)'; End if;
    If not exists (Select top 1 column_name from table_columns where schema_name = CURRENT_SCHEMA and table_name = 'S0SADC' and column_name = 'ENDDATE') then      EXEC 'ALTER TABLE "S0SADC" ADD ("ENDDATE" DATE)'; End if;
    If not exists (Select top 1 column_name from table_columns where schema_name = CURRENT_SCHEMA and table_name = 'S0SADC' and column_name = 'CUSTOMERNAME') then EXEC 'ALTER TABLE "S0SADC" ADD ("CUSTOMERNAME" NVARCHAR(100))'; End if;

    If exists (Select top 1 column_name from table_columns where schema_name = CURRENT_SCHEMA and table_name = 'S0SADC' and column_name = 'INSTALLKEY' and data_type_name <> 'NCLOB') then
        EXEC 'ALTER TABLE "S0SADC" ALTER ("INSTALLKEY" NCLOB)';
    End if;
END;
```

### 3. Proc → canonical 4-parameter
**SQL Server** (`01.S0ADDONDATA_SP.sql`):
```sql
CREATE OR ALTER PROCEDURE [dbo].[S0ADDONDATA_SP]
( @ExtName NVARCHAR(100), @ExtVersion NVARCHAR(100), @Partner NVARCHAR(100), @pUser NVARCHAR(100) )
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @result INT;
    IF @ExtVersion > ( SELECT ISNULL(MAX([VERSION]), '0') FROM [S0SADC]
                       WHERE [NAME] = @ExtName AND [PROVIDER] = @Partner AND [INSTCOMPNY] = DB_NAME() )
    BEGIN
        INSERT INTO [S0SADC] ([NAME],[VERSION],[PROVIDER],[TYPE],[INSTCOMPNY],[INSTALLDATE],[INSTALLUSER],[INSTALLKEY])
        SELECT @ExtName, @ExtVersion, @Partner, 'LightAddon', DB_NAME(), CAST(GETDATE() AS DATE), ISNULL(@pUser, SUSER_NAME()), NULL;
        SET @result = 0;
    END
    ELSE SET @result = 1;
    SELECT @result;
END;
```

**HANA** (`01.S0ADDONDATA_SP.sql`):
```sql
CREATE OR REPLACE PROCEDURE S0ADDONDATA_SP
( ExtName nvarchar(100), ExtVersion nvarchar(100), Partner nvarchar(100), pUser nvarchar(100) ) AS
    result int;
BEGIN
    IF :ExtVersion > ( Select ifnull(max(VERSION),'0') from S0SADC
                       where NAME = :ExtName and PROVIDER = :Partner and INSTCOMPNY = CURRENT_SCHEMA ) THEN
        Insert Into S0SADC ("NAME","VERSION","PROVIDER","TYPE","INSTCOMPNY","INSTALLDATE","INSTALLUSER","INSTALLKEY")
        Select :ExtName, :ExtVersion, :Partner, 'LightAddon', CURRENT_SCHEMA, CURRENT_DATE, ifnull(:pUser, CURRENT_USER), null from dummy;
        result := 0;
    ELSE result := 1;
    END IF;
    Select :result from dummy;
END;
```

### 4. C# query layer (`AddonSqlQueries.cs` / `QueryStore.cs`)
- The proc-call builder passes **4 args** (name, version, partner, userSignature) — drop any
  `status`/`action` 5th arg. `@pUser` is NVARCHAR, so quote the signature (`'{userSignature}'`).
  SQL Server: `EXECUTE S0ADDONDATA_SP '{name}','{ver}','{partner}','{user}'`;
  HANA: `CALL S0ADDONDATA_SP ('{name}','{ver}','{partner}','{user}')`.
- **Every SELECT that reads `INSTALLKEY` must wrap it**:
  `CAST(INSTALLKEY AS NVARCHAR(4000)) AS INSTALLKEY` (HANA: `CAST("INSTALLKEY" AS NVARCHAR(4000)) AS "INSTALLKEY"`).
  The SAP DI API `Recordset` truncates a `MAX`/`NCLOB` column on read → `hash(read) != SHKEY` →
  the license integrity check and key parse fail. 4000 fits any license key.

### 5. Init flow → record-on-success (NO delete, auto-retry)
The canonical proc has no delete branch, and we never delete data. Restructure the add-on's
`GeneralSettings()` so the version row is written ONLY after a successful init:
- **Detect "new version" read-only** before init (compare `.ard` version to installed `MAX(VERSION)`).
  Either a numeric C# compare (Consolidation's `CompareVersions`) or a query like:
  ```sql
  -- returns 0 = new (run init), 1 = already installed  (mirrors the proc)
  SELECT CASE WHEN '{version}' > ISNULL(MAX([VERSION]), '0') THEN 0 ELSE 1 END AS RESULT
  FROM S0SADC WHERE [NAME]='{name}' AND [PROVIDER]='{partner}' AND [INSTCOMPNY] = DB_NAME()
  ```
- Run init (UDT/UDF/UDO + register scripts). These creators are idempotent (check-exists-then-create),
  so re-running on retry is safe.
- **Call the proc to record the version ONLY after init + scripts succeed.** On failure, `return`
  without recording → the next super-user login re-detects "new" and retries automatically.
- **Remove `DeleteFailedVersion()`** (method + all call sites) — it relied on the proc's old
  `status=0` DELETE branch, which no longer exists.

```csharp
// GeneralSettings(): record-on-success
if (IsNewVersion())                       // read-only detect
{
    if (!InitSetting())   { /* report failed */ return; }   // no row written -> retried next launch
    if (!RegistingScript()){ /* report failed */ return; }
    RecordInstalledVersion();             // calls the 4-arg proc ONLY now
}
```

### 6. Verify (Claude does NOT build/run — the user builds)
- Grep the add-on: no `INSTALLGUSER` left; no `DeleteFailedVersion`; no 5-arg proc call; region/brace balance intact.
- After the user runs it: init log clean (no "Invalid column" / "too many arguments").
- DB checks:
  - SQL Server: `SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='S0SADC' ORDER BY ORDINAL_POSITION;`
    (13 cols, `INSTALLUSER`, `INSTALLKEY` = nvarchar/-1) and
    `SELECT COUNT(*) FROM sys.parameters WHERE object_id = OBJECT_ID('S0ADDONDATA_SP');` (= 4).
  - HANA: `SELECT column_name, data_type_name FROM table_columns WHERE table_name='S0SADC' AND schema_name=CURRENT_SCHEMA ORDER BY position;`

## Critical rules
- **Identical proc across ALL add-ons** — same 4-param signature AND same body. Whichever add-on
  loads last re-creates it, so a divergent copy re-breaks the others.
- **Never DROP / DELETE data.** Migration is rename + add-column + widen only.
- Column is **`INSTALLUSER`** (no "G"). `INSTALLGUSER` is the legacy typo to rename away.
- Versions must be **zero-padded** (e.g. `26.05.05`) so the proc's string `>` compare matches a
  numeric compare.
- Update **every** copy (00.INIT, 01.TABLE, `setup.json` Base64) and mirror **SQLSERVER + HANADB**.
- Don't build/run for the user unless explicitly asked (Smartis project working rule).
