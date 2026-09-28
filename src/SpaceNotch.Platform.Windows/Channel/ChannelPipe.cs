using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Channel;

namespace SpaceNotch.Platform.Windows.Channel;

/// <summary>
/// Le canal local de la notch (ADR-024) : un tube nommé réservé à
/// l'utilisateur courant. Un client écrit une ligne JSON ; pour une question
/// d'agent, le serveur répond une ligne « allow » ou « deny » quand
/// l'utilisateur a tranché.
///
/// <para>
/// <see cref="PipeOptions.CurrentUserOnly"/> ferme le tube aux autres comptes
/// et aux processus d'une autre session : rien n'écoute sur le réseau.
/// </para>
/// </summary>
public static class ChannelPipe
{
    /// <summary>Nom du tube, propre à la session Windows.</summary>
    public static string Name { get; } = BuildName();

    private static string BuildName()
    {
        using var process = global::System.Diagnostics.Process.GetCurrentProcess();
        return $"SpaceNotch.Channel.{Environment.UserName}.{process.SessionId}";
    }

    /// <summary>
    /// Envoie un message à la notch. Renvoie la réponse si <paramref name="waitForAnswer"/>,
    /// sinon une chaîne vide ; <c>null</c> si la notch ne tourne pas ou n'a pas répondu à temps.
    /// </summary>
    public static async Task<string?> SendAsync(ChannelMessage message, bool waitForAnswer, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var cts = new CancellationTokenSource(timeout);

        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            await client.ConnectAsync((int)Math.Min(timeout.TotalMilliseconds, 1500), cts.Token).ConfigureAwait(false);

            var utf8 = new UTF8Encoding(false);
            using var writer = new StreamWriter(client, utf8, 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            await writer.WriteLineAsync(ChannelProtocol.Serialize(message).AsMemory(), cts.Token).ConfigureAwait(false);

            if (!waitForAnswer)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(client, utf8, false, 256, leaveOpen: true);
            return (await reader.ReadLineAsync(cts.Token).ConfigureAwait(false))?.Trim();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Côté notch : accepte les connexions, lit une ligne, la remet à
/// <see cref="Received"/>. Pour une question, la connexion reste ouverte
/// jusqu'à <see cref="Answer"/> — ou jusqu'à ce que le client abandonne.
/// </summary>
public sealed class ChannelServer : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();

    /// <summary>Un message valide est arrivé (sur un fil du pool).</summary>
    public event Action<ChannelMessage>? Received;

    /// <summary>Un client en attente de réponse est parti sans elle (délai dépassé, Ctrl+C).</summary>
    public event Action<string>? Abandoned;

    public void Start() => _ = Task.Run(() => AcceptLoopAsync(_stop.Token));

    /// <summary>Répond à la question de l'agent <paramref name="id"/>.</summary>
    public bool Answer(string id, bool allow)
        => _pending.TryRemove(id, out TaskCompletionSource<bool>? answer) && answer.TrySetResult(allow);

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream server;

            try
            {
                server = new NamedPipeServerStream(
                    ChannelPipe.Name,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException)
            {
                // Une autre notch tient déjà le nom : impossible par le mutex d'instance,
                // mais on n'insiste pas en boucle.
                return;
            }

            try
            {
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }

            _ = Task.Run(() => ServeAsync(server, token), token);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream server, CancellationToken token)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                var utf8 = new UTF8Encoding(false);
                using var reader = new StreamReader(server, utf8, false, 1024, leaveOpen: true);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));

                string? line = await ReadBoundedLineAsync(reader, timeout.Token).ConfigureAwait(false);

                if (ChannelProtocol.Parse(line) is not { } message)
                {
                    return;
                }

                bool asks = message is AgentMessage { State: ChannelState.Waiting, Question: not null };
                TaskCompletionSource<bool>? answer = null;

                if (asks)
                {
                    answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    // Une nouvelle question du même agent remplace l'ancienne : l'ancienne est refusée.
                    if (_pending.TryRemove(message.Id, out TaskCompletionSource<bool>? previous))
                    {
                        previous.TrySetResult(false);
                    }

                    _pending[message.Id] = answer;
                }

                Received?.Invoke(message);

                if (answer is null)
                {
                    return;
                }

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                wait.CancelAfter(TimeSpan.FromSeconds(ClaudeHook.HookTimeoutSeconds));

                try
                {
                    bool allow = await answer.Task.WaitAsync(wait.Token).ConfigureAwait(false);
                    using var writer = new StreamWriter(server, utf8, 64, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
                    await writer.WriteLineAsync(ChannelProtocol.Answer(allow).AsMemory(), wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _pending.TryRemove(new(message.Id, answer));
                    Abandoned?.Invoke(message.Id);
                }
                catch (IOException)
                {
                    // Le client est parti avant la réponse.
                    _pending.TryRemove(new(message.Id, answer));
                    Abandoned?.Invoke(message.Id);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Un client mal élevé ne fait jamais tomber la notch.
            }
        }
    }

    /// <summary>Lit une ligne sans jamais accepter plus de <see cref="ChannelProtocol.MaxLine"/> caractères.</summary>
    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        var line = new StringBuilder();
        var buffer = new char[256];

        while (line.Length <= ChannelProtocol.MaxLine)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            int newline = Array.IndexOf(buffer, '\n', 0, read);

            if (newline >= 0)
            {
                line.Append(buffer, 0, newline);
                return line.ToString().TrimEnd('\r');
            }

            line.Append(buffer, 0, read);
        }

        return line.Length is > 0 and <= ChannelProtocol.MaxLine ? line.ToString().TrimEnd('\r') : null;
    }

    public void Dispose()
    {
        _stop.Cancel();

        foreach (TaskCompletionSource<bool> answer in _pending.Values)
        {
            answer.TrySetResult(false);
        }

        _pending.Clear();
        _stop.Dispose();
    }
}
