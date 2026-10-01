using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Social;

namespace SpaceNotch.Platform.Windows.Discord;

/// <summary>Où en est la connexion à Discord, pour les réglages.</summary>
public enum DiscordLinkState
{
    /// <summary>Discord n'est pas lancé (aucun tube).</summary>
    NotRunning,

    /// <summary>Connecté, mais pas encore autorisé : « Connecter » dans les réglages.</summary>
    NeedsAuthorization,

    /// <summary>En attente de l'accord dans la fenêtre de Discord.</summary>
    Authorizing,

    /// <summary>Autorisé : la salle vocale est suivie.</summary>
    Connected,

    /// <summary>Refus, identifiant inconnu, ou application non admise par Discord.</summary>
    Failed
}

/// <summary>
/// Client du RPC local de Discord (T3) : le tube <c>discord-ipc-N</c> du client
/// de bureau. Il suit la salle vocale où l'on est et coupe le micro à la demande.
///
/// <para>
/// L'accès passe par l'application Discord de l'utilisateur (son identifiant et
/// son secret, voir ADR-026) : Discord affiche une fois une fenêtre d'accord,
/// puis le jeton est gardé par l'appelant (dans le coffre de Windows). Sans
/// accord, rien n'est lu. La connexion ne quitte jamais la machine, sauf
/// l'échange du code contre un jeton avec <c>discord.com</c>.
/// </para>
/// </summary>
public sealed class DiscordIpcClient : IDisposable
{
    private const string TokenEndpoint = "https://discord.com/api/oauth2/token";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly Func<(string ClientId, string Secret)?> _credentials;
    private readonly Func<string?> _loadToken;
    private readonly Action<string?> _saveToken;
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly VoiceRoom _room = new();
    private CancellationTokenSource? _cts;
    private NamedPipeClientStream? _pipe;
    private string? _subscribedChannel;
    private bool _interactive;
    private bool _refreshed;
    private int _nonce;

