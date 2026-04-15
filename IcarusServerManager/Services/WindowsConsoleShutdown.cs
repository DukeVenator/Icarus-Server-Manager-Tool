using System.Runtime.InteropServices;

namespace IcarusServerManager.Services;

/// <summary>
/// Sends a Ctrl+C to another process's console (common graceful shutdown for UE / dedicated servers).
/// </summary>
internal static class WindowsConsoleShutdown
{
    private const uint CtrlCEvent = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool ConsoleCtrlDelegate(uint ctrlType);

    private static bool IgnoreCtrl(uint _) => true;

    /// <summary>Returns false if the API calls failed (caller may try other shutdown paths).</summary>
    public static bool TrySendCtrlC(int processId)
    {
        var ignore = new ConsoleCtrlDelegate(IgnoreCtrl);
        var attached = false;
        var handlerInstalled = false;

        try
        {
            _ = FreeConsole();

            if (!AttachConsole((uint)processId))
            {
                return false;
            }

            attached = true;

            if (!SetConsoleCtrlHandler(ignore, true))
            {
                return false;
            }

            handlerInstalled = true;

            if (!GenerateConsoleCtrlEvent(CtrlCEvent, 0))
            {
                return false;
            }

            // Let the target process run its Ctrl handler before we detach.
            Thread.Sleep(300);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (handlerInstalled)
            {
                _ = SetConsoleCtrlHandler(ignore, false);
            }

            if (attached)
            {
                _ = FreeConsole();
            }

            GC.KeepAlive(ignore);
        }
    }
}
