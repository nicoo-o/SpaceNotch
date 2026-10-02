using SpaceNotch.Platform.Windows.Discord;

namespace SpaceNotch_App.Phone;

/// <summary>
/// Le client Discord de la notch et ses secrets, partagés entre la fenêtre
/// (qui le fait tourner) et les réglages (qui le connectent). Le secret de
/// l'application et le jeton sont dans le coffre de Windows (ADR-026).
/// </summary>
internal static class DiscordLink
{
    public const string SecretResource = "SpaceNotch.Discord.Secret";

    public const string TokenResource = "SpaceNotch.Discord.Token";

    /// <summary>Le client en cours, créé par la fenêtre de la notch.</summary>
    public static DiscordIpcClient? Client { get; set; }

    public static bool HasSecret => Assistant.SecretVault.Read(SecretResource) is not null;
}
