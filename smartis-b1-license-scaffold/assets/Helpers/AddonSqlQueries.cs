// ============================================================================
// Smartis SAP B1 — AddonSqlQueries (canonical copy from VASManager / branch vas_license)
// Bundled by skill: smartis-b1-license-scaffold
// Dialect-aware (SQL Server + HANA) query builder for the S0SADC license row,
// the shared S0ADDONDATA_SP version proc, and the GetAddonInfo proc.
// ADAPT: namespace + base class (SapUiBase) + proc name (VASGetAddonInfo) — see SKILL.md.
// ============================================================================
using System;
using UIAPI;

namespace VASManager.Helpers
{
    internal static class AddonSqlQueries
    {
        private static bool IsHana
        {
            get { return SapUiBase.oCompany.DbServerType == SAPbobsCOM.BoDataServerTypes.dst_HANADB; }
        }

        private static string SqlValue(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        public static string GetLatestLicense(string addonName, string addonVersion, string provider)
        {
            addonName = SqlValue(addonName);
            addonVersion = SqlValue(addonVersion);
            provider = SqlValue(provider);

            return IsHana
                ? $@"SELECT TOP 1 ""NAME"", ""VERSION"", CAST(""INSTALLKEY"" AS NVARCHAR(4000)) AS ""INSTALLKEY"", ""SHKEY"", ""STARTDATE"", ""ENDDATE"" FROM ""S0SADC"" WHERE ""NAME"" = '{addonName}' AND ""VERSION"" = '{addonVersion}' AND ""PROVIDER"" = '{provider}' ORDER BY ""ID"" DESC"
                : $@"SELECT TOP 1 NAME, VERSION, CAST(INSTALLKEY AS NVARCHAR(4000)) AS INSTALLKEY, SHKEY, STARTDATE, ENDDATE FROM S0SADC WHERE NAME = '{addonName}' AND VERSION = '{addonVersion}' AND PROVIDER = '{provider}' ORDER BY ID DESC";
        }

        public static string GetRegisteredLicense(string addonName, string provider)
        {
            addonName = SqlValue(addonName);
            provider = SqlValue(provider);

            return IsHana
                ? $@"SELECT TOP 1 ""INSTALLKEY"" FROM ""S0SADC""
                     WHERE ""NAME"" = '{addonName}'
                       AND ""PROVIDER"" = '{provider}'
                       AND ""INSTALLKEY"" IS NOT NULL
                     ORDER BY ""ID"" DESC"
                : $@"SELECT TOP 1 INSTALLKEY FROM S0SADC
                     WHERE [NAME] = '{addonName}'
                       AND [PROVIDER] = '{provider}'
                       AND INSTALLKEY IS NOT NULL
                       AND DATALENGTH(INSTALLKEY) > 0
                     ORDER BY ID DESC";
        }

        public static string CountAddonVersions(string addonName, string provider)
        {
            addonName = SqlValue(addonName);
            provider = SqlValue(provider);

            return IsHana
                ? $@"SELECT COUNT(*) AS ""CNT"" FROM ""S0SADC"" WHERE ""NAME"" = '{addonName}' AND ""PROVIDER"" = '{provider}'"
                : $@"SELECT COUNT(*) AS CNT FROM S0SADC WHERE [NAME] = '{addonName}' AND [PROVIDER] = '{provider}'";
        }

        public static string UpdateLicense(
            string licenseKey,
            string hashKey,
            DateTime fromDate,
            DateTime toDate,
            string dbName,
            string customer,
            string addonName,
            string addonVersion,
            string provider)
        {
            licenseKey = SqlValue(licenseKey);
            hashKey = SqlValue(hashKey);
            dbName = SqlValue(dbName);
            customer = SqlValue(customer);
            addonName = SqlValue(addonName);
            addonVersion = SqlValue(addonVersion);
            provider = SqlValue(provider);

            return IsHana
                ? $@"UPDATE ""S0SADC""
                   SET ""INSTALLKEY"" = '{licenseKey}',
                       ""SHKEY"" = '{hashKey}',
                       ""STARTDATE"" = '{fromDate:yyyy-MM-dd}',
                       ""ENDDATE"" = '{toDate:yyyy-MM-dd}',
                       ""INSTCOMPNY"" = '{dbName}',
                       ""CUSTOMERNAME"" = '{customer}'
                   WHERE ""ID"" = (
                       SELECT TOP 1 ""ID""
                       FROM ""S0SADC""
                       WHERE ""NAME"" = '{addonName}'
                         AND ""VERSION"" = '{addonVersion}'
                         AND ""PROVIDER"" = '{provider}'
                       ORDER BY ""ID"" DESC
                   )"
                : $@"UPDATE S0SADC
                   SET INSTALLKEY = '{licenseKey}',
                       SHKEY = '{hashKey}',
                       STARTDATE = '{fromDate:yyyy-MM-dd}',
                       ENDDATE = '{toDate:yyyy-MM-dd}',
                       INSTCOMPNY = '{dbName}',
                       CUSTOMERNAME = '{customer}'
                   WHERE ID = (
                       SELECT TOP 1 ID
                       FROM S0SADC
                       WHERE [NAME] = '{addonName}'
                         AND [VERSION] = '{addonVersion}'
                         AND [PROVIDER] = '{provider}'
                       ORDER BY ID DESC
                   )";
        }

        public static string DetectNewVersion(string addonName, string addonVersion, string provider)
        {
            addonName = SqlValue(addonName);
            addonVersion = SqlValue(addonVersion);
            provider = SqlValue(provider);

            // Read-only mirror of S0ADDONDATA_SP's "is new version" check.
            // Returns 0 when this version is newer than what is recorded (needs init),
            // 1 when it is already installed. No insert/update/delete.
            return IsHana
                ? $@"SELECT CASE WHEN '{addonVersion}' > IFNULL(MAX(""VERSION""), '0') THEN 0 ELSE 1 END AS ""RESULT"" FROM ""S0SADC"" WHERE ""NAME"" = '{addonName}' AND ""PROVIDER"" = '{provider}' AND ""INSTCOMPNY"" = CURRENT_SCHEMA"
                : $@"SELECT CASE WHEN '{addonVersion}' > ISNULL(MAX([VERSION]), '0') THEN 0 ELSE 1 END AS RESULT FROM S0SADC WHERE [NAME] = '{addonName}' AND [PROVIDER] = '{provider}' AND [INSTCOMPNY] = DB_NAME()";
        }

        public static string ExecuteAddonDataProcedure(
            string addonName,
            string addonVersion,
            string provider,
            int userSignature)
        {
            addonName = SqlValue(addonName);
            addonVersion = SqlValue(addonVersion);
            provider = SqlValue(provider);

            return IsHana
                ? $"CALL S0ADDONDATA_SP ('{addonName}','{addonVersion}','{provider}','{userSignature}');"
                : $"EXECUTE S0ADDONDATA_SP '{addonName}','{addonVersion}','{provider}','{userSignature}'";
        }

        public static string GetAddonInfo(string addonName, string addonVersion, string provider)
        {
            addonName = SqlValue(addonName);
            addonVersion = SqlValue(addonVersion);
            provider = SqlValue(provider);

            return IsHana
                ? $@"call ""VASGetAddonInfo""('{addonName}', '{addonVersion}', '{provider}')"
                : $"execute VASGetAddonInfo '{addonName}', '{addonVersion}', '{provider}'";
        }
    }
}
