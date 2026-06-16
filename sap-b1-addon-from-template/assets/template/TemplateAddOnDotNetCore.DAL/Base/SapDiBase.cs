using SAPbobsCOM;
using TemplateAddOnDotNetCore.Common.Enums;

namespace TemplateAddOnDotNetCore.DAL.Base;

/// <summary>
/// Base class for SAP DI API data access operations.
/// Company is initialized by the Presentation layer (SapUiBase) at startup.
/// </summary>
public class SapDiBase
{
    private static Company? _company;

    #region Properties

    public static Company Company
    {
        get => _company ?? throw new InvalidOperationException("SAP Company not initialized. Ensure SapUiBase has started.");
        set
        {
            _company = value;
            DbType = value.DbServerType == BoDataServerTypes.dst_HANADB
                ? DatabaseType.Hana
                : DatabaseType.SqlServer;
        }
    }

    public static DatabaseType DbType { get; private set; }
    public static int UserId { get; set; }
    public static string BranchName { get; set; } = string.Empty;
    public static bool IsSuperUser { get; set; }

    #endregion

    #region Query Execution

    public static Recordset ExecQuery(string sql)
    {
        var rs = (Recordset)Company.GetBusinessObject(BoObjectTypes.BoRecordset);
        rs.DoQuery(sql);
        return rs;
    }

    public static Recordset ExecQuery(string storeName, Dictionary<string, object> parameters)
    {
        if (string.IsNullOrWhiteSpace(storeName))
            throw new ArgumentException("Store name is required.", nameof(storeName));

        if (parameters == null || parameters.Count == 0)
            throw new ArgumentException("Parameters are required.", nameof(parameters));

        string sql = QueryBuilder.BuildExecuteStore(storeName, parameters, DbType);
        return ExecQuery(sql);
    }

    public static object? GetScalarValue(string sql)
    {
        try
        {
            var rs = ExecQuery(sql);
            return rs.RecordCount > 0 ? rs.Fields.Item(0).Value : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Document Operations

    public static Documents GetDocument(object objectType, int docEntry)
    {
        var oObjectType = (BoObjectTypes)Enum.ToObject(typeof(BoObjectTypes), Convert.ToInt32(objectType));
        var document = (Documents)Company.GetBusinessObject(oObjectType);

        if (document.GetByKey(docEntry))
            return document;

        throw new Exception($"SAP Document not found: ObjectType={objectType}, DocEntry={docEntry}");
    }

    public static (bool Success, string Message) UpdateDocument(
        object objectType,
        int docKey,
        Dictionary<string, object> values)
    {
        try
        {
            var oObjectType = (BoObjectTypes)Enum.ToObject(typeof(BoObjectTypes), Convert.ToInt32(objectType));

            if (oObjectType == BoObjectTypes.oStockTransfer)
                return UpdateStockTransfer(oObjectType, docKey, values);

            return UpdateGeneralDocument(oObjectType, docKey, values);
        }
        catch (Exception ex)
        {
            return (false, $"Error updating document: {ex.Message}");
        }
    }

    private static (bool Success, string Message) UpdateStockTransfer(
        BoObjectTypes objectType, int docKey, Dictionary<string, object> values)
    {
        var transfer = (StockTransfer)Company.GetBusinessObject(objectType);
        if (!transfer.GetByKey(docKey))
            return (false, $"Stock Transfer not found: DocEntry={docKey}");

        foreach (var (key, value) in values)
        {
            if (key == "Comments")
                transfer.Comments = value as string;

            if (key.StartsWith("U_") && value != null)
                transfer.UserFields.Fields.Item(key).Value = value;
        }

        int result = transfer.Update();
        return result == 0
            ? (true, string.Empty)
            : (false, Company.GetLastErrorDescription());
    }

    private static (bool Success, string Message) UpdateGeneralDocument(
        BoObjectTypes objectType, int docKey, Dictionary<string, object> values)
    {
        var document = (Documents)Company.GetBusinessObject(objectType);
        if (!document.GetByKey(docKey))
            return (false, $"Document not found: DocEntry={docKey}");

        foreach (var (key, value) in values)
        {
            if (key == "NumAtCard")
                document.NumAtCard = value as string;

            if (key.StartsWith("U_") && value != null)
            {
                try { document.UserFields.Fields.Item(key).Value = value.ToString(); }
                catch { }
            }
        }

        int result = document.Update();
        return result == 0
            ? (true, string.Empty)
            : (false, Company.GetLastErrorDescription());
    }

    #endregion

    #region Raw Data Update

    public static bool UpdateData(
        string[] tables,
        Dictionary<string, object> values,
        Dictionary<string, object> conditions,
        out string message)
    {
        message = string.Empty;
        try
        {
            if (tables == null || tables.Length == 0) return false;
            if (values == null || values.Count == 0) return false;
            if (conditions == null || conditions.Count == 0) return false;

            foreach (string table in tables)
            {
                string sql = QueryBuilder.BuildUpdateQuery(table, values, conditions);
                ExecQuery(sql);
            }
            return true;
        }
        catch (Exception ex)
        {
            message = $"Error updating data: {ex.Message}";
            return false;
        }
    }

    #endregion

    #region Utilities

    public static void ReleaseObject(object obj)
    {
        System.Runtime.InteropServices.Marshal.ReleaseComObject(obj);
        GC.Collect();
    }

    #endregion
}
