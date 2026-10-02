using System.Globalization;
using System.Text.RegularExpressions;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Assistant;

namespace SpaceNotch.Core.Launcher;

/// <summary>Ce qu'une commande tapée fait.</summary>
public enum LauncherCommandKind
{
    /// <summary>Lance un minuteur.</summary>
    Timer,

    /// <summary>Règle le volume.</summary>
    Volume,

    /// <summary>Copie une couleur (et l'affiche, par le presse-papier).</summary>
    Color,

    /// <summary>Capture de texte (W4) : tracer un rectangle, le texte part au presse-papier.</summary>
    Capture,

    /// <summary>Un rappel compris dans une phrase (I2).</summary>
    Reminder,

    /// <summary>Ne pas déranger jusqu'à une heure (I2).</summary>
    Quiet,

    /// <summary>Demander au modèle ce que la grammaire n'a pas compris (I2).</summary>
    Ask
}

/// <summary>Une commande reconnue, avec ce que la ligne de résultat en montre.</summary>
/// <param name="Tags">Les étiquettes d'une phrase comprise (I2) : « Rappel », « 17:00 », « Appeler Paul ».</param>
public sealed record LauncherCommand(LauncherCommandKind Kind, string Title, string Subtitle, string Target, IReadOnlyList<string>? Tags = null);

/// <summary>
/// Commandes tapées (F4) : dans Alt+Espace, une commande courte montre son
/// résultat avant Entrée — « timer 10 » prépare un minuteur de dix minutes,
/// « vol 30 » le niveau, « #7FE6FF » la couleur. L'analyse est locale, sans
/// réseau ; la commande devient une ligne de résultat comme une autre, en
/// tête. La cible est une adresse <c>cmd:</c> que l'application exécute.
/// </summary>
public static partial class LauncherCommands
{
    public const string Prefix = "cmd:";

    /// <summary>Durée maximale d'un minuteur lancé ainsi.</summary>
    public static readonly TimeSpan MaxTimer = TimeSpan.FromHours(24);

