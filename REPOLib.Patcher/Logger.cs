using BepInEx.Logging;

namespace REPOLib.Patcher;

internal static class Logger
{
    public static ManualLogSource ManualLogSource
    {
        get
        {
            _manualLogSource ??= BepInEx.Logging.Logger.CreateLogSource(MyPluginInfo.PLUGIN_GUID);
            return _manualLogSource;
        }
    }

    private static ManualLogSource _manualLogSource;

    public static void LogDebug(object data)
    {
        Log(LogLevel.Debug, data);
    }

    public static void LogInfo(object data)
    {
        Log(LogLevel.Info, data);
    }

    public static void LogMessage(object data)
    {
        Log(LogLevel.Message, data);
    }

    public static void LogWarning(object data)
    {
        Log(LogLevel.Warning, data);
    }

    public static void LogError(object data)
    {
        Log(LogLevel.Error, data);
    }

    public static void LogFatal(object data)
    {
        Log(LogLevel.Fatal, data);
    }

    public static void Log(LogLevel logLevel, object data)
    {
        ManualLogSource.Log(logLevel, data);
    }
}
