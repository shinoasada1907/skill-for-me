// ============================================================================
// Smartis SAP B1 — LicenseHelper (canonical copy from VASManager / branch vas_license)
// Bundled by skill: smartis-b1-license-scaffold
// ADAPT BEFORE USE: rename namespaces, base class (SapUiBase), service layer
//   (SDXServiceLayer) — see SKILL.md checklist.
// ============================================================================
using System;
using System.IO;
using System.Text;
using SmartisLicenseLibrary;
using SAPbouiCOM.Framework;
using System.Security.Cryptography;
using UIAPI;
using SDXManager.Service_Layer;
using VASManager.Models;

namespace VASManager.Helpers
{
    /// <summary>
    /// Helper class for reading and validating license files using SmartisLicenseLibrary
    /// </summary>
    public class LicenseHelper
    {
        private readonly LicenseReader _licenseReader;
        private const string DEFAULT_LICENSE_FILE = "license.txt";
        private const string TRIAL_HARDWARE_KEY = "Trial";
        private static string _cachedAddonIdentifierFingerprint = string.Empty;
        private static bool _cachedAddonIdentifierValid = false;
        private static string _cachedInstallationNumberFingerprint = string.Empty;
        private static string _cachedInstallationNumber = string.Empty;

        public LicenseHelper()
        {
            _licenseReader = new LicenseReader();
        }

