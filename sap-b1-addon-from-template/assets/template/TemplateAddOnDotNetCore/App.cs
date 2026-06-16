using SAPbouiCOM;
using TemplateAddOnDotNetCore.Base;
using Application = SAPbouiCOM.Framework.Application;

namespace TemplateAddOnDotNetCore;

/// <summary>
/// Main Add-On application class.
/// Override virtual methods from SapUiBase to implement your business logic.
/// </summary>
public class App : SapUiBase
{
    public override void Loading()
    {
        // TODO: Add menu items, load forms, initialize resources
        // Example:
        // AddMenuItems();
        // LoadCustomForms();
    }

    public override void SBO_Application_ItemEvent(string FormUID, ref ItemEvent pVal, out bool BubbleEvent)
    {
        BubbleEvent = true;

        try
        {
            // TODO: Handle item events
            // Example:
            // if (pVal.FormTypeEx == "133" && pVal.ItemUID == "1" && pVal.BeforeAction)
            // {
            //     // Handle button click on A/R Invoice form
            // }
        }
        catch (Exception ex)
        {
            Application.SBO_Application.MessageBox(ex.Message, 1, "Ok", string.Empty, string.Empty);
        }
    }

    public override void SBO_Application_MenuEvent(ref MenuEvent pVal, out bool BubbleEvent)
    {
        BubbleEvent = true;

        try
        {
            // TODO: Handle menu events
            // Example:
            // if (pVal.MenuUID == MenuConstants.FunctionMenuUid && !pVal.BeforeAction)
            // {
            //     // Open custom form
            // }
        }
        catch (Exception ex)
        {
            Application.SBO_Application.MessageBox(ex.Message, 1, "Ok", string.Empty, string.Empty);
        }
    }

    public override void SBO_Application_FormDataEvent(ref BusinessObjectInfo pVal, out bool BubbleEvent)
    {
        BubbleEvent = true;

        try
        {
            // TODO: Handle form data events
            // Example:
            // if (pVal.FormTypeEx == "133" && pVal.ActionSuccess && pVal.EventType == BoEventTypes.et_FORM_DATA_ADD)
            // {
            //     // After A/R Invoice is added successfully
            // }
        }
        catch (Exception ex)
        {
            Application.SBO_Application.MessageBox(ex.Message, 1, "Ok", string.Empty, string.Empty);
        }
    }

    public override void SBO_Application_LayoutKeyEvent(ref LayoutKeyInfo eventInfo, out bool BubbleEvent)
    {
        BubbleEvent = true;
    }
}
