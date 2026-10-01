using System;
using Windows.Security.Credentials;

namespace SpaceNotch_App.Assistant;

/// <summary>
/// La clé d'API de l'utilisateur, gardée dans le coffre d'identifiants de
/// Windows (PasswordVault), jamais dans la configuration ni le journal.
/// </summary>
internal static class AssistantKeys
{
    private const string Resource = "SpaceNotch.Assistant.Claude";
    private const string User = "api-key";

    public static string? Read()
    {
        try
        {
            PasswordCredential credential = new PasswordVault().Retrieve(Resource, User);
            credential.RetrievePassword();
            return string.IsNullOrWhiteSpace(credential.Password) ? null : credential.Password;
        }
        catch (Exception)
        {
            // Absente : le coffre lève plutôt que de rendre null.
            return null;
        }
    }

    public static bool HasKey => Read() is not null;

    public static void Save(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Remove();
        new PasswordVault().Add(new PasswordCredential(Resource, User, key.Trim()));
    }

    public static void Remove()
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(Resource, User));
        }
        catch (Exception)
        {
            // Déjà absente.
        }
    }
}
