using Vintagestory.API.Common;

namespace FloatingCombatText.Utils;

#nullable disable
public static class Logger
{
    private static ILogger _logger;
    private static ModSystem _modSystem;
    private static string _logBaseFormat;

    public static void Init(ModSystem modSystem, ILogger logger)
    {
        _modSystem = modSystem;
        _logger = logger;
        _logBaseFormat = "[{0}] {1}";
    }
    
    private static readonly System.Func<string, string> LogMsg = ((x) => string.Format(_logBaseFormat, _modSystem.Mod.Info.Name, x));


    public static void Event(string message) => _logger.Log(EnumLogType.Event, LogMsg(message));
    public static void Notification(string message) => _logger.Log(EnumLogType.Notification, LogMsg(message));
    public static void Log(string message) => _logger.Log(EnumLogType.Build, LogMsg(message));
    public static void Error(string message) => _logger.Log(EnumLogType.Error, LogMsg(message));
    public static void Debug(string message) => _logger.Log(EnumLogType.Debug, LogMsg(message));
}