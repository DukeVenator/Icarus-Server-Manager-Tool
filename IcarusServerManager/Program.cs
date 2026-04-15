using IcarusServerManager.Services;

namespace IcarusServerManager
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            var logger = new Logger();
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                logger.Error("Unhandled domain exception.", args.ExceptionObject as Exception);
                MessageBox.Show("A fatal error occurred. Check logs for details.");
            };
            Application.ThreadException += (_, args) =>
            {
                logger.Error("Unhandled UI exception.", args.Exception);
                MessageBox.Show("An application error occurred. Check logs for details.");
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                logger.Error("Unobserved task exception.", args.Exception);
                args.SetObserved();
            };

            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}