CREATE OR REPLACE PROCEDURE S0ADDONDATA_SP
    (
        ExtName nvarchar(100),
        ExtVersion nvarchar(100),
        Partner nvarchar(100),
        pUser nvarchar(100)
    ) AS
    result int;
BEGIN
    IF :ExtVersion > ( Select ifnull(max(VERSION),'0') from S0SADC
                       where NAME = :ExtName and PROVIDER = :Partner and INSTCOMPNY = CURRENT_SCHEMA )
    THEN
        -- Carry the license of the previous version forward to this new version row.
        Insert Into S0SADC ("NAME","VERSION","PROVIDER","TYPE","INSTCOMPNY","INSTALLDATE","INSTALLUSER","INSTALLKEY","SHKEY","STARTDATE","ENDDATE","CUSTOMERNAME")
        Select :ExtName, :ExtVersion, :Partner, 'LightAddon', CURRENT_SCHEMA, CURRENT_DATE, ifnull(:pUser, CURRENT_USER),
               prev."INSTALLKEY", prev."SHKEY", prev."STARTDATE", prev."ENDDATE", prev."CUSTOMERNAME"
        from dummy
        left outer join (
            Select TOP 1 "INSTALLKEY", "SHKEY", "STARTDATE", "ENDDATE", "CUSTOMERNAME"
            from S0SADC
            where NAME = :ExtName and PROVIDER = :Partner and INSTCOMPNY = CURRENT_SCHEMA and INSTALLKEY is not null
            order by ID desc
        ) prev on 1 = 1;

        result := 0;
    ELSE
        result := 1;
    END IF;

    Select :result from dummy;
END;
