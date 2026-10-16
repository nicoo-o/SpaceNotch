using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Platform.Windows.Shell;

/// <summary>
/// Veille sur la présence de la session : une application occupe-t-elle l'écran ?
///
/// <para>
/// <b>Pourquoi le shell et pas une mesure de fenêtre.</b> Comparer le rectangle
/// de la fenêtre au premier plan avec celui de l'écran est l'approche évidente, et
/// elle est fausse : une fenêtre peut couvrir l'écran tout en laissant la barre
/// des tâches, une autre peut être en plein écran sans être au premier plan, et un
/// jeu en exclusif ne se décrit pas du tout comme une fenêtre de cette taille.
/// <c>SHQueryUserNotificationState</c> est la réponse documentée à cette question
/// précise — c'est celle que PowerToys utilise pour la même raison — et elle est
/// la seule à distinguer un jeu Direct3D en exclusif d'une fenêtre simplement
/// agrandie.
/// </para>
///
/// <para>
/// <b>Sur événement, jamais par scrutation.</b> L'état est relu quand une fenêtre
/// prend le premier plan, ou quand l'une est réduite : ce sont les deux seuls
/// moments où il peut changer sans que l'application soit au courant. Entre ces
/// moments, rien ne tourne — sauf pendant que l'Island est retirée : la fin d'un
/// plein écran n'émet pas toujours d'événement, et l'hôte relit alors l'état
/// toutes les 2 s (n° 11).
/// </para>
///
/// <para>
/// <b>Ce que ce veilleur ne voit pas.</b> Une bascule plein écran qui conserve
/// la même fenêtre au premier plan — certaines vidéos, certains lecteurs — peut
/// passer sans événement. C'est pourquoi <see cref="Recheck"/> est public : l'hôte
/// le rappelle quand le shell lui signale un changement d'environnement, ce qui
/// rattrape ces cas en pratique. La prétention inverse serait une garantie qu'on
/// ne peut pas tenir.
/// </para>
/// </summary>
public sealed class FullscreenPresenceWatcher : IDisposable
{
    /// <summary>Une fenêtre vient de devenir celle du premier plan.</summary>
    private const uint EventSystemForeground = 0x0003;

    /// <summary>Une fenêtre vient d'être réduite, ou de cesser de l'être.</summary>
    private const uint EventSystemMinimizeStart = 0x0016;

    private const uint EventSystemMinimizeEnd = 0x0017;

    /// <summary>Une fenêtre vient d'être déplacée, agrandie ou restaurée.</summary>
    private const uint EventObjectLocationChange = 0x800B;

    /// <summary>L'événement concerne la fenêtre elle-même, et non l'un de ses objets.</summary>
    private const int ObjIdWindow = 0;

    /// <summary>Le rappel est appelé sur le fil qui a posé le crochet, sans injection.</summary>
    private const uint WinEventOutOfContext = 0x0000;

    private readonly WinEventDelegate _callback;
    private readonly IntPtr[] _hooks = new IntPtr[4];

    private bool _shouldHide;
    private bool _disposed;

