using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SpaceNotch.Infrastructure.Plugins;

public static class PluginSignatureVerifier
{
    public static bool HasValidSignature(string assemblyPath)
    {
        try
        {
            using X509Certificate certificate = CreateFromSignedFile(assemblyPath);
            return certificate is not null;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

#pragma warning disable SYSLIB0057
    private static X509Certificate CreateFromSignedFile(string path)
        => X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
}