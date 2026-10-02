using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpaceNotch.Core.Channel;

/// <summary>État d'un travail annoncé par le canal local.</summary>
public enum ChannelState
{
    /// <summary>En cours.</summary>
    Working = 0,

    /// <summary>Terminé : la notch le dit, puis l'oublie.</summary>
    Done = 1,

    /// <summary>En échec.</summary>
    Error = 2,

    /// <summary>En attente d'une réponse de l'utilisateur (agents seulement).</summary>
    Waiting = 3
}

/// <summary>Un message reçu sur le canal local.</summary>
public abstract record ChannelMessage(string Id);

/// <summary>
/// Progression ouverte (W1) : un script, un build ou un rendu dit où il en
/// est. Étapes facultatives (« 3/4 · Tests ») et avancement de l'étape.
/// </summary>
public sealed record ProgressMessage(
    string Id,
    string Title,
    string? Label,
    int Step,
    int Steps,
    double? Fraction,
    ChannelState State) : ChannelMessage(Id);

/// <summary>
/// Agent IA (I4) : Claude Code, Codex ou un script long. Il réfléchit, attend
/// une réponse, a fini. <see cref="Question"/> n'est posée que dans l'état
/// <see cref="ChannelState.Waiting"/>.
/// </summary>
public sealed record AgentMessage(
    string Id,
    string Name,
    string? Detail,
    string? Question,
    ChannelState State) : ChannelMessage(Id);

/// <summary>
/// Notification d'un script : « build.ps1 · Script terminé · 42 s ». Avec
/// <see cref="Open"/>, la carte propose de l'ouvrir (un journal, un dossier,
/// une page) à côté de « Ignorer ».
/// </summary>
public sealed record NotifyMessage(
    string Id,
    string Title,
    string? Body,
    string? Source,
    string? Open,
    string? OpenLabel,
    ChannelState State) : ChannelMessage(Id);

/// <summary>Retire une activité annoncée par le canal.</summary>
public sealed record ClearMessage(string Id) : ChannelMessage(Id);

/// <summary>
/// Le canal local de la notch : des messages JSON d'une ligne, envoyés par
/// l'exécutable lui-même (<c>SpaceNotch.exe --progress …</c>, <c>--hook</c>)
/// sur un tube nommé réservé à l'utilisateur. Voir ADR-024.
///
/// <para>
/// Tout ce qui arrive est borné : identifiants courts, textes coupés, étapes
/// plafonnées. Un message mal formé est ignoré, jamais une exception.
/// </para>
/// </summary>
public static partial class ChannelProtocol
{
    /// <summary>Longueur maximale d'un texte affiché.</summary>
    public const int MaxText = 80;

    /// <summary>Nombre maximal d'étapes d'une progression.</summary>
    public const int MaxSteps = 12;

