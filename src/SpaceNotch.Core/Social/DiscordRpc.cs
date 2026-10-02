using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpaceNotch.Core.Social;

/// <summary>Les codes de trame de l'IPC de Discord.</summary>
public enum DiscordOpcode
{
    Handshake = 0,
    Frame = 1,
    Close = 2,
    Ping = 3,
    Pong = 4
}

/// <summary>
/// Le protocole RPC local de Discord (T3) : le client de bureau écoute sur
/// <c>\\.\pipe\discord-ipc-0</c> (jusqu'à 9). Chaque trame est un code et une
/// longueur sur 4 octets petit-boutistes, puis du JSON en UTF-8. Ici, seulement
/// le format et les messages ; le tube est ouvert par la plateforme.
/// </summary>
public static class DiscordRpc
{
    /// <summary>L'adresse de retour à déclarer dans l'application Discord (non appelée : le code passe par le RPC).</summary>
    public const string RedirectUri = "http://localhost";

    /// <summary>Taille maximale d'une trame acceptée : au-delà, la connexion est fermée.</summary>
    public const int MaxFrame = 64 * 1024;

    /// <summary>Les permissions demandées : lire la salle vocale et couper son micro.</summary>
    public static readonly IReadOnlyList<string> Scopes = ["rpc", "rpc.voice.read", "rpc.voice.write"];

    public static byte[] Encode(DiscordOpcode opcode, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), (int)opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), payload.Length);
        payload.CopyTo(frame, 8);
        return frame;
    }

    /// <summary>L'en-tête d'une trame : son code et la longueur du JSON qui suit.</summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> header, out DiscordOpcode opcode, out int length)
    {
        opcode = default;
        length = 0;

        if (header.Length < 8)
        {
            return false;
        }

        int op = BinaryPrimitives.ReadInt32LittleEndian(header[..4]);
        length = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
        opcode = (DiscordOpcode)op;

        return op is >= 0 and <= 4 && length is >= 0 and <= MaxFrame;
    }

    public static string Handshake(string clientId) => new JsonObject { ["v"] = 1, ["client_id"] = clientId }.ToJsonString();

    public static string Authorize(string clientId, string nonce) => Command("AUTHORIZE", new JsonObject
    {
        ["client_id"] = clientId,
        ["scopes"] = new JsonArray([.. Scopes.Select(s => (JsonNode)JsonValue.Create(s))])
    }, nonce);

    public static string Authenticate(string accessToken, string nonce)
        => Command("AUTHENTICATE", new JsonObject { ["access_token"] = accessToken }, nonce);

    public static string GetSelectedVoiceChannel(string nonce) => Command("GET_SELECTED_VOICE_CHANNEL", new JsonObject(), nonce);

    public static string GetVoiceSettings(string nonce) => Command("GET_VOICE_SETTINGS", new JsonObject(), nonce);

    public static string SetMute(bool mute, string nonce) => Command("SET_VOICE_SETTINGS", new JsonObject { ["mute"] = mute }, nonce);

    /// <summary>S'abonne à un évènement ; ceux d'une salle demandent son identifiant.</summary>
    public static string Subscribe(string evt, string? channelId, string nonce)
    {
        var args = new JsonObject();

        if (channelId is not null)
        {
            args["channel_id"] = channelId;
        }

        return new JsonObject { ["cmd"] = "SUBSCRIBE", ["evt"] = evt, ["args"] = args, ["nonce"] = nonce }.ToJsonString();
    }

    public static string Unsubscribe(string evt, string channelId, string nonce)
        => new JsonObject { ["cmd"] = "UNSUBSCRIBE", ["evt"] = evt, ["args"] = new JsonObject { ["channel_id"] = channelId }, ["nonce"] = nonce }.ToJsonString();

    /// <summary>Les évènements d'une salle vocale à suivre.</summary>
    public static readonly IReadOnlyList<string> RoomEvents = ["VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE", "SPEAKING_START", "SPEAKING_STOP"];

    /// <summary>Le corps de l'échange du code contre un jeton (OAuth2, <c>authorization_code</c>).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> TokenRequest(string clientId, string clientSecret, string code) =>
    [
        new("client_id", clientId),
        new("client_secret", clientSecret),
        new("grant_type", "authorization_code"),
        new("code", code),
        new("redirect_uri", RedirectUri)
    ];

    /// <summary>Le corps du renouvellement d'un jeton.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> RefreshRequest(string clientId, string clientSecret, string refreshToken) =>
    [
        new("client_id", clientId),
        new("client_secret", clientSecret),
        new("grant_type", "refresh_token"),
        new("refresh_token", refreshToken)
    ];

    /// <summary>Le code rendu par <c>AUTHORIZE</c>, une fois accepté dans Discord.</summary>
    public static string? ReadCode(string json) => Read(json, "data", "code");

    /// <summary>Le jeton d'accès et celui de renouvellement d'une réponse OAuth2.</summary>
    public static (string Access, string? Refresh, int ExpiresIn)? ReadToken(string json)
    {
        if (Read(json, "access_token") is not { } access)
        {
            return null;
        }

        int expires = 0;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("expires_in", out JsonElement e) && e.ValueKind == JsonValueKind.Number)
            {
                expires = e.GetInt32();
            }
        }
        catch (JsonException)
        {
        }

        return (access, Read(json, "refresh_token"), expires);
    }

    private static string? Read(string json, params string[] path)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement node = doc.RootElement;

            foreach (string key in path)
            {
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(key, out node))
                {
                    return null;
                }
            }

            return node.ValueKind == JsonValueKind.String ? node.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Un identifiant d'application Discord : un nombre de 17 à 20 chiffres.</summary>
    public static bool IsClientId(string? value)
        => value is { Length: >= 17 and <= 20 } && value.All(char.IsAsciiDigit);

    private static string Command(string cmd, JsonObject args, string nonce)
        => new JsonObject { ["cmd"] = cmd, ["args"] = args, ["nonce"] = nonce }.ToJsonString();
}

