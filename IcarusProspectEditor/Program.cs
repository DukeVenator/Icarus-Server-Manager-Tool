using IcarusProspectEditor.Services;

namespace IcarusProspectEditor;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        Application.ThreadException += (_, exArgs) =>
        {
            AppLogService.Error($"Unhandled UI thread exception.{Environment.NewLine}{AppLogService.DumpRecentActions()}", exArgs.Exception);
            MessageBox.Show(
                "An unexpected UI error occurred. Details were written to the log folder.",
                "Unexpected error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, exArgs) =>
        {
            var ex = exArgs.ExceptionObject as Exception
                     ?? new Exception($"Non-Exception unhandled object: {exArgs.ExceptionObject}");
            AppLogService.Error(
                $"Unhandled application exception (terminating={exArgs.IsTerminating}).{Environment.NewLine}{AppLogService.DumpRecentActions()}",
                ex);
        };
        ApplicationConfiguration.Initialize();
        AppLogService.LogSessionStart();
        Application.Run(new MainForm());
    }    
}