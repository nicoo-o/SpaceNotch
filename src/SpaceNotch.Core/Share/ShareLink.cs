using System.Globalization;
using System.Text;

namespace SpaceNotch.Core.Share;

/// <summary>
/// Partage PC → téléphone (F8) : un fichier déposé sur la notch devient un
/// lien local à usage unique — un jeton aléatoire dans l'adresse, valable dix
/// minutes, servi seulement sur le réseau local. Le téléphone le lit par un QR
/// code, sans compte ni câble.
/// </summary>
public sealed class ShareLink
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly object _gate = new();

    public ShareLink(string token, string fileName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Token = token;
        FileName = fileName;
        ExpiresAt = createdAt + Lifetime;
    }

    public string Token { get; }

    public string FileName { get; }

    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Vrai une fois le fichier téléchargé : le lien ne sert qu'une fois.</summary>
    public bool Used { get; private set; }

    /// <summary>Adresse que le QR code porte.</summary>
    public string Url(string host, int port)
        => string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}/{Token}/{Uri.EscapeDataString(FileName)}");

    /// <summary>
    /// La requête (« GET /jeton/nom HTTP/1.1 ») ouvre-t-elle ce partage ?
    /// Le jeton doit correspondre exactement, le lien ne pas avoir servi ni
    /// expiré ; le nom du fichier n'est qu'un confort de lecture.
    /// </summary>
    public bool Accepts(string? requestLine, DateTimeOffset now)
    {
        lock (_gate)
        {
            return AcceptsCore(requestLine, now);
        }
    }

    /// <summary>Réserve atomiquement le lien pour une seule requête valide.</summary>
    public bool TryClaim(string? requestLine, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (Used || !AcceptsCore(requestLine, now))
            {
                return false;
            }

            Used = true;
            return true;
        }
    }

    private bool AcceptsCore(string? requestLine, DateTimeOffset now)
    {
        if (Used || now >= ExpiresAt || string.IsNullOrEmpty(requestLine))
        {
            return false;
        }

        string[] parts = requestLine.Split(' ');

        if (parts.Length < 2 || parts[0] != "GET")
        {
            return false;
        }

        string[] path = parts[1].TrimStart('/').Split('/', 2);
        return path.Length > 0 && FixedTimeEquals(path[0], Token);
    }

    public void MarkUsed()
    {
        lock (_gate)
        {
            Used = true;
        }
    }

    /// <summary>Jeton lisible dans une adresse, tiré de 16 octets aléatoires fournis par l'appelant.</summary>
    public static string TokenFrom(ReadOnlySpan<byte> random)
    {
        if (random.Length < 16)
        {
            throw new ArgumentException("Seize octets aléatoires au moins.", nameof(random));
        }

        return Convert.ToBase64String(random).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>En-têtes de la réponse qui sert le fichier.</summary>
    public static string ResponseHeaders(string fileName, long length)
    {
        string ascii = new(fileName.Select(c => c < 128 && c != '"' && !char.IsControl(c) ? c : '_').ToArray());
        return string.Create(CultureInfo.InvariantCulture, $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {length}\r\nContent-Disposition: attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
    }

    public const string NotFound = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

    private static bool FixedTimeEquals(string a, string b)
        => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