/// <summary>Quelqu'un dans la salle vocale.</summary>
public sealed record VoiceMember(string Id, string Name, bool Speaking, bool Muted);

/// <summary>
/// La salle vocale où l'on est, reconstruite à partir des messages de Discord :
/// qui est là, qui parle, et si son propre micro est coupé.
/// </summary>
public sealed class VoiceRoom
{
    private readonly Dictionary<string, VoiceMember> _members = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>La salle, ou <c>null</c> hors d'une salle vocale.</summary>
    public string? ChannelId { get; private set; }

    public string? ChannelName { get; private set; }

    public bool SelfMuted { get; private set; }

    /// <summary>Dans l'ordre d'arrivée.</summary>
    public IReadOnlyList<VoiceMember> Members => [.. _order.Select(id => _members[id])];

    /// <summary>
    /// Applique un message reçu. Rend ce qui a changé, pour que l'appelant
    /// (re)demande la salle ou s'abonne à ses évènements.
    /// </summary>
    public VoiceChange Apply(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return Apply(doc.RootElement);
        }
        catch (JsonException)
        {
            return VoiceChange.None;
        }
    }

    public VoiceChange Apply(JsonElement message)
    {
        string? cmd = Str(message, "cmd");
        string? evt = Str(message, "evt");

        if (!message.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
        {
            // Hors d'une salle, Discord répond une salle nulle.
            if (cmd == "GET_SELECTED_VOICE_CHANNEL" && ChannelId is not null)
            {
                Clear();
                ChannelId = null;
                return VoiceChange.Channel;
            }

            return VoiceChange.None;
        }

        if (evt == "ERROR")
        {
            return VoiceChange.Error;
        }

        switch (cmd == "DISPATCH" ? evt : cmd)
        {
            case "READY":
                return VoiceChange.Ready;

            case "AUTHORIZE":
                return VoiceChange.Authorized;

            case "AUTHENTICATE":
                return VoiceChange.Authenticated;

            case "GET_SELECTED_VOICE_CHANNEL":
                return LoadChannel(data);

            case "VOICE_CHANNEL_SELECT":
                string? next = Str(data, "channel_id");

                if (next == ChannelId)
                {
                    return VoiceChange.None;
                }

                string? left = ChannelId;
                Clear();
                ChannelId = next;
                return left is null && next is null ? VoiceChange.None : VoiceChange.ChannelSwitched;

            case "VOICE_STATE_CREATE":
            case "VOICE_STATE_UPDATE":
                return Upsert(data) ? VoiceChange.Members : VoiceChange.None;

            case "VOICE_STATE_DELETE":
                if (UserId(data) is { } gone && _members.Remove(gone))
                {
                    _order.Remove(gone);
                    return VoiceChange.Members;
                }

                return VoiceChange.None;

            case "SPEAKING_START":
            case "SPEAKING_STOP":
                bool speaking = (cmd == "DISPATCH" ? evt : cmd) == "SPEAKING_START";

                if (Str(data, "user_id") is { } id && _members.TryGetValue(id, out VoiceMember? member) && member.Speaking != speaking)
                {
                    _members[id] = member with { Speaking = speaking };
                    return VoiceChange.Members;
                }

                return VoiceChange.None;

            case "GET_VOICE_SETTINGS":
            case "SET_VOICE_SETTINGS":
            case "VOICE_SETTINGS_UPDATE":
                if (data.TryGetProperty("mute", out JsonElement mute) && mute.ValueKind is JsonValueKind.True or JsonValueKind.False && mute.GetBoolean() != SelfMuted)
                {
                    SelfMuted = mute.GetBoolean();
                    return VoiceChange.SelfMute;
                }

                return VoiceChange.None;

            default:
                return VoiceChange.None;
        }
    }

    private VoiceChange LoadChannel(JsonElement data)
    {
        Clear();
        ChannelId = Str(data, "id");
        ChannelName = Str(data, "name");

        if (data.TryGetProperty("voice_states", out JsonElement states) && states.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement state in states.EnumerateArray().Take(50))
            {
                Upsert(state);
            }
        }

        return VoiceChange.Channel;
    }

    private bool Upsert(JsonElement data)
    {
        if (UserId(data) is not { } id)
        {
            return false;
        }

        string name = Clip(Str(data, "nick")
            ?? (data.TryGetProperty("user", out JsonElement user) ? Str(user, "global_name") ?? Str(user, "username") : null)
            ?? "?");

        bool muted = data.TryGetProperty("voice_state", out JsonElement vs)
            && (Bool(vs, "mute") || Bool(vs, "self_mute") || Bool(vs, "deaf") || Bool(vs, "self_deaf"));

        bool speaking = _members.TryGetValue(id, out VoiceMember? existing) && existing.Speaking;
        var next = new VoiceMember(id, name, speaking, muted);

        if (existing == next)
        {
            return false;
        }

        if (existing is null)
        {
            _order.Add(id);
        }

        _members[id] = next;
        return true;
    }

    private void Clear()
    {
        _members.Clear();
        _order.Clear();
        ChannelName = null;
    }

    private static string? UserId(JsonElement data)
        => data.TryGetProperty("user", out JsonElement user) ? Str(user, "id") : Str(data, "user_id");

    private static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Bool(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string Clip(string name)
    {
        string clean = new([.. name.Where(c => !char.IsControl(c))]);
        return clean.Length <= 24 ? clean : string.Concat(clean.AsSpan(0, 23), "…");
    }
}

/// <summary>Ce qu'un message a changé.</summary>
public enum VoiceChange
{
    None,
    Ready,
    Authorized,
    Authenticated,
    Error,

    /// <summary>La salle a été lue : s'abonner à ses évènements.</summary>
    Channel,

    /// <summary>On a changé de salle (ou on l'a quittée) : la redemander.</summary>
    ChannelSwitched,

    Members,
    SelfMute
}
