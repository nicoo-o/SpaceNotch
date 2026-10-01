using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Share;

namespace SpaceNotch.Platform.Windows.Share;

/// <summary>
/// Partage PC → téléphone (F8) : un petit serveur HTTP à usage unique. Il
/// écoute sur le réseau local, sert le fichier une fois à qui présente le
/// jeton, puis s'arrête — ou s'arrête à l'expiration. Aucune autre adresse
/// n'est servie, aucun dossier n'est exposé. Au premier partage, le pare-feu
/// de Windows demande l'autorisation : c'est la confirmation voulue.
/// </summary>
public sealed class ShareServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    /// <summary>Le fichier a été téléchargé.</summary>
    public event Action? Served;

    /// <summary>Le partage s'est arrêté (servi, expiré ou annulé).</summary>
    public event Action? Stopped;

    public int Port { get; private set; }

    /// <summary>Adresse IPv4 de la machine sur le réseau local, ou null sans réseau.</summary>
    public static string? LocalAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a))
                .Select(a => a.ToString())
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Commence à servir <paramref name="path"/> sous <paramref name="link"/>.</summary>
    public void Start(ShareLink link, string path, string host)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        Stop();

        if (!IPAddress.TryParse(host, out IPAddress? address) || !IsPrivate(address))
        {
            throw new ArgumentException("Le partage exige une adresse IPv4 privée.", nameof(host));
        }

        _stop = new CancellationTokenSource();
        _listener = new TcpListener(address, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        CancellationToken token = _stop.Token;
        TimeSpan left = link.ExpiresAt - DateTimeOffset.UtcNow;
        _stop.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.Zero);
        _ = Task.Run(() => ServeAsync(link, path, token), token);
    }

    public void Stop()
    {
        try
        {
            _stop?.Cancel();
            _listener?.Stop();
        }
        catch (Exception)
        {
            // Déjà arrêté.
        }

        _listener = null;
        _stop?.Dispose();
        _stop = null;
    }

    public void Dispose() => Stop();

    private async Task ServeAsync(ShareLink link, string path, CancellationToken token)
    {
        TcpListener? listener = _listener;

        try
        {
            while (!token.IsCancellationRequested && listener is not null)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                client.ReceiveTimeout = 5000;
                using NetworkStream stream = client.GetStream();

                string requestLine = await ReadLineAsync(stream, token).ConfigureAwait(false);

                if (!File.Exists(path) || !link.TryClaim(requestLine, DateTimeOffset.UtcNow))
                {
                    byte[] notFound = Encoding.ASCII.GetBytes(ShareLink.NotFound);
                    await stream.WriteAsync(notFound, token).ConfigureAwait(false);
                    continue;
                }

                link.MarkUsed();
                await using FileStream file = File.OpenRead(path);
                byte[] headers = Encoding.ASCII.GetBytes(ShareLink.ResponseHeaders(link.FileName, file.Length));
                await stream.WriteAsync(headers, token).ConfigureAwait(false);
                await file.CopyToAsync(stream, token).ConfigureAwait(false);
                Served?.Invoke();
                break;
            }
        }
        catch (Exception)
        {
            // Annulé, expiré ou connexion coupée : le partage s'arrête.
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch (Exception)
            {
                // Déjà arrêté.
            }

            Stopped?.Invoke();
        }
    }

    /// <summary>Lit la ligne de requête (jusqu'à 2 Ko) ; le reste des en-têtes est ignoré.</summary>
    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[2048];
        int length = 0;

        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), token).ConfigureAwait(false);

            if (read <= 0)
            {
                break;
            }

            length += read;
            int newline = Array.IndexOf(buffer, (byte)'\n', 0, length);

            if (newline >= 0)
            {
                return Encoding.ASCII.GetString(buffer, 0, newline).TrimEnd('\r');
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, length);
    }

    private static bool IsPrivate(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }
}
