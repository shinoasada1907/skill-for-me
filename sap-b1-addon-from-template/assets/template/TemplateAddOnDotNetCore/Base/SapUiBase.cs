using SAPbouiCOM;
using TemplateAddOnDotNetCore.Common.Constants;
using TemplateAddOnDotNetCore.DAL.Base;
using Application = SAPbouiCOM.Framework.Application;
using ProgressBar = SAPbouiCOM.ProgressBar;

namespace TemplateAddOnDotNetCore.Base;

/// <summary>
/// Base class for SAP UI API integration.
/// Handles UI connection, DI API initialization, and SAP event registration.
/// Inherit from this class in App.cs to build your Add-On.
/// </summary>
public class SapUiBase
{
    public static ProgressBar? ProgressBar;
    public static int ProcessId;
    public static bool IsProgressBarActive;

    #region Constructor & Initialization

    public SapUiBase()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            Application oApp = args.Length <= 1
                ? new Application()
                : new Application(args[1]);

            ShowStatus("[SBO_UI] Connected to UI API.", false);

            if (!InitializeDiApi())
                return;

            Loading();
            RegisterEvents();

            oApp.Run();

            try { ProcessId = GetProcessId(); }
            catch { }
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(ex.Message);
        }
    }

    private bool InitializeDiApi()
    {
        var company = new SAPbobsCOM.Company();
        string cookie = company.GetContextCookie();
        string context = Application.SBO_Application.Company.GetConnectionContext(cookie);

        if (company.Connected)
            company.Disconnect();

        if (company.SetSboLoginContext(context) != 0)
        {
            ShowStatus("[SBO_DI] Failed setting connection to DI API.", true);
            return false;
        }

        company = (SAPbobsCOM.Company)Application.SBO_Application.Company.GetDICompany();
        if (!company.Connected && company.Connect() != 0)
        {
            ShowStatus("[SBO_DI] Failed connecting to company database.", true);
            ShowMessageBox(company.GetLastErrorDescription());
            return false;
        }

        // Pass Company to DAL layer
        SapDiBase.Company = company;

        // Load user info
        var rs = SapDiBase.ExecQuery(
            $"SELECT \"USERID\" FROM OUSR WHERE \"USER_CODE\"='{company.UserName}'");
        if (rs.RecordCount > 0)
            SapDiBase.UserId = (int)rs.Fields.Item("USERID").Value;

        rs = SapDiBase.ExecQuery(
            $"SELECT x2.\"Name\" FROM OUSR x1 LEFT JOIN OUBR x2 ON x1.\"Branch\"=x2.\"Code\" WHERE x1.\"USER_CODE\"='{company.UserName}'");
        if (rs.RecordCount > 0)
            SapDiBase.BranchName = rs.Fields.Item("Name").Value?.ToString() ?? string.Empty;

        ShowStatus("[SBO_DI] Connected successfully to company database.", false);
        return true;
    }

    private void RegisterEvents()
    {
        Application.SBO_Application.FormDataEvent +=
            new _IApplicationEvents_FormDataEventEventHandler(SBO_Application_FormDataEvent);
        Application.SBO_Application.ItemEvent +=
            new _IApplicationEvents_ItemEventEventHandler(SBO_Application_ItemEvent);
        Application.SBO_Application.MenuEvent +=
            new _IApplicationEvents_MenuEventEventHandler(SBO_Application_MenuEvent);
        Application.SBO_Application.AppEvent +=
            new _IApplicationEvents_AppEventEventHandler(SBO_Application_AppEvent);
        Application.SBO_Application.LayoutKeyEvent +=
            new _IApplicationEvents_LayoutKeyEventEventHandler(SBO_Application_LayoutKeyEvent);
        Application.SBO_Application.ProgressBarEvent +=
            new _IApplicationEvents_ProgressBarEventEventHandler(SBO_Application_ProgressBarEvent);
    }

    #endregion

    #region Virtual Methods (Override in App.cs)

    public virtual void Loading()
    {
        // Override to add menus, load forms, etc.
    }

    public virtual void SBO_Application_FormDataEvent(ref BusinessObjectInfo pVal, out bool BubbleEvent)
    {
        BubbleEvent = true;
    }

    public virtual void SBO_Application_ItemEvent(string FormUID, ref ItemEvent pVal, out bool BubbleEvent)
    {
        BubbleEvent = true;
    }

    public virtual void SBO_Application_MenuEvent(ref MenuEvent pVal, out bool BubbleEvent)
    {
        BubbleEvent = true;
    }

    public virtual void SBO_Application_LayoutKeyEvent(ref LayoutKeyInfo eventInfo, out bool BubbleEvent)
    {
        BubbleEvent = true;
    }

    public virtual void SBO_Application_ProgressBarEvent(ref ProgressBarEvent pVal, out bool BubbleEvent)
    {
        BubbleEvent = true;
        if (pVal.EventType == BoProgressBarEventTypes.pbet_ProgressBarStopped && pVal.BeforeAction)
            StopProgressBar();
    }

    #endregion

    #region App Event (non-virtual)

    private static void SBO_Application_AppEvent(BoAppEventTypes EventType)
    {
        switch (EventType)
        {
            case BoAppEventTypes.aet_ShutDown:
            case BoAppEventTypes.aet_ServerTerminition:
                System.Windows.Forms.Application.Exit();
                break;
        }
    }

    #endregion

    #region UI Helpers

    public static void ShowMessageBox(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;

        if (msg.StartsWith('\n'))
            msg = msg[1..];

        Application.SBO_Application.MessageBox(msg);
    }

    public static void ShowStatus(string msg, bool isError)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;

        if (msg.StartsWith('\n'))
            msg = msg[1..];

        Application.SBO_Application.SetStatusBarMessage(msg, BoMessageTime.bmt_Short, isError);
    }

    public static void FormatForm(SAPbouiCOM.Form form)
    {
        Items items = form.Items;
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items.Item(i);
            if (item.Type is BoFormItemTypes.it_BUTTON or BoFormItemTypes.it_BUTTON_COMBO)
                item.Height = 22;

            if (item.Type is BoFormItemTypes.it_STATIC or BoFormItemTypes.it_CHECK_BOX or BoFormItemTypes.it_COMBO_BOX)
                item.Height = 15;
        }

        form.Left = (Application.SBO_Application.Desktop.Width - form.Width) / 2;
        form.Top = (Application.SBO_Application.Desktop.Height - form.Height) / 2 - 150;
    }

    public static System.Windows.Forms.NativeWindow GetNativeWindow()
    {
        var nw = new System.Windows.Forms.NativeWindow();
        nw.AssignHandle(System.Diagnostics.Process.GetProcessById(ProcessId).MainWindowHandle);
        return nw;
    }

    #endregion

    #region Progress Bar

    public static void StartProgressBar(string text, int length, bool stoppable = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                text = "Processing...";

            ProgressBar = Application.SBO_Application.StatusBar.CreateProgressBar(text, length, stoppable);
            ProgressBar.Text = text;
            ProgressBar.Maximum = length;
            IsProgressBarActive = true;
        }
        catch { }
    }

    public static void UpdateProgressBar(int increment)
    {
        try
        {
            if (ProgressBar == null) return;

            int current = ProgressBar.Value + increment;
            string text = ProgressBar.Text;
            int index = text.IndexOf("...");
            if (index >= 0)
                text = text[..index];

            ProgressBar.Text = $"{text}... {current} of {ProgressBar.Maximum}";
            ProgressBar.Value = current;
        }
        catch { }
    }

    public static void StopProgressBar()
    {
        try
        {
            if (ProgressBar != null)
            {
                ProgressBar.Stop();
                SapDiBase.ReleaseObject(ProgressBar);
                IsProgressBarActive = false;
            }
        }
        catch { }
    }

    #endregion

    #region File Dialogs

    public static string SelectFile(string filter = "Excel|*.xls;*.xlsx")
    {
        string result = string.Empty;

        var thread = new Thread(() =>
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("SAP Business One");
            if (processes.Length == 0) return;

            var nw = new System.Windows.Forms.NativeWindow();
            nw.AssignHandle(processes[0].MainWindowHandle);

            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Multiselect = false,
                Filter = filter,
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(nw) == System.Windows.Forms.DialogResult.OK)
                result = dialog.FileName;
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        while (thread.IsAlive)
            System.Windows.Forms.Application.DoEvents();

        return result;
    }

    public static string SelectFolder()
    {
        string result = string.Empty;

        var thread = new Thread(() =>
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("SAP Business One");
            if (processes.Length == 0) return;

            var nw = new System.Windows.Forms.NativeWindow();
            nw.AssignHandle(processes[0].MainWindowHandle);

            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog(nw) == System.Windows.Forms.DialogResult.OK)
                result = dialog.SelectedPath;
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        while (thread.IsAlive)
            System.Windows.Forms.Application.DoEvents();

        return result;
    }

    #endregion

    #region Process

    private int GetProcessId()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("SAP Business One");
        foreach (var process in processes)
        {
            try
            {
                var sboGuiApi = new SboGuiApi();
                var args = Environment.GetCommandLineArgs();

                if (args.Length <= 1)
                    sboGuiApi.Connect(SapConstants.DebugConnectionString);
                else
                    sboGuiApi.Connect(args[1]);

                int processAppId = sboGuiApi.GetAppIdFromProcessId(process.Id);
                if (Application.SBO_Application.AppId == processAppId)
                    return process.Id;
            }
            catch { }
        }
        return 0;
    }

    #endregion
}
