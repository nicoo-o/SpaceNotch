using System.Text;

namespace SpaceNotch.Core.Setup;

/// <summary>Ce que l'exécutable doit faire à ce lancement.</summary>
public enum SetupMode
{
    /// <summary>Lancement ordinaire : la notch.</summary>
    None = 0,

    /// <summary>L'installeur, avec son interface.</summary>
    Install,

    /// <summary>Le désinstalleur, avec son interface.</summary>
    Uninstall,

    /// <summary>
    /// L'installation pour tous, sans interface, dans un processus élevé. Il ne
    /// fait que ce qui exige les droits d'administrateur ; l'interface reste
    /// dans le processus de l'utilisateur, qui n'est jamais élevé.
    /// </summary>
    InstallWorker,

    /// <summary>La désinstallation pour tous, sans interface, dans un processus élevé.</summary>
    UninstallWorker
}

/// <summary>Pour qui SpaceNotch est installée.</summary>
public enum InstallScope
{
    /// <summary>L'utilisateur courant, dans son profil, sans droits d'administrateur.</summary>
    CurrentUser = 0,

    /// <summary>Tous les utilisateurs, dans Program Files, avec les droits d'administrateur.</summary>
    AllUsers
}

/// <summary>Les choix de l'installeur.</summary>
/// <param name="Scope">Pour qui.</param>
/// <param name="StartWithWindows">Lancer la notch à l'ouverture de session.</param>
/// <param name="DesktopShortcut">Poser un raccourci sur le bureau (le menu Démarrer en a toujours un).</param>
public sealed record InstallOptions(InstallScope Scope, bool StartWithWindows, bool DesktopShortcut)
{
    /// <summary>Les choix proposés d'emblée : pour soi, au démarrage, avec un raccourci.</summary>
    public static InstallOptions Default { get; } = new(InstallScope.CurrentUser, StartWithWindows: true, DesktopShortcut: true);
}

/// <summary>
/// Le mode d'installation lu dans la ligne de commande et le nom du fichier.
///
/// <para>
/// Un seul exécutable sert à tout : <c>SpaceNotch.exe</c> est l'application,
/// <c>SpaceNotch-Setup.exe</c> — le même fichier, renommé — est l'installeur.
/// L'installeur se copie lui-même sous le nom de l'application : rien n'est
/// empaqueté deux fois, et ce qui s'installe est exactement ce qui a été
/// essayé. Voir ADR-022.
/// </para>
/// </summary>
/// <param name="Mode">Ce que le lancement doit faire.</param>
/// <param name="Options">Choix transmis au processus élevé, ou imposés en mode silencieux.</param>
/// <param name="Quiet">Aucune interface : installation ou désinstallation scriptée.</param>
/// <param name="RemoveSettings">À la désinstallation, supprimer aussi réglages et journaux.</param>
public sealed record SetupCommand(SetupMode Mode, InstallOptions Options, bool Quiet = false, bool RemoveSettings = false)
{
    /// <summary>Vrai pour les processus élevés, sans interface, qui font le travail d'administrateur.</summary>
    public bool IsWorker => Mode is SetupMode.InstallWorker or SetupMode.UninstallWorker;

    /// <summary>Lancement ordinaire.</summary>
    public static SetupCommand None { get; } = new(SetupMode.None, InstallOptions.Default);

    /// <summary>
    /// Lit la ligne de commande. Un argument explicite l'emporte sur le nom du
    /// fichier ; un argument inconnu est ignoré, jamais une erreur.
    /// </summary>
    /// <param name="arguments">Arguments, le premier pouvant être l'exécutable lui-même.</param>
    /// <param name="executablePath">Chemin de l'exécutable, pour reconnaître un installeur à son nom.</param>
    public static SetupCommand Parse(IReadOnlyList<string> arguments, string? executablePath)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        SetupMode? mode = null;
        InstallScope scope = InstallOptions.Default.Scope;
        bool startup = InstallOptions.Default.StartWithWindows;
        bool desktop = InstallOptions.Default.DesktopShortcut;
        bool quiet = false;
        bool removeSettings = false;

        foreach (string raw in arguments)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string argument = raw.Trim();
            (string name, string? value) = Split(argument);

