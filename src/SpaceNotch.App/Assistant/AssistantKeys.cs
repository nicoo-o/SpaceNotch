using System;
using Windows.Security.Credentials;

namespace SpaceNotch_App.Assistant;

/// <summary>
/// Les secrets de l'utilisateur — clé d'API, jetons de connexion — gardés
/// dans le coffre d'identifiants de Windows (PasswordVault), jamais dans la
/// configuration ni le journal.
/// </summary>
internal static class SecretVault
{
    private const string User = "spacenotch";

    public static string? Read(string resource, string user = User)
    {
        try
        {
            PasswordCredential credential = new PasswordVault().Retrieve(resource, user);
            credential.RetrievePassword();
            return string.IsNullOrWhiteSpace(credential.Password) ? null : credential.Password;
        }
        catch (Exception)
        {
            // Absent : le coffre lève plutôt que de rendre null.
            return null;
        }
    }

    public static void Save(string resource, string value, string user = User)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Remove(resource, user);
        new PasswordVault().Add(new PasswordCredential(resource, user, value.Trim()));
    }

    public static void Remove(string resource, string user = User)
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(resource, user));
        }
        catch (Exception)
        {
            // Déjà absent.
        }
    }
}

/// <summary>La clé d'API Claude de l'utilisateur (ADR-025).</summary>
internal static class AssistantKeys
{
    private const string Resource = "SpaceNotch.Assistant.Claude";
    private const string User = "api-key";

    public static string? Read() => SecretVault.Read(Resource, User);

    public static bool HasKey => Read() is not null;

    public static void Save(string key) => SecretVault.Save(Resource, key, User);

    public static void Remove() => SecretVault.Remove(Resource, User);
}
