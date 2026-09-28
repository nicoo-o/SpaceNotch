using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpaceNotch.Core.Channel;

/// <summary>Ce qu'un hook de Claude Code devient dans la notch.</summary>
/// <param name="Message">Message à envoyer à la notch, ou <c>null</c> si l'événement ne la concerne pas.</param>
/// <param name="AwaitsDecision">Vrai pour une demande d'autorisation : le hook attend « Autoriser » ou « Refuser ».</param>
public readonly record struct HookTranslation(AgentMessage? Message, bool AwaitsDecision);

/// <summary>
/// Agents IA dans la notch (I4) : les hooks de Claude Code appellent
/// <c>SpaceNotch.exe --hook</c>, qui lit l'événement sur son entrée standard
/// et le traduit ici.
///
/// <list type="bullet">
/// <item><c>UserPromptSubmit</c> : l'agent réfléchit.</item>
/// <item><c>PermissionRequest</c> : l'agent attend ; la notch pose la question,
/// et la réponse revient à Claude Code (<see cref="Decision"/>).</item>
/// <item><c>Notification</c> : l'agent attend une saisie dans le terminal.</item>
/// <item><c>Stop</c> : terminé.</item>
/// </list>
///
/// <para>
/// Une session = une activité : l'identifiant vient de <c>session_id</c>,
/// pour que deux fenêtres de Claude Code ne se mélangent pas.
/// </para>
/// </summary>
public static class ClaudeHook
{
    /// <summary>Nom affiché de l'agent.</summary>
    public const string AgentName = "Claude Code";

    /// <summary>Événements auxquels la notch s'abonne.</summary>
    public static IReadOnlyList<string> Events { get; } = ["UserPromptSubmit", "PermissionRequest", "Notification", "Stop"];

    /// <summary>Attente maximale d'une réponse, en secondes : moins que le délai du hook.</summary>
    public const int DecisionSeconds = 55;

    /// <summary>Délai déclaré au hook, en secondes.</summary>
    public const int HookTimeoutSeconds = 60;

    /// <summary>Traduit l'entrée JSON d'un hook ; rien si elle est illisible ou d'un autre événement.</summary>
    public static HookTranslation Translate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 256 * 1024)
        {
            return default;
        }

        JsonObject? o;

        try
        {
            o = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return default;
        }

        if (o is null)
        {
            return default;
        }

        string id = IdFor(Text(o, "session_id"));
        string? project = ProjectName(Text(o, "cwd"));

        return Text(o, "hook_event_name") switch
        {
            "UserPromptSubmit" => new(new AgentMessage(id, AgentName, project, null, ChannelState.Working), false),
            "PermissionRequest" => new(new AgentMessage(id, AgentName, project, Describe(Text(o, "tool_name"), o["tool_input"] as JsonObject), ChannelState.Waiting), true),
            "Notification" when Text(o, "notification_type") is "idle_prompt" or "agent_needs_input" or "elicitation_dialog"
                => new(new AgentMessage(id, AgentName, project, Localization.Lang.T("Attend ta réponse dans le terminal", "Waiting for you in the terminal"), ChannelState.Waiting), false),
            "Stop" => new(new AgentMessage(id, AgentName, project, null, ChannelState.Done), false),
            _ => default
        };
    }

    /// <summary>Sortie du hook <c>PermissionRequest</c> pour une réponse de la notch.</summary>
    public static string Decision(bool allow)
    {
        var output = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PermissionRequest",
                ["decision"] = new JsonObject { ["behavior"] = allow ? "allow" : "deny" }
            }
        };

        return output.ToJsonString();
    }

    /// <summary>
    /// Ajoute les hooks de la notch à un <c>settings.json</c> de Claude Code, sans
    /// rien retirer d'autre. Deux ajouts de suite ne dupliquent rien : une entrée
    /// qui appelle déjà <c>--hook</c> est remplacée.
    /// </summary>
    /// <param name="settingsJson">Contenu actuel, vide si le fichier n'existe pas.</param>
    /// <param name="executable">Chemin complet de SpaceNotch.exe.</param>
    public static string Install(string? settingsJson, string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        JsonObject root = string.IsNullOrWhiteSpace(settingsJson)
            ? []
            : JsonNode.Parse(settingsJson) as JsonObject ?? throw new JsonException("settings.json n'est pas un objet.");

        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = [];
            root["hooks"] = hooks;
        }

        string command = $"\"{executable}\" --hook";

        foreach (string name in Events)
        {
            if (hooks[name] is not JsonArray groups)
            {
                groups = [];
                hooks[name] = groups;
            }

            RemoveOurs(groups);

            var hook = new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = name == "PermissionRequest" ? HookTimeoutSeconds : 5
            };

            groups.Add(new JsonObject { ["matcher"] = "*", ["hooks"] = new JsonArray(hook) });
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Retire les hooks de la notch d'un <c>settings.json</c>, et seulement eux.</summary>
    public static string Uninstall(string settingsJson)
    {
        if (JsonNode.Parse(settingsJson) is not JsonObject root || root["hooks"] is not JsonObject hooks)
        {
            return settingsJson;
        }

        foreach (string name in Events)
        {
            if (hooks[name] is JsonArray groups)
            {
                RemoveOurs(groups);

                if (groups.Count == 0)
                {
                    hooks.Remove(name);
                }
            }
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Vrai si le <c>settings.json</c> appelle déjà la notch.</summary>
    public static bool IsInstalled(string? settingsJson)
        => settingsJson is not null && settingsJson.Contains("--hook", StringComparison.Ordinal) && settingsJson.Contains("SpaceNotch", StringComparison.OrdinalIgnoreCase);

    private static void RemoveOurs(JsonArray groups)
    {
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] is not JsonObject group || group["hooks"] is not JsonArray list)
            {
                continue;
            }

            for (int j = list.Count - 1; j >= 0; j--)
            {
                if (list[j] is JsonObject h && Text(h, "command") is { } c
                    && c.Contains("SpaceNotch", StringComparison.OrdinalIgnoreCase) && c.EndsWith("--hook", StringComparison.Ordinal))
                {
                    list.RemoveAt(j);
                }
            }

            if (list.Count == 0)
            {
                groups.RemoveAt(i);
            }
        }
    }

    /// <summary>« Bash · dotnet test », « Edit · Program.cs » : ce que l'agent veut faire.</summary>
    public static string Describe(string? tool, JsonObject? input)
    {
        string name = string.IsNullOrWhiteSpace(tool) ? "?" : tool;
        string? target = input is null ? null
            : Text(input, "command") ?? FileName(Text(input, "file_path")) ?? Text(input, "url") ?? Text(input, "pattern");

        return ChannelProtocol.Clip(target is null ? name : $"{name} · {target}") ?? name;
    }

    private static string IdFor(string? session)
    {
        string suffix = string.IsNullOrEmpty(session)
            ? "0"
            : new string(session.ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c)).Take(8).ToArray());

        return $"claude.{(suffix.Length == 0 ? "0" : suffix)}";
    }

    private static string? ProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return null;
        }

        string trimmed = cwd.TrimEnd('\\', '/');
        int slash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return ChannelProtocol.Clip(slash >= 0 ? trimmed[(slash + 1)..] : trimmed);
    }

    private static string? FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static string? Text(JsonObject o, string key)
        => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
