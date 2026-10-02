using System;
using System.IO;
using System.Linq;
using System.Text;
using SpaceNotch.Core.Channel;
using SpaceNotch.Platform.Windows.Channel;

namespace SpaceNotch_App.Startup;

/// <summary>
/// Les deux commandes du canal local (ADR-024), traitées avant tout XAML :
/// elles s'exécutent à chaque message d'un agent ou d'un script, et doivent
/// rendre la main en quelques dizaines de millisecondes.
///
/// <list type="bullet">
/// <item><c>SpaceNotch.exe --hook</c> : appelé par les hooks de Claude Code ;
/// lit l'événement sur l'entrée standard. Pour une demande d'autorisation, il
/// attend la réponse de la notch et l'écrit sur la sortie standard.</item>
/// <item><c>SpaceNotch.exe --progress --id build --step 2/4 --label Tests</c> :
/// un script annonce où il en est.</item>
/// <item><c>SpaceNotch.exe --notify --id build --title "Script terminé" --body "42 s"
/// --source build.ps1 --open C:\logs\build.log --done</c> : un script prévient,
/// et propose d'ouvrir son journal.</item>
/// </list>
///
/// <para>
/// Ni l'une ni l'autre ne fait jamais échouer l'appelant : sans notch, sans
/// réponse, ou devant une entrée illisible, elles se taisent et sortent à 0
/// (seuls des arguments <c>--progress</c> invalides sortent à 2).
/// Claude Code reprend alors sa propre question dans le terminal.
/// </para>
/// </summary>
internal static class ChannelCommand
{
    /// <summary>Vrai si les arguments demandent l'une des deux commandes.</summary>
    public static bool Matches(string[] args)
        => args.Any(a => IsFlag(a, "--hook") || IsFlag(a, "--progress") || IsFlag(a, "--notify"));

    public static int Run(string[] args)
    {
        try
        {
            return args.Any(a => IsFlag(a, "--hook")) ? Hook() : Progress(args);
        }
        catch (Exception)
        {
            // Un agent ne doit jamais être bloqué par la notch.
            return 0;
        }
    }

    private static int Hook()
    {
        string input;

        using (var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
        {
            var buffer = new char[256 * 1024];
            int length = reader.ReadBlock(buffer, 0, buffer.Length);
            input = new string(buffer, 0, length);
        }

        HookTranslation translation = ClaudeHook.Translate(input);

        if (translation.Message is not { } message)
        {
            return 0;
        }

        TimeSpan timeout = translation.AwaitsDecision ? TimeSpan.FromSeconds(ClaudeHook.DecisionSeconds) : TimeSpan.FromSeconds(2);
        string? answer = ChannelPipe.SendAsync(message, translation.AwaitsDecision, timeout).GetAwaiter().GetResult();

        if (translation.AwaitsDecision && answer is "allow" or "deny")
        {
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            output.Write(ClaudeHook.Decision(answer == "allow"));
        }

        return 0;
    }

    private static int Progress(string[] args)
    {
        if (ChannelProtocol.FromArguments(args) is not { } message)
        {
            return 2;
        }

        // Notch absente : le script continue, comme s'il n'avait rien dit.
        _ = ChannelPipe.SendAsync(message, waitForAnswer: false, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        return 0;
    }

    private static bool IsFlag(string argument, string flag)
        => string.Equals(argument?.Trim(), flag, StringComparison.OrdinalIgnoreCase);
}