    /// <param name="credentials">L'identifiant et le secret de l'application Discord, ou <c>null</c>.</param>
    /// <param name="loadToken">Le jeton gardé (JSON OAuth2), ou <c>null</c>.</param>
    /// <param name="saveToken">Garde (ou efface, avec <c>null</c>) le jeton.</param>
    public DiscordIpcClient(Func<(string ClientId, string Secret)?> credentials, Func<string?> loadToken, Action<string?> saveToken)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _loadToken = loadToken ?? throw new ArgumentNullException(nameof(loadToken));
        _saveToken = saveToken ?? throw new ArgumentNullException(nameof(saveToken));
    }

    /// <summary>La salle a changé : nom, membres, qui parle, micro. Sur un fil d'arrière-plan.</summary>
    public event Action<string?, string?, IReadOnlyList<VoiceMember>, bool>? RoomChanged;

    public event Action<DiscordLinkState>? StateChanged;

    public DiscordLinkState State { get; private set; } = DiscordLinkState.NotRunning;

    /// <summary>Démarre la boucle de connexion (et de reconnexion). Sans effet si elle tourne.</summary>
    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    /// <summary>« Connecter » : la prochaine connexion demandera l'accord dans Discord.</summary>
    public void Authorize()
    {
        _interactive = true;
        Stop();
        Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        ClosePipe();
    }

    /// <summary>Coupe ou rouvre son micro.</summary>
    public async Task<bool> SetMuteAsync(bool mute)
    {
        if (State != DiscordLinkState.Connected)
        {
            return false;
        }

        return await SendAsync(DiscordRpc.SetMute(mute, NextNonce()), CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_credentials() is null)
                {
                    SetState(DiscordLinkState.NeedsAuthorization);
                }
                else if (await ConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ReadLoopAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    SetState(DiscordLinkState.NotRunning);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or JsonException or HttpRequestException or TimeoutException)
            {
                // Discord fermé ou redémarré : on réessaie plus tard.
            }
            catch (OperationCanceledException)
            {
                return;
            }

            ClosePipe();
            Publish(clear: true);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(State == DiscordLinkState.Failed ? 120 : 20), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture), PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(300, cancellationToken).ConfigureAwait(false);
                _pipe = pipe;
                _subscribedChannel = null;
                _refreshed = false;
                (string clientId, _) = _credentials()!.Value;
                await WriteFrameAsync(DiscordOpcode.Handshake, DiscordRpc.Handshake(clientId), cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[8];

        while (!cancellationToken.IsCancellationRequested && _pipe is { IsConnected: true } pipe)
        {
            await pipe.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

            if (!DiscordRpc.TryReadHeader(header, out DiscordOpcode op, out int length))
            {
                return;
            }

            byte[] payload = new byte[length];
            await pipe.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            string json = Encoding.UTF8.GetString(payload);

            switch (op)
            {
                case DiscordOpcode.Ping:
                    await WriteFrameAsync(DiscordOpcode.Pong, json, cancellationToken).ConfigureAwait(false);
                    break;

                case DiscordOpcode.Close:
                    // Refus du client (identifiant inconnu, application non admise).
                    SetState(DiscordLinkState.Failed);
                    return;

                case DiscordOpcode.Frame:
                    await HandleAsync(json, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task HandleAsync(string json, CancellationToken cancellationToken)
    {
        VoiceChange change = _room.Apply(json);

        switch (change)
        {
            case VoiceChange.Ready:
                await OnReadyAsync(cancellationToken).ConfigureAwait(false);
                break;

            case VoiceChange.Authorized:
                if (DiscordRpc.ReadCode(json) is { } code && await ExchangeAsync(DiscordRpc.TokenRequest(_credentials()!.Value.ClientId, _credentials()!.Value.Secret, code), cancellationToken).ConfigureAwait(false) is { } token)
                {
                    _interactive = false;
                    await SendAsync(DiscordRpc.Authenticate(token, NextNonce()), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    SetState(DiscordLinkState.Failed);
                }

                break;

            case VoiceChange.Authenticated:
                SetState(DiscordLinkState.Connected);
                await SendAsync(DiscordRpc.Subscribe("VOICE_CHANNEL_SELECT", null, NextNonce()), cancellationToken).ConfigureAwait(false);
                await SendAsync(DiscordRpc.Subscribe("VOICE_SETTINGS_UPDATE", null, NextNonce()), cancellationToken).ConfigureAwait(false);
                await SendAsync(DiscordRpc.GetVoiceSettings(NextNonce()), cancellationToken).ConfigureAwait(false);
                await SendAsync(DiscordRpc.GetSelectedVoiceChannel(NextNonce()), cancellationToken).ConfigureAwait(false);
                break;

            case VoiceChange.Error:
                await OnErrorAsync(json, cancellationToken).ConfigureAwait(false);
                break;

            case VoiceChange.Channel:
                await FollowChannelAsync(cancellationToken).ConfigureAwait(false);
                Publish();
                break;

            case VoiceChange.ChannelSwitched:
                await FollowChannelAsync(cancellationToken).ConfigureAwait(false);
                Publish();

                if (_room.ChannelId is not null)
                {
                    await SendAsync(DiscordRpc.GetSelectedVoiceChannel(NextNonce()), cancellationToken).ConfigureAwait(false);
                }

                break;

            case VoiceChange.Members:
            case VoiceChange.SelfMute:
                Publish();
                break;
        }
    }

    private async Task OnReadyAsync(CancellationToken cancellationToken)
    {
        (string clientId, _) = _credentials()!.Value;

        if (AccessToken() is { } access)
        {
            await SendAsync(DiscordRpc.Authenticate(access, NextNonce()), cancellationToken).ConfigureAwait(false);
        }
        else if (_interactive)
        {
            SetState(DiscordLinkState.Authorizing);
            await SendAsync(DiscordRpc.Authorize(clientId, NextNonce()), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            SetState(DiscordLinkState.NeedsAuthorization);
        }
    }

    private async Task OnErrorAsync(string json, CancellationToken cancellationToken)
    {
        bool authenticate = json.Contains("\"AUTHENTICATE\"", StringComparison.Ordinal);

        // Jeton expiré : un renouvellement, une fois.
        if (authenticate && !_refreshed && RefreshToken() is { } refresh)
        {
            _refreshed = true;
            (string clientId, string secret) = _credentials()!.Value;

            if (await ExchangeAsync(DiscordRpc.RefreshRequest(clientId, secret, refresh), cancellationToken).ConfigureAwait(false) is { } token)
            {
                await SendAsync(DiscordRpc.Authenticate(token, NextNonce()), cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        if (authenticate || json.Contains("\"AUTHORIZE\"", StringComparison.Ordinal))
        {
            _saveToken(null);
            SetState(_interactive ? DiscordLinkState.Failed : DiscordLinkState.NeedsAuthorization);
            _interactive = false;
        }
    }

    /// <summary>Suit les évènements de la salle courante, et lâche ceux de la précédente.</summary>
    private async Task FollowChannelAsync(CancellationToken cancellationToken)
    {
        string? channel = _room.ChannelId;

        if (channel == _subscribedChannel)
        {
            return;
        }

        if (_subscribedChannel is { } previous)
        {
            foreach (string evt in DiscordRpc.RoomEvents)
            {
                await SendAsync(DiscordRpc.Unsubscribe(evt, previous, NextNonce()), cancellationToken).ConfigureAwait(false);
            }
        }

        _subscribedChannel = channel;

        if (channel is not null)
        {
            foreach (string evt in DiscordRpc.RoomEvents)
            {
                await SendAsync(DiscordRpc.Subscribe(evt, channel, NextNonce()), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<string?> ExchangeAsync(IReadOnlyList<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await Http.PostAsync(new Uri(TokenEndpoint), content, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode || DiscordRpc.ReadToken(body) is not { } token)
        {
            return null;
        }

        _saveToken(body);
        return token.Access;
    }

    private string? AccessToken() => _loadToken() is { } json ? DiscordRpc.ReadToken(json)?.Access : null;

    private string? RefreshToken() => _loadToken() is { } json ? DiscordRpc.ReadToken(json)?.Refresh : null;

    private void Publish(bool clear = false)
    {
        if (clear)
        {
            RoomChanged?.Invoke(null, null, [], false);
            return;
        }

        RoomChanged?.Invoke(_room.ChannelId, _room.ChannelName, _room.Members, _room.SelfMuted);
    }

    private async Task<bool> SendAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            await WriteFrameAsync(DiscordOpcode.Frame, json, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task WriteFrameAsync(DiscordOpcode opcode, string json, CancellationToken cancellationToken)
    {
        byte[] frame = DiscordRpc.Encode(opcode, json);
        await _write.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_pipe is not { IsConnected: true } pipe)
            {
                throw new IOException("Discord n'est pas connecté.");
            }

            await pipe.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _write.Release();
        }
    }

    private string NextNonce() => Interlocked.Increment(ref _nonce).ToString(global::System.Globalization.CultureInfo.InvariantCulture);

    private void SetState(DiscordLinkState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }

    private void ClosePipe()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        pipe?.Dispose();
    }

    public void Dispose()
    {
        Stop();
        _write.Dispose();
    }
}
