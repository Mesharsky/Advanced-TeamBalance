using Microsoft.Extensions.Logging;

namespace AdvancedTeamBalance;

internal static class Log
{
    internal static bool DebugEnabled { get; set; }

    private static ILogger? Logger => Plugin.Instance?.Logger;

    public static void Debug(string message)
    {
        if (DebugEnabled)
            Logger?.LogInformation("{Message}", message);
    }

    public static void Info(string message) => Logger?.LogInformation("{Message}", message);

    public static void Warning(string message) => Logger?.LogWarning("{Message}", message);

    public static void Error(string message) => Logger?.LogError("{Message}", message);
}