    public FullscreenPresenceWatcher()
    {
        // Le délégué est conservé : un rappel natif dont le délégué managé est
        // collecté fait tomber le processus à la première notification.
        _callback = OnWinEvent;

        _hooks[0] = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        _hooks[1] = SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeStart, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        _hooks[2] = SetWinEventHook(EventSystemMinimizeEnd, EventSystemMinimizeEnd, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        WatchForegroundLocation(GetForegroundWindow());

        _shouldHide = Query();
    }

    /// <summary>
    /// Rectangle de l'Island, en pixels physiques, fourni par l'hôte.
    ///
    /// <para>
    /// C'est ce rectangle qui permet de répondre à la question que le shell ne
    /// sait pas trancher : « une fenêtre recouvre-t-elle l'Island ? ». Un
    /// navigateur agrandi occupe le haut de l'écran sans occuper la session :
    /// <c>SHQueryUserNotificationState</c> répond « notifications acceptées », et
    /// l'Island resterait donc posée sur une barre de titre qu'elle n'a pas à
    /// masquer. La comparaison des deux rectangles est la seule mesure qui
    /// décrive ce cas.
    /// </para>
    ///
    /// <para>
    /// Fourni par l'hôte, et non calculé ici : la fenêtre de l'Island est la
    /// seule à savoir où elle se trouve, à mesure qu'elle s'anime.
    /// </para>
    /// </summary>
    public Func<(int X, int Y, int Width, int Height)>? IslandBounds { get; set; }

    /// <summary>
    /// Signalé lorsque l'Island doit se retirer, ou reparaître. La valeur est
    /// l'état demandé ; l'événement n'est levé que sur un changement réel, ce qui
    /// évite de remasquer une fenêtre déjà masquée à chaque clic dans la barre des
    /// tâches.
    /// </summary>
    public event EventHandler<bool>? Changed;

    /// <summary>Vrai lorsque la session ne permet pas d'afficher par-dessus.</summary>
    public bool ShouldHide => _shouldHide;

    /// <summary>
    /// Pourquoi l'Island se retire, pour le journal (n° 11 : elle disparaissait
    /// « sans explication ») ; vide quand rien ne l'y oblige.
    /// </summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// Relit l'état et lève <see cref="Changed"/> s'il a changé. Appelé par l'hôte
    /// lorsqu'un message du shell peut avoir modifié la situation.
    /// </summary>
    public void Recheck() => Apply(Query());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IntPtr hook in _hooks)
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWinEvent(hook);
            }
        }

        GC.SuppressFinalize(this);
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int idObject,
        int idChild,
        uint thread,
        uint time)
    {
        if (_disposed)
        {
            return;
        }

        // Le déplacement d'une fenêtre est signalé pour *toutes* les fenêtres du
        // bureau, et il est de loin l'événement le plus fréquent. Seul celui de la
        // fenêtre au premier plan peut changer la réponse : le filtre est ici, et
        // non dans la mesure, sinon la moindre fenêtre déplacée ferait relire
        // l'état de la session.
        if (eventType == EventObjectLocationChange
            && (idObject != ObjIdWindow || window != GetForegroundWindow()))
        {
            return;
        }

        // Nouvelle fenêtre au premier plan : on n'écoute plus que ses
        // déplacements à elle.
        if (eventType == EventSystemForeground)
        {
            WatchForegroundLocation(window);
        }

        // Le rappel est appelé pour chaque fenêtre concernée, y compris celles qui
        // n'ont aucun rapport : la seule chose à faire ici est de relire l'état de
        // la session, et lui seul décide.
        Apply(Query());
    }

    /// <summary>
    /// Écoute les déplacements des seules fenêtres du processus au premier plan.
    /// Un crochet sur tout le bureau réveillait le fil d'interface à chaque
    /// mouvement de la souris (le curseur est un « objet » qui se déplace) —
    /// de 125 à 1 000 fois par seconde.
    /// </summary>
    private void WatchForegroundLocation(IntPtr foreground)
    {
        _ = GetWindowThreadProcessId(foreground, out uint processId);

        if (foreground != IntPtr.Zero && processId == _locationProcess && _hooks[3] != IntPtr.Zero)
        {
            return;
        }

        if (_hooks[3] != IntPtr.Zero)
        {
            UnhookWinEvent(_hooks[3]);
            _hooks[3] = IntPtr.Zero;
        }

        _locationProcess = processId;

        if (foreground == IntPtr.Zero || processId == 0 || processId == (uint)Environment.ProcessId)
        {
            return;
        }

        _hooks[3] = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _callback, processId, 0, WinEventOutOfContext);
    }

    private uint _locationProcess;

    private void Apply(bool shouldHide)
    {
        if (shouldHide == _shouldHide)
        {
            return;
        }

        _shouldHide = shouldHide;
        Changed?.Invoke(this, shouldHide);
    }

    /// <summary>
    /// Interroge le shell.
    ///
    /// L'appel échoue sur les sessions où l'interface n'est pas disponible — un
    /// service, une session déconnectée. L'échec est traité comme « libre », et
    /// non comme « occupé » : ne pas pouvoir répondre ne doit pas faire
    /// disparaître l'Island, ce qui serait le pire des replis.
    /// </summary>
    private bool Query()
    {
        QueryUserNotificationState? state = SessionState();

        switch (state)
        {
            // La session est verrouillée, ou l'écran est éteint : rien à peindre.
            case QueryUserNotificationState.NotPresent:
                Reason = "session verrouillée ou écran éteint";
                return true;

            // Jeu en Direct3D exclusif : la surface n'est plus gérée par le
            // compositeur, une fenêtre toujours au-dessus n'y apparaîtrait pas.
            case QueryUserNotificationState.RunningD3DFullScreen:
                Reason = "jeu en plein écran exclusif";
                return true;

            case QueryUserNotificationState.PresentationMode:
                Reason = "mode présentation";
                return true;

            // « Une application occupe l'écran » : un navigateur en plein écran,
            // un jeu sans bordure… mais aussi une surcouche « toujours devant »
            // que les clics traversent (n° 11). Le shell ne dit pas laquelle : on
            // cherche une vraie fenêtre plein écran sur l'écran de la notch.
            case QueryUserNotificationState.Busy:
                return FullscreenWindowOnIslandMonitor();

            default:
                return ForegroundCoversIsland();
        }
    }

    /// <summary>
    /// Le moniteur de l'Island, ou <c>null</c> si l'hôte ne le donne pas encore
    /// (fenêtre pas encore placée) ou si Windows ne sait pas le décrire.
    /// </summary>
    private PixelRect? IslandMonitor()
    {
        Func<(int X, int Y, int Width, int Height)>? bounds = IslandBounds;

        if (bounds is null)
        {
            return null;
        }

        (int x, int y, int width, int height) = bounds();

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var island = new WindowRect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        IntPtr monitor = MonitorFromRect(ref island, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        return new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
    }

    /// <summary>
    /// Le shell dit « occupé » : une fenêtre couvre-t-elle vraiment l'écran de la
    /// notch ? Les fenêtres de premier niveau sont parcourues de haut en bas, et
    /// la première opaque qui couvre l'écran décide (<see cref="FullscreenRule"/>).
    /// Sans moniteur connu, on garde l'ancienne réponse : se retirer.
    ///
    /// <para>
    /// Une surcouche permanente garde le shell « occupé », et les déplacements de
    /// la fenêtre au premier plan arrivent par dizaines par seconde : la réponse
    /// est gardée 250 ms, et le nom des processus est mémorisé (relecture de #87).
    /// </para>
    /// </summary>
    private bool FullscreenWindowOnIslandMonitor()
    {
        if (IslandMonitor() is not { } monitor)
        {
            Reason = "une application occupe l'écran (écran de la notch inconnu)";
            return true;
        }

        long now = Environment.TickCount64;

        if (_busyAnswer is { } recent && now - _busyAt < BusyAnswerLifetimeMs && recent.Monitor == monitor)
        {
            Reason = recent.Reason;
            return recent.Hide;
        }

        string? found = null;
        uint own = (uint)Environment.ProcessId;
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcess);

        EnumWindows(
            (window, parameter) =>
            {
                try
                {
                    if (!GetWindowRect(window, out WindowRect rect))
                    {
                        return true;
                    }

                    var box = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);

                    // Le moins cher d'abord : la plupart des fenêtres ne couvrent pas l'écran.
                    if (!FullscreenRule.Covers(box, monitor) || !IsWindowVisible(window))
                    {
                        return true;
                    }

                    _ = GetWindowThreadProcessId(window, out uint processId);

                    // Fenêtre détruite pendant le parcours : ses lectures échouent sans bruit.
                    if (processId == 0 || IsShellWindow(window))
                    {
                        return true;
                    }

                    uint style = (uint)NativeMethods.GetWindowLongPtr(window, NativeConstants.GWL_EXSTYLE).ToInt64();
                    bool cloaked = DwmGetWindowAttribute(window, NativeConstants.DWMWA_CLOAKED, out int cloak, sizeof(int)) == 0 && cloak != 0;

                    CoveringWindow verdict = FullscreenRule.Classify(
                        box,
                        monitor,
                        style,
                        minimized: IsIconic(window),
                        maximized: IsZoomed(window),
                        cloaked,
                        shell: false,
                        inFront: processId == foregroundProcess || (style & (uint)NativeConstants.WS_EX_TOPMOST) != 0);

                    switch (verdict)
                    {
                        case CoveringWindow.Ignore:
                            return true;

                        case CoveringWindow.Occludes:
                            return false;
                    }

                    // Nos propres fenêtres (l'installeur) ne comptent pas : vérifié
                    // en dernier, c'est la partie la plus coûteuse.
                    if (processId == own || IsSpaceNotchProcess(processId))
                    {
                        return true;
                    }

                    found = $"fenêtre plein écran : {ProcessName(processId)} ({ClassName(window)})";
                    return false;
                }
                catch (Exception)
                {
                    // Une exception ne doit pas traverser user32 : on s'arrête là.
                    return false;
                }
            },
            IntPtr.Zero);

        Reason = found ?? string.Empty;
        _busyAnswer = (monitor, found is not null, Reason);
        _busyAt = now;
        return found is not null;
    }

    private const long BusyAnswerLifetimeMs = 250;

    private (PixelRect Monitor, bool Hide, string Reason)? _busyAnswer;
    private long _busyAt;

    /// <summary>
    /// Vrai lorsqu'une autre application occupe <em>tout</em> l'écran de l'Island.
    ///
    /// L'Island ne se retire que devant un vrai plein écran : une vidéo, un jeu,
    /// une présentation — une fenêtre qui couvre le moniteur entier, barre des
    /// tâches comprise. Une fenêtre simplement agrandie laisse la barre des
    /// tâches visible et ne compte pas : se retirer devant elle faisait
    /// disparaître la notch presque en permanence, puisque la plupart des
    /// fenêtres sont agrandies. Le bureau lui-même (Progman, WorkerW) et la
    /// barre des tâches ne comptent jamais, ni l'installeur de SpaceNotch.
    /// </summary>
    private bool ForegroundCoversIsland()
    {
        Reason = string.Empty;

        if (IslandMonitor() is not { } monitor)
        {
            return false;
        }

        IntPtr foreground = GetForegroundWindow();

        if (foreground == IntPtr.Zero || IsIconic(foreground) || IsShellWindow(foreground))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out uint processId);

        if (processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        if (!GetWindowRect(foreground, out WindowRect rect))
        {
            return false;
        }

        // Plein écran : la fenêtre couvre tout le moniteur de l'Island, sans être
        // simplement agrandie (barre des tâches masquée automatiquement, n° 11).
        bool covers = FullscreenRule.IsFullscreen(new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom), monitor, IsZoomed(foreground));

        // Nos propres fenêtres (l'installeur, un autre processus) ne comptent
        // pas. Vérifié en dernier, et mémorisé : interroger un processus est la
        // partie la plus coûteuse de la mesure.
        if (!covers || IsSpaceNotchProcess(processId))
        {
            return false;
        }

        Reason = $"fenêtre au premier plan en plein écran : {ProcessName(processId)} ({ClassName(foreground)})";
        return true;
    }

    /// <summary>
    /// SpaceNotch ou non, par processus. Un cache à une entrée alternait entre
    /// l'Explorateur, la surcouche et les fenêtres agrandies, et ne servait
    /// presque jamais (relecture de #87).
    /// </summary>
    private readonly Dictionary<uint, bool> _ourProcesses = [];

    private static string ProcessName(uint processId)
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return processId.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            return processId.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static string ClassName(IntPtr window)
    {
        var name = new char[64];
        int length = GetClassName(window, name, name.Length);
        return length > 0 ? new string(name, 0, length) : "?";
    }

    /// <summary>Le bureau et la barre des tâches : jamais « une application en plein écran ».</summary>
    private static bool IsShellWindow(IntPtr window)
    {
        var name = new char[64];
        int length = GetClassName(window, name, name.Length);

        if (length <= 0)
        {
            return false;
        }

        string cls = new(name, 0, length);

        return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    /// <summary>L'installeur (SpaceNotch-Setup) est un autre processus, mais pas un autre produit.</summary>
    private bool IsSpaceNotchProcess(uint processId)
    {
        if (_ourProcesses.TryGetValue(processId, out bool ours))
        {
            return ours;
        }

        // Les identifiants de processus se réutilisent : un cache borné, vidé
        // d'un coup, suffit.
        if (_ourProcesses.Count > 64)
        {
            _ourProcesses.Clear();
        }

        ours = ReadIsSpaceNotch(processId);
        _ourProcesses[processId] = ours;
        return ours;
    }

    private static bool ReadIsSpaceNotch(uint processId)
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName.StartsWith("SpaceNotch", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// L'état de la session selon le shell ; <c>null</c> si l'appel échoue (service,
    /// session déconnectée) : ne pas savoir ne doit pas retirer l'Island.
    /// </summary>
    private static QueryUserNotificationState? SessionState()
        => SHQueryUserNotificationState(out QueryUserNotificationState state) == 0 ? state : null;

    /// <summary>États rapportés par <c>SHQueryUserNotificationState</c>.</summary>
    private enum QueryUserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7
    }

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int idObject,
        int idChild,
        uint thread,
        uint time);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHQueryUserNotificationState(out QueryUserNotificationState state);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect rect);

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref WindowRect rect, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public WindowRect Monitor;
        public WindowRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