    public static bool TryParse(string? query, bool french, out LauncherCommand command, DateTimeOffset? now = null)
    {
        command = null!;
        string q = (query ?? string.Empty).Trim();

        if (q.Length == 0)
        {
            return false;
        }

        Match timer = TimerPattern().Match(q);

        if (timer.Success)
        {
            double amount = double.Parse(timer.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            bool seconds = timer.Groups["u"].Value.StartsWith('s');
            bool hours = timer.Groups["u"].Value.StartsWith('h');
            TimeSpan duration = seconds ? TimeSpan.FromSeconds(amount) : hours ? TimeSpan.FromHours(amount) : TimeSpan.FromMinutes(amount);

            if (duration <= TimeSpan.Zero || duration > MaxTimer)
            {
                return false;
            }

            string shown = duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

            command = new LauncherCommand(
                LauncherCommandKind.Timer,
                (french ? "Minuteur " : "Timer ") + shown,
                french ? "Entrée pour lancer" : "Enter to start",
                Prefix + "timer:" + ((int)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture));
            return true;
        }

        Match volume = VolumePattern().Match(q);

        if (volume.Success)
        {
            int level = int.Parse(volume.Groups["n"].Value, CultureInfo.InvariantCulture);

            if (level is < 0 or > 100)
            {
                return false;
            }

            command = new LauncherCommand(
                LauncherCommandKind.Volume,
                $"Volume {level} %",
                french ? "Entrée pour régler" : "Enter to set",
                Prefix + "volume:" + level.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        // Une couleur s'écrit avec un dièse ou une fonction : « 7FE6FF » seul
        // reste une recherche, il pourrait être un nom de fichier.
        if ((q.StartsWith('#') || q.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) || q.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
            && ColorCode.TryParse(q, out ColorCode color))
        {
            command = new LauncherCommand(
                LauncherCommandKind.Color,
                color.Hex,
                color.Rgb + " · " + (french ? "Entrée pour copier" : "Enter to copy"),
                Prefix + "color:" + color.Hex.TrimStart('#'));
            return true;
        }

        // Langage naturel (I2) : « rappelle-moi d'appeler Paul à 17 h ».
        if (NaturalCommand.Parse(q, now ?? DateTimeOffset.Now) is { } intent)
        {
            command = FromIntent(intent, french, now ?? DateTimeOffset.Now);
            return true;
        }

        if (CapturePattern().IsMatch(q))
        {
            command = new LauncherCommand(
                LauncherCommandKind.Capture,
                french ? "Capturer du texte" : "Capture text",
                french ? "Tracer un rectangle · le texte est copié" : "Draw a rectangle · the text is copied",
                Prefix + "capture:text");
            return true;
        }

        return false;
    }

    /// <summary>La ligne de résultat d'une phrase comprise : ce qui sera fait, et quand.</summary>
    public static LauncherCommand FromIntent(NaturalIntent intent, bool french, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(intent);

        IReadOnlyList<IntentChip> parts = intent.Chips(french, now);
        string chips = string.Join(" · ", parts.Select(c => c.Value));
        string enter = french ? "Entrée pour valider" : "Enter to confirm";

        // Maquette I2 : la phrase se découpe en étiquettes — le genre, l'heure, l'action.
        IReadOnlyList<string> tags = intent.Kind == NaturalKind.Reminder
            ? [parts[0].Label, parts[1].Value, Capitalize(intent.Text ?? string.Empty)]
            : [parts[0].Label, parts[0].Value];

        return intent.Kind switch
        {
            NaturalKind.Reminder => new LauncherCommand(
                LauncherCommandKind.Reminder,
                (french ? "Rappel : " : "Reminder: ") + intent.Text,
                chips + " · " + enter,
                Prefix + "reminder:" + intent.At!.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "|" + intent.Text,
                tags),
            NaturalKind.Quiet => new LauncherCommand(
                LauncherCommandKind.Quiet,
                french ? "Ne pas déranger" : "Do not disturb",
                chips + " · " + enter,
                Prefix + "quiet:" + intent.At!.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                tags),
            NaturalKind.Timer => new LauncherCommand(
                LauncherCommandKind.Timer,
                (french ? "Minuteur " : "Timer ") + chips,
                enter,
                Prefix + "timer:" + ((int)intent.Duration!.Value.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                tags),
            _ => new LauncherCommand(
                LauncherCommandKind.Volume,
                $"Volume {intent.Level} %",
                enter,
                Prefix + "volume:" + intent.Level!.Value.ToString(CultureInfo.InvariantCulture),
                tags)
        };
    }

    private static string Capitalize(string text)
        => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];

    /// <summary>La ligne « Demander à … » quand un modèle est choisi et que rien d'autre n'a compris.</summary>
    public static LauncherCommand AskCommand(string query, string modelName, bool french)
        => new(
            LauncherCommandKind.Ask,
            (french ? "Demander à " : "Ask ") + modelName,
            query,
            Prefix + "ask:" + query);

    /// <summary>Lit une cible <c>cmd:</c> : le genre et sa valeur.</summary>
    public static bool TryRead(string? target, out LauncherCommandKind kind, out string value)
    {
        kind = default;
        value = string.Empty;

        if (target is null || !target.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = target[Prefix.Length..].Split(':', 2);

        if (parts.Length != 2)
        {
            return false;
        }

        value = parts[1];
        switch (parts[0])
        {
            case "timer": kind = LauncherCommandKind.Timer; return true;
            case "volume": kind = LauncherCommandKind.Volume; return true;
            case "color": kind = LauncherCommandKind.Color; return true;
            case "capture": kind = LauncherCommandKind.Capture; return true;
            case "reminder": kind = LauncherCommandKind.Reminder; return true;
            case "quiet": kind = LauncherCommandKind.Quiet; return true;
            case "ask": kind = LauncherCommandKind.Ask; return true;
            default: return false;
        }
    }

    [GeneratedRegex(@"^(?:ocr|texte|text|capture|capturer(?: du)? texte|capture text|copier (?:le )?texte)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CapturePattern();

    [GeneratedRegex(@"^(?:timer|minuteur|min)\s+(?<n>\d{1,4}(?:[.,]\d+)?)\s*(?<u>s|sec|secondes?|seconds?|m|min|minutes?|h|heures?|hours?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimerPattern();

    [GeneratedRegex(@"^(?:vol|volume)\s+(?<n>\d{1,3})\s*%?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumePattern();
}
