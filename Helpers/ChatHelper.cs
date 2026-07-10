using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;

namespace AdvancedTeamBalance;

public static class ChatHelper
{
    /// <summary>
    /// Prints a localized message to one player. With includePrefix the plugin
    /// tag is passed as {0} and translation arguments start at {1}.
    /// </summary>
    public static void PrintLocalizedChat(CCSPlayerController? player, bool includePrefix, string key, params object[] args)
    {
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            return;

        var localizer = Plugin.Localization;
        if (localizer == null)
        {
            Log.Warning($"Localizer not ready, dropped message '{key}'");
            return;
        }

        object[] finalArgs = includePrefix
            ? [Plugin.Instance?.Config.General.PluginTag.ReplaceColorTags() ?? string.Empty, .. args]
            : args;

        try
        {
            player.PrintToChat(localizer.ForPlayer(player, key, finalArgs));
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to print translation '{key}': {ex.Message}");
        }
    }

    public static void PrintLocalizedChatAll(bool includePrefix, string key, params object[] args)
    {
        foreach (var player in Utilities.GetPlayers())
            PrintLocalizedChat(player, includePrefix, key, args);
    }
}
