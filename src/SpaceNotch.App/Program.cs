using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using SpaceNotch_App.Startup;

namespace SpaceNotch_App;

/// <summary>
/// Point d'entrée du processus, écrit à la main plutôt que généré par XAML
/// (DISABLE_XAML_GENERATED_MAIN) pour une seule raison : les commandes du
/// canal local (<c>--hook</c>, <c>--progress</c>) passent avant l'initialisation
/// de WinUI. Un hook de Claude Code s'exécute à chaque message ; il doit
/// rendre la main sans charger le moindre XAML. Voir ADR-024.
///
/// Le reste reproduit exactement le Main que XAML génère.
/// </summary>
public static class Program
{
#pragma warning disable SYSLIB1054 // La même déclaration que le Main généré par XAML.
    [DllImport("Microsoft.ui.xaml.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void XamlCheckProcessRequirements();
#pragma warning restore SYSLIB1054

    [STAThread]
    private static int Main(string[] args)
    {
        if (ChannelCommand.Matches(args))
        {
            return ChannelCommand.Run(args);
        }

        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(start =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