    /// <summary>Taille maximale d'une ligne reçue, en caractères.</summary>
    public const int MaxLine = 4096;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,39}$")]
    private static partial Regex IdPattern();

    /// <summary>Vrai pour un identifiant accepté : minuscules, chiffres, « . _ - », 40 caractères au plus.</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Lit une ligne ; <c>null</c> si elle est mal formée ou trop longue.</summary>
    public static ChannelMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLine)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(line) is not JsonObject o)
            {
                return null;
            }

            string? id = Text(o, "id")?.ToLowerInvariant();

            if (!IsValidId(id))
            {
                return null;
            }

            return Text(o, "type") switch
            {
                "progress" => ParseProgress(id!, o),
                "agent" => ParseAgent(id!, o),
                "notify" => ParseNotify(id!, o),
                "clear" => new ClearMessage(id!),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Écrit un message en une ligne JSON.</summary>
    public static string Serialize(ChannelMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var o = new JsonObject { ["id"] = message.Id };

        switch (message)
        {
            case ProgressMessage p:
                o["type"] = "progress";
                o["title"] = p.Title;
                if (p.Label is not null) o["label"] = p.Label;
                if (p.Steps > 0) { o["step"] = p.Step; o["steps"] = p.Steps; }
                if (p.Fraction is { } f) o["progress"] = f;
                o["state"] = StateName(p.State);
                break;
            case AgentMessage a:
                o["type"] = "agent";
                o["name"] = a.Name;
                if (a.Detail is not null) o["detail"] = a.Detail;
                if (a.Question is not null) o["question"] = a.Question;
                o["state"] = StateName(a.State);
                break;
            case NotifyMessage n:
                o["type"] = "notify";
                o["title"] = n.Title;
                if (n.Body is not null) o["body"] = n.Body;
                if (n.Source is not null) o["source"] = n.Source;
                if (n.Open is not null) o["open"] = n.Open;
                if (n.OpenLabel is not null) o["openLabel"] = n.OpenLabel;
                o["state"] = StateName(n.State);
                break;
            default:
                o["type"] = "clear";
                break;
        }

        return o.ToJsonString();
    }

    /// <summary>Réponse de la notch à une question d'agent : une ligne « allow » ou « deny ».</summary>
    public static string Answer(bool allow) => allow ? "allow" : "deny";

    /// <summary>
    /// Lit la commande <c>--progress</c> : <c>--id build --title Build --step 3/4
    /// --label Tests --percent 40 --done|--error|--clear</c>.
    /// </summary>
    public static ChannelMessage? FromArguments(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? Value(string name)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        bool Flag(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        string id = (Value("--id") ?? "progress").ToLowerInvariant();

        if (!IsValidId(id))
        {
            return null;
        }

        if (Flag("--clear"))
        {
            return new ClearMessage(id);
        }

        ChannelState outcome = Flag("--done") ? ChannelState.Done : Flag("--error") ? ChannelState.Error : ChannelState.Working;

        if (Flag("--notify"))
        {
            return new NotifyMessage(
                id,
                Clip(Value("--title")) ?? id,
                Clip(Value("--body")),
                Clip(Value("--source")),
                Target(Value("--open")),
                Clip(Value("--open-label")),
                outcome);
        }

        (int step, int steps) = ParseSteps(Value("--step"));
        double? fraction = double.TryParse(Value("--percent"), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
            ? Math.Clamp(percent / 100, 0, 1)
            : null;

        return new ProgressMessage(id, Clip(Value("--title")) ?? id, Clip(Value("--label")), step, steps, fraction, outcome);
    }

    private static ProgressMessage ParseProgress(string id, JsonObject o)
    {
        int steps = Math.Clamp(Int(o, "steps") ?? 0, 0, MaxSteps);
        int step = steps == 0 ? 0 : Math.Clamp(Int(o, "step") ?? 1, 1, steps);
        double? fraction = Number(o, "progress") is { } f && !double.IsNaN(f) ? Math.Clamp(f, 0, 1) : null;

        return new ProgressMessage(id, Clip(Text(o, "title")) ?? id, Clip(Text(o, "label")), step, steps, fraction, State(Text(o, "state")));
    }

    private static AgentMessage ParseAgent(string id, JsonObject o)
    {
        ChannelState state = State(Text(o, "state"));
        string? question = state == ChannelState.Waiting ? Clip(Text(o, "question")) : null;
        return new AgentMessage(id, Clip(Text(o, "name")) ?? id, Clip(Text(o, "detail")), question, state);
    }

    private static NotifyMessage ParseNotify(string id, JsonObject o)
        => new(
            id,
            Clip(Text(o, "title")) ?? id,
            Clip(Text(o, "body")),
            Clip(Text(o, "source")),
            Target(Text(o, "open")),
            Clip(Text(o, "openLabel")),
            State(Text(o, "state")));

    /// <summary>Longueur maximale d'une cible à ouvrir.</summary>
    public const int MaxTarget = 260;

    /// <summary>
    /// Ce qu'une notification peut proposer d'ouvrir : un chemin absolu ou une
    /// page web, rien d'autre. Ni commande, ni protocole d'application, ni chemin
    /// relatif (qui dépendrait du dossier de la notch, pas du script).
    /// </summary>
    public static string? Target(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string target = value.Trim();

        if (target.Length > MaxTarget || target.Any(char.IsControl))
        {
            return null;
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return uri.AbsoluteUri;
        }

        bool drive = target.Length >= 3 && char.IsAsciiLetter(target[0]) && target[1] == ':' && target[2] is '\\' or '/';
        bool share = target.StartsWith(@"\\", StringComparison.Ordinal) && !target.StartsWith(@"\\?", StringComparison.Ordinal) && !target.StartsWith(@"\\.", StringComparison.Ordinal);
        return drive || share ? target : null;
    }

    private static (int Step, int Steps) ParseSteps(string? value)
    {
        if (value?.Split('/') is [string a, string b]
            && int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)
            && int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps)
            && steps > 0)
        {
            steps = Math.Min(steps, MaxSteps);
            return (Math.Clamp(step, 1, steps), steps);
        }

        return (0, 0);
    }

    private static ChannelState State(string? name) => name switch
    {
        "done" => ChannelState.Done,
        "error" => ChannelState.Error,
        "waiting" => ChannelState.Waiting,
        _ => ChannelState.Working
    };

    private static string StateName(ChannelState state) => state switch
    {
        ChannelState.Done => "done",
        ChannelState.Error => "error",
        ChannelState.Waiting => "waiting",
        _ => "working"
    };

    /// <summary>Un texte d'une ligne, sans caractères de contrôle, coupé à <see cref="MaxText"/>.</summary>
    public static string? Clip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string flat = new(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        flat = string.Join(' ', flat.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= MaxText ? flat : string.Concat(flat.AsSpan(0, MaxText - 1), "…");
    }

    private static string? Text(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static int? Int(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue(out int i) ? i : null;

    private static double? Number(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue(out double d) ? d : null;
}
