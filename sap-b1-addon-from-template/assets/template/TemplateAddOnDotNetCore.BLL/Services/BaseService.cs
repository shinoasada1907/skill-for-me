using TemplateAddOnDotNetCore.DAL.Base;

namespace TemplateAddOnDotNetCore.BLL.Services;

/// <summary>
/// Base class for all business logic services.
/// Provides access to DAL layer through SapDiBase.
///
/// Example usage:
/// <code>
/// public class OrderService : BaseService
/// {
///     public (bool Success, string Message) ApproveOrder(int docEntry)
///     {
///         var doc = SapDiBase.GetDocument(13, docEntry); // 13 = A/R Invoice
///         // Business validation logic here...
///         var values = new Dictionary&lt;string, object&gt; { { "U_Approved", "Y" } };
///         return SapDiBase.UpdateDocument(13, docEntry, values);
///     }
/// }
/// </code>
/// </summary>
public abstract class BaseService
{
}