        public static string ComputeSHA256Short(string input, int length = 16)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hash).Replace("-", "").ToLower().Substring(0, length);
            }
        }

        private static string SqlValue(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private static bool ValidateAddonIdentifier(string addonIdentifier, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(addonIdentifier))
            {
                message = "License AddonIdentifier is missing.";
                return false;
            }

            try
            {
                // Fixed connection string — hardcoded intentionally; this value is the same on every
                // install. The only per-server value is the license IdentifierKey (passed in below as
                // addonIdentifier), which already comes from the license, so nothing to adapt here.
                string connectionString = "0030002C0030002C00530041005000420044005F00440061007400650076002C0050004C006F006D0056004900490056";
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    message = "Connection string not available. Cannot verify AddonIdentifier.";
                    return false;
                }

                SAPbouiCOM.SboGuiApi sboGuiApi = null;
                SAPbouiCOM.Application testApplication = null;

                try
                {
                    sboGuiApi = new SAPbouiCOM.SboGuiApi
                    {
                        AddonIdentifier = addonIdentifier
                    };

                    sboGuiApi.Connect(connectionString);
                    testApplication = sboGuiApi.GetApplication(-1);

                    return testApplication != null;
                }
                finally
                {
                    if (testApplication != null) SapUiBase.ReleaseObject(testApplication);
                    if (sboGuiApi != null) SapUiBase.ReleaseObject(sboGuiApi);
                }
            }
            catch (Exception ex)
            {
                message = "Invalid AddonIdentifier: " + ex.Message;
                return false;
            }
        }

        private static bool ValidateAddonIdentifierCached(string addonIdentifier, string fingerprint, out string message)
        {
            message = string.Empty;

            if (_cachedAddonIdentifierValid &&
                _cachedAddonIdentifierFingerprint.Equals(fingerprint, StringComparison.Ordinal))
            {
                return true;
            }

            if (!ValidateAddonIdentifier(addonIdentifier, out message))
            {
                return false;
            }

            _cachedAddonIdentifierFingerprint = fingerprint;
            _cachedAddonIdentifierValid = true;
            return true;
        }

        private static string BuildRuntimeFingerprint(
            string licenseKey,
            string storedHash,
            string companyDb,
            string identifierKey)
        {
            string value = string.Join("|", new[]
            {
                licenseKey ?? string.Empty,
                storedHash ?? string.Empty,
                companyDb ?? string.Empty,
                identifierKey ?? string.Empty
            });

            return ComputeSHA256Short(value, 32);
        }

        private static bool IsLicenseRequiredForFunction(SmartisLicenseInfo licenseInfo, string formId)
        {
            foreach (var function in licenseInfo.GetFunctions())
            {
                string objectId = Convert.ToString(function["OBJECT"]);
                if (!objectId.Equals(formId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string required = Convert.ToString(function["REQ"]);
                return required.Equals("Y", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private static bool ValidateInstallationNumberCached(
            SmartisLicenseInfo licenseInfo,
            string fingerprint,
            out string message)
        {
            message = string.Empty;

            if (!_cachedInstallationNumberFingerprint.Equals(fingerprint, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_cachedInstallationNumber))
            {
                _cachedInstallationNumber = new SDXServiceLayer().InstallationNumber();
                _cachedInstallationNumberFingerprint = fingerprint;
            }

            if (!string.Equals(licenseInfo.InstallationNumber, _cachedInstallationNumber, StringComparison.OrdinalIgnoreCase))
            {
                message = "Invalid Installation Number";
                return false;
            }

            return true;
        }

        public static bool CheckLicense(string formId, out string message)
        {
            message = string.Empty;
            string step = "loading license from database";
            try
            {
                var reader = new LicenseReader();
                string query = AddonSqlQueries.GetLatestLicense(
                    AddOnVersionModel.Name,
                    AddOnVersionModel.Version,
                    AddOnVersionModel.Partner);

                SAPbobsCOM.Recordset recordset = SapUiBase.ExecQuery(
                    query);

                if (recordset.RecordCount == 0)
                {
                    message = "No license found in database. Please register your license.";
                    return false;
                }

                step = "reading license key";
                string storedHash = recordset.Fields.Item("SHKEY").Value?.ToString() ?? string.Empty;
                string lkey = recordset.Fields.Item("INSTALLKEY").Value?.ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(lkey))
                {
                    message = "No license key found. Please register your license.";
                    return false;
                }

                step = "parsing license key";
                string[] licenseKey = lkey.Split(':');
                var data = reader.ParseLicenseKeyToInfo(licenseKey[0]);
                if (data is null)
                {
                    message = "Cannot read license key (corrupted or wrong format). Please re-register your license.";
                    return false;
                }

                step = "validating addon name";
                string expectedAddon = AddOnVersionModel.Name;
                if (string.IsNullOrWhiteSpace(expectedAddon))
                {
                    message = "Addon name not initialized from .ard.";
                    return false;
                }

                string addon = data.Addon;
                if (string.IsNullOrWhiteSpace(addon) ||
                    !addon.Equals(expectedAddon, StringComparison.OrdinalIgnoreCase))
                {
                    message = $"Wrong add-on name.\n\nExpected: {expectedAddon}\nLicense: {(string.IsNullOrWhiteSpace(addon) ? "(empty)" : addon)}";
                    return false;
                }

                step = "verifying license integrity";
                string hashlkey = LicenseHelper.ComputeSHA256Short(lkey);
                if (storedHash != hashlkey)
                {
                    message = "License integrity check failed (key hash/SHKEY mismatch). The stored license may have been modified.";
                    return false;
                }

                bool isTrial = data.HardwareKey.Equals(TRIAL_HARDWARE_KEY, StringComparison.OrdinalIgnoreCase);

                string currentDb = SapUiBase.oCompany.CompanyDB;
                string runtimeFingerprint = BuildRuntimeFingerprint(
                    lkey,
                    storedHash,
                    currentDb,
                    data.IdentifierKey);

                step = "resolving license dates";
                DateTime startDate = Convert.ToDateTime(recordset.Fields.Item("STARTDATE").Value);
                DateTime endDate = Convert.ToDateTime(recordset.Fields.Item("ENDDATE").Value);

                if (licenseKey.Length > 1)
                {
                    string realDate = SapUiBase.DecodeBase64(licenseKey[1]);
                    string[] dateTrial = realDate.Split('-');
                    startDate = Convert.ToDateTime(dateTrial[0]);
                    endDate = Convert.ToDateTime(dateTrial[1]);
                }
                else
                {
                    startDate = DateTime.ParseExact(data.FromDate.Trim(), "yyyyMMdd", null);
                    endDate = DateTime.ParseExact(data.ToDate.Trim(), "yyyyMMdd", null);
                }

                DateTime today = DateTime.Now.Date;

                if (today < startDate)
                {
                    message = $"License not yet valid!\n\nValid from: {startDate:yyyy-MM-dd}";
                    return false;
                }

                if (today > endDate)
                {
                    message = $"License has expired!\n\nExpired on: {endDate:yyyy-MM-dd}\n\nPlease contact support to renew your license.";
                    return false;
                }

                step = "checking function access";
                if (!IsLicenseRequiredForFunction(data, formId))
                {
                    return true;
                }

                if (!data.IsFunctionAccessible(formId))
                {
                    message = $"License does not allow function '{formId}'.";
                    return false;
                }

                if (!isTrial)
                {
                    step = "validating company database";
                    if (!data.HasDatabase(currentDb))
                    {
                        message = $"License does not cover database '{currentDb}'.";
                        return false;
                    }

                    step = "validating installation number";
                    if (!ValidateInstallationNumberCached(data, runtimeFingerprint, out message))
                    {
                        return false;
                    }
                }

                step = "validating addon identifier";
                if (!isTrial &&
                    !ValidateAddonIdentifierCached(data.IdentifierKey, runtimeFingerprint, out message))
                {
                    return false;
                }

                TimeSpan remainingDays = endDate - today;
                if (remainingDays.Days <= 30 && remainingDays.Days > 0)
                {
                    Application.SBO_Application.StatusBar.SetText(
                        $"⚠️ License will expire in {remainingDays.Days} days ({endDate:yyyy-MM-dd})",
                        SAPbouiCOM.BoMessageTime.bmt_Short,
                        SAPbouiCOM.BoStatusBarMessageType.smt_Warning);
                }

                return true;

            }
            catch (Exception ex)
            {
                message = $"License check failed while {step}.\n\nDetail: {ex.Message}";
            }
            return false;
        }


        /// <summary>
        /// Reads license from the default license.txt file
        /// </summary>
        /// <returns>SmartisLicenseInfo object containing all license information</returns>
        public SmartisLicenseInfo ReadLicense()
        {
            return ReadLicenseFromFile(DEFAULT_LICENSE_FILE);
        }

        /// <summary>
        /// Reads license from a specified file path
        /// </summary>
        /// <param name="filePath">Path to the license file</param>
        /// <returns>SmartisLicenseInfo object containing all license information</returns>
        public SmartisLicenseInfo ReadLicenseFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"License file not found: {filePath}");
                }

                // Check file extension to determine file type
                string extension = Path.GetExtension(filePath).ToLowerInvariant();

                if (extension == ".smlc")
                {
                    // Encrypted .smlc file
                    return _licenseReader.ReadSmartisLicenseFromEncryptedFile(filePath);
                }
                else
                {
                    // Regular .txt file - try to auto-detect encryption
                    return _licenseReader.ExtractLicenseInfo(filePath);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read license file: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Reads and validates license, checking expiration and validity
        /// </summary>
        /// <returns>SmartisLicenseInfo if valid, throws exception if invalid</returns>
        public SmartisLicenseInfo ReadAndValidateLicense()
        {
            return ReadAndValidateLicenseFromFile(DEFAULT_LICENSE_FILE);
        }

        /// <summary>
        /// Reads and validates license from specified file, checking expiration and validity
        /// </summary>
        /// <param name="filePath">Path to the license file</param>
        /// <returns>SmartisLicenseInfo if valid, throws exception if invalid</returns>
        public SmartisLicenseInfo ReadAndValidateLicenseFromFile(string filePath)
        {
            var licenseInfo = ReadLicenseFromFile(filePath);

            if (licenseInfo.IsExpired)
            {
                throw new InvalidOperationException(
                    $"License has expired on {licenseInfo.ToDate}. Please contact support to renew your license.");
            }

            if (licenseInfo.IsNotYetValid)
            {
                throw new InvalidOperationException(
                    $"License is not yet valid. Valid from: {licenseInfo.FromDate}");
            }

            if (!licenseInfo.IsValid)
            {
                throw new InvalidOperationException("License is not valid.");
            }

            return licenseInfo;
        }

        /// <summary>
        /// Checks if the license is valid for a specific company database
        /// </summary>
        /// <param name="companyDb">Company database name to check</param>
        /// <returns>True if license is valid and covers the specified database</returns>
        public bool IsValidForDatabase(string companyDb)
        {
            try
            {
                var licenseInfo = ReadAndValidateLicense();
                return licenseInfo.HasDatabase(companyDb);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets license information as a formatted string for display
        /// </summary>
        /// <returns>Formatted license information string</returns>
        public string GetLicenseDisplayInfo()
        {
            try
            {
                var licenseInfo = ReadLicense();

                var sb = new StringBuilder();
                sb.AppendLine("=== License Information ===");
                sb.AppendLine($"Product         : {licenseInfo.ProductName}");
                sb.AppendLine($"Customer        : {licenseInfo.CustomerName}");
                sb.AppendLine($"Contract ID     : {licenseInfo.ContractId}");
                sb.AppendLine($"Hardware Key    : {licenseInfo.HardwareKey}");
                sb.AppendLine($"Installation No : {licenseInfo.InstallationNumber}");
                sb.AppendLine($"Valid From      : {licenseInfo.FromDate}");
                sb.AppendLine($"Valid To        : {licenseInfo.ToDate}");
                sb.AppendLine($"Perpetual       : {(licenseInfo.IsPerpetual ? "Yes" : "No")}");
                sb.AppendLine($"Status          : {(licenseInfo.IsValid ? "VALID" : licenseInfo.IsExpired ? "EXPIRED" : "NOT YET VALID")}");

                if (!licenseInfo.IsPerpetual)
                {
                    sb.AppendLine($"Days Remaining  : {licenseInfo.DaysUntilExpiration}");
                }

                if (licenseInfo.CompanySchemas.Count > 0)
                {
                    sb.AppendLine($"Company DBs     : {string.Join(", ", licenseInfo.CompanySchemas)}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error reading license: {ex.Message}";
            }
        }

        /// <summary>
        /// Shows license information in SAP B1 message box
        /// </summary>
        public void ShowLicenseInfo()
        {
            try
            {
                string info = GetLicenseDisplayInfo();
                Application.SBO_Application.MessageBox(info);
            }
            catch (Exception ex)
            {
                Application.SBO_Application.MessageBox($"Error reading license: {ex.Message}", 1, "OK");
            }
        }

        /// <summary>
        /// Validates license and shows appropriate message
        /// </summary>
        /// <returns>True if license is valid, false otherwise</returns>
        public bool ValidateAndShowStatus()
        {
            try
            {
                var licenseInfo = ReadAndValidateLicense();

                string message;
                if (licenseInfo.IsPerpetual)
                {
                    message = $"License is valid (Perpetual)\n\nCustomer: {licenseInfo.CustomerName}\nProduct: {licenseInfo.ProductName}";
                }
                else
                {
                    message = $"License is valid\n\nCustomer: {licenseInfo.CustomerName}\nProduct: {licenseInfo.ProductName}\nDays Remaining: {licenseInfo.DaysUntilExpiration}";
                }

                Application.SBO_Application.SetStatusBarMessage(
                    message,
                    SAPbouiCOM.BoMessageTime.bmt_Medium,
                    false);

                return true;
            }
            catch (Exception ex)
            {
                Application.SBO_Application.MessageBox(
                    $"License validation failed:\n{ex.Message}",
                    1,
                    "OK");
                return false;
            }
        }

        /// <summary>
        /// Gets license expiry warning message if license is expiring soon
        /// </summary>
        /// <param name="warningDays">Number of days before expiry to show warning (default: 30)</param>
        /// <returns>Warning message if expiring soon, null otherwise</returns>
        public string GetExpiryWarning(int warningDays = 30)
        {
            try
            {
                var licenseInfo = ReadLicense();

                if (licenseInfo.IsPerpetual || !licenseInfo.IsValid)
                {
                    return null;
                }

                if (licenseInfo.DaysUntilExpiration <= warningDays && licenseInfo.DaysUntilExpiration > 0)
                {
                    return $"WARNING: Your license will expire in {licenseInfo.DaysUntilExpiration} days. Please contact support to renew.";
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