            switch (name.ToUpperInvariant())
            {
                case "--INSTALL":
                    mode = SetupMode.Install;
                    break;
                case "--UNINSTALL":
                    mode = SetupMode.Uninstall;
                    break;
                case "--INSTALL-WORKER":
                    mode = SetupMode.InstallWorker;
                    break;
                case "--UNINSTALL-WORKER":
                    mode = SetupMode.UninstallWorker;
                    break;
                case "--QUIET":
                    quiet = true;
                    break;
                case "--REMOVE-SETTINGS":
                    removeSettings = true;
                    break;
                case "--SCOPE":
                    scope = string.Equals(value, "machine", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)
                        ? InstallScope.AllUsers
                        : InstallScope.CurrentUser;
                    break;
                case "--STARTUP":
                    startup = IsOn(value);
                    break;
                case "--DESKTOP":
                    desktop = IsOn(value);
                    break;
            }
        }

        mode ??= ModeFromFileName(executablePath);

        return new SetupCommand(mode.Value, new InstallOptions(scope, startup, desktop), quiet, removeSettings);
    }

    /// <summary>
    /// Un installeur se reconnaît à son nom : <c>SpaceNotch-Setup.exe</c>, et
    /// aussi <c>SpaceNotch-Setup (1).exe</c> quand le navigateur a renommé un
    /// second téléchargement.
    /// </summary>
    public static SetupMode ModeFromFileName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return SetupMode.None;
        }

        string name = FileNameOf(executablePath);

        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return SetupMode.Uninstall;
        }

        return name.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || name.Contains("install", StringComparison.OrdinalIgnoreCase)
            ? SetupMode.Install
            : SetupMode.None;
    }

    /// <summary>Arguments qui redonnent cette commande — pour relancer en processus élevé.</summary>
    public IReadOnlyList<string> ToArguments()
    {
        var arguments = new List<string>();

        switch (Mode)
        {
            case SetupMode.Install:
                arguments.Add("--install");
                break;
            case SetupMode.Uninstall:
                arguments.Add("--uninstall");
                break;
            case SetupMode.InstallWorker:
                arguments.Add("--install-worker");
                break;
            case SetupMode.UninstallWorker:
                arguments.Add("--uninstall-worker");
                break;
        }

        arguments.Add(Options.Scope == InstallScope.AllUsers ? "--scope=machine" : "--scope=user");
        arguments.Add(Options.StartWithWindows ? "--startup=on" : "--startup=off");
        arguments.Add(Options.DesktopShortcut ? "--desktop=on" : "--desktop=off");

        if (Quiet)
        {
            arguments.Add("--quiet");
        }

        if (RemoveSettings)
        {
            arguments.Add("--remove-settings");
        }

        return arguments;
    }

    /// <summary>Ligne de commande Windows, chaque argument cité selon les règles de CommandLineToArgvW.</summary>
    public string ToCommandLine() => JoinArguments(ToArguments());

    /// <summary>Assemble des arguments en une ligne de commande que Windows redécoupera à l'identique.</summary>
    public static string JoinArguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return string.Join(' ', arguments.Select(Quote));
    }

    /// <summary>
    /// Cite un argument : entre guillemets s'il contient un espace, avec les
    /// barres obliques inverses doublées devant un guillemet.
    /// </summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder("\"");
        int backslashes = 0;

        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                builder.Append('\\', backslashes);
            }

            backslashes = 0;
            builder.Append(c);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');

        return builder.ToString();
    }

    private static (string Name, string? Value) Split(string argument)
    {
        int equals = argument.IndexOf('=', StringComparison.Ordinal);

        return equals < 0
            ? (argument, null)
            : (argument[..equals], argument[(equals + 1)..]);
    }

    private static bool IsOn(string? value)
        => value is null
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.Ordinal)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nom de fichier sans dossier ni extension, quel que soit le séparateur.</summary>
    private static string FileNameOf(string path)
    {
        int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        string name = slash >= 0 ? path[(slash + 1)..] : path;
        int dot = name.LastIndexOf('.');

        return dot > 0 ? name[..dot] : name;
    }
}
