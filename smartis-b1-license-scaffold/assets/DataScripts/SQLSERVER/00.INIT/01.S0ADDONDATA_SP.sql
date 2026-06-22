CREATE OR ALTER PROCEDURE [dbo].[S0ADDONDATA_SP]
(
    @ExtName NVARCHAR(100),
    @ExtVersion NVARCHAR(100),
    @Partner NVARCHAR(100),
    @pUser NVARCHAR(100)
)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @result INT;

    IF @ExtVersion > (
        SELECT ISNULL(MAX([VERSION]), '0')
        FROM [S0SADC]
        WHERE [NAME] = @ExtName
          AND [PROVIDER] = @Partner
          AND [INSTCOMPNY] = DB_NAME()
    )
    BEGIN
        -- Carry the license of the previous version forward to this new version row.
        INSERT INTO [S0SADC] ([NAME], [VERSION], [PROVIDER], [TYPE], [INSTCOMPNY], [INSTALLDATE], [INSTALLUSER], [INSTALLKEY], [SHKEY], [STARTDATE], [ENDDATE], [CUSTOMERNAME])
        SELECT @ExtName, @ExtVersion, @Partner, 'LightAddon', DB_NAME(), CAST(GETDATE() AS DATE), ISNULL(@pUser, SUSER_NAME()),
               prev.[INSTALLKEY], prev.[SHKEY], prev.[STARTDATE], prev.[ENDDATE], prev.[CUSTOMERNAME]
        FROM (SELECT 1 AS x) d
        OUTER APPLY (
            SELECT TOP 1 [INSTALLKEY], [SHKEY], [STARTDATE], [ENDDATE], [CUSTOMERNAME]
            FROM [S0SADC]
            WHERE [NAME] = @ExtName AND [PROVIDER] = @Partner AND [INSTCOMPNY] = DB_NAME() AND [INSTALLKEY] IS NOT NULL
            ORDER BY [ID] DESC
        ) prev;

        SET @result = 0;
    END
    ELSE
    BEGIN
        SET @result = 1;
    END;

    SELECT @result;
END;
