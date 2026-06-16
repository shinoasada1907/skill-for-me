namespace TemplateAddOnDotNetCore;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        _ = new App();
    }
}
