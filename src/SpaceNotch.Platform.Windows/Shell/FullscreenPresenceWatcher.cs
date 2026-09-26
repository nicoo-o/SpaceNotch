using System;
using System.Runtime.InteropServices;

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
/// moments, rien ne tourne.
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
        _hooks[3] = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);

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

        // Le rappel est appelé pour chaque fenêtre concernée, y compris celles qui
        // n'ont aucun rapport : la seule chose à faire ici est de relire l'état de
        // la session, et lui seul décide.
        Apply(Query());
    }

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
    private bool Query() => SessionIsOccupied() || ForegroundCoversIsland();

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
        Func<(int X, int Y, int Width, int Height)>? bounds = IslandBounds;

        if (bounds is null)
        {
            return false;
        }

        (int x, int y, int width, int height) = bounds();

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        IntPtr foreground = GetForegroundWindow();

        if (foreground == IntPtr.Zero || IsIconic(foreground) || IsShellWindow(foreground))
        {
            return false;
        }

        // Nos propres fenêtres ne se recouvrent pas elles-mêmes : ni l'Island, ni
        // sa surface atmosphérique, ni la fenêtre de réglages ne comptent comme
        // « une autre application ».
        _ = GetWindowThreadProcessId(foreground, out uint processId);

        if (processId == (uint)Environment.ProcessId || IsSpaceNotchProcess(processId))
        {
            return false;
        }

        if (!GetWindowRect(foreground, out WindowRect rect))
        {
            return false;
        }

        var island = new WindowRect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        IntPtr monitor = MonitorFromRect(ref island, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        // Plein écran : la fenêtre couvre tout le moniteur de l'Island.
        return rect.Left <= info.Monitor.Left
            && rect.Top <= info.Monitor.Top
            && rect.Right >= info.Monitor.Right
            && rect.Bottom >= info.Monitor.Bottom;
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
    private static bool IsSpaceNotchProcess(uint processId)
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

    /// <summary>Interroge le shell sur l'occupation de la session.</summary>
    private static bool SessionIsOccupied()
    {
        if (SHQueryUserNotificationState(out QueryUserNotificationState state) != 0)
        {
            return false;
        }

        return state switch
        {
            // La session est verrouillée, ou l'écran est éteint : rien à peindre.
            QueryUserNotificationState.NotPresent => true,

            // Une application occupe l'écran. Le shell rapporte cet état aussi bien
            // pour une présentation que pour un jeu qui n'est pas en Direct3D
            // exclusif — un navigateur en plein écran, par exemple.
            QueryUserNotificationState.Busy => true,

            // Jeu en Direct3D exclusif : la surface n'est plus gérée par le
            // compositeur, une fenêtre toujours au-dessus n'y apparaîtrait pas.
            QueryUserNotificationState.RunningD3DFullScreen => true,

            QueryUserNotificationState.PresentationMode => true,

            _ => false
        };
    }

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
