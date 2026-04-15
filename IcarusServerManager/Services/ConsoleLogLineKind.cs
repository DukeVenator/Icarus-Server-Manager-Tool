namespace IcarusServerManager.Services;

internal enum ConsoleLogLineKind
{
    ManagerError,
    ManagerWarn,
    ManagerInfo,
    GameFatalOrError,
    GameWarning,
    GameImportant,
    GameVerbose,
    GameGeneral
}
