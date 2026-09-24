using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Display;

/// <summary>
/// État de luminosité d'un écran interne.
/// </summary>
/// <param name="Percent">Luminosité courante, de 0 à 100.</param>
public readonly record struct BrightnessInfo(int Percent);

/// <summary>
/// Luminosité des écrans internes, lue et suivie de façon événementielle.
///
/// Windows n'expose aucune notification de changement de luminosité : il faut
/// donc choisir entre interroger périodiquement la valeur — ce que le projet
/// s'interdit — ou écouter l'intention de l'utilisateur. Le service retient la
/// seconde voie : un crochet clavier de bas niveau capte les touches de
/// luminosité, y compris lorsque le pilote les traite lui-même. La valeur est
/// alors lue une seule fois, à la demande, jamais en boucle.
///
/// Limite assumée et documentée : une luminosité modifiée par le panneau de
/// configuration du fabricant, sans passer par le clavier, ne produit aucun
/// événement. Le service ne l'invente pas — il ne fait que refléter ce qu'il peut
/// observer sans scrutation.
/// </summary>
public sealed partial class BrightnessService : IDisposable
{
    private const int WhKeyboardLl = 13;

    /// <summary>Touche « augmenter la luminosité » du clavier étendu.</summary>
    private const int VkBrightnessUp = 0xAF;

    /// <summary>Touche « diminuer la luminosité » du clavier étendu.</summary>
    private const int VkBrightnessDown = 0xAE;

    private static readonly IntPtr PrimaryMonitor = IntPtr.Zero;

    private readonly LowLevelKeyboardProc _hookProc;

    private IntPtr _hookHandle = IntPtr.Zero;
    private bool _disposed;

    public BrightnessService()
    {
        // Le délégué est conservé dans un champ : sans référence vivante, le
        // ramasse-miettes le collecterait et Windows appellerait une adresse morte.
        _hookProc = OnKeyboardMessage;
    }

    /// <summary>Signalé lorsque l'utilisateur a demandé un changement de luminosité.</summary>
    public event EventHandler<BrightnessInfo>? BrightnessChanged;

    /// <summary>
    /// Vrai lorsque le crochet est installé et que l'écran expose une luminosité
    /// réglable. Faux sur un poste de bureau à écran externe, où la fonctionnalité
    /// n'a pas de sens.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Installe le crochet clavier. Idempotent.</summary>
    public void Start()
    {
        if (_disposed || _hookHandle != IntPtr.Zero)
        {
            return;
        }

        if (!TryRead(out _))
        {
            // Écran sans luminosité réglable : inutile d'installer le crochet.
            return;
        }

        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, IntPtr.Zero, 0);

        IsAvailable = _hookHandle != IntPtr.Zero;
    }

    /// <summary>Retire le crochet. Strictement symétrique de <see cref="Start"/>.</summary>
    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        IsAvailable = false;
    }

    /// <summary>
    /// Lit la luminosité courante. Retourne <c>false</c> lorsque l'écran ne
    /// l'expose pas — un poste de bureau, un écran externe, une machine virtuelle.
    /// </summary>
    public static bool TryRead(out BrightnessInfo info)
    {
        info = default;

        if (!GetMonitorBrightness(PrimaryMonitor, out _, out uint current, out _))
        {
            return false;
        }

        info = new BrightnessInfo((int)current);
        return true;
    }

    /// <summary>Applique une luminosité, bornée à la plage acceptée par l'écran.</summary>
    public static bool TrySet(int percent)
    {
        if (!GetMonitorBrightness(PrimaryMonitor, out uint minimum, out _, out uint maximum))
        {
            return false;
        }

        uint clamped = (uint)Math.Clamp(percent, (int)minimum, (int)maximum);
        return SetMonitorBrightness(PrimaryMonitor, clamped);
    }

    private IntPtr OnKeyboardMessage(int code, IntPtr wParam, IntPtr lParam)
    {
        const int WmKeyUp = 0x0101;

        if (code >= 0 && wParam.ToInt64() == WmKeyUp && TryRead(out BrightnessInfo info))
        {
            int virtualKey = Marshal.ReadInt32(lParam);

            if (virtualKey is VkBrightnessUp or VkBrightnessDown)
            {
                // La lecture précède la diffusion : à l'instant où le crochet
                // s'exécute, le pilote n'a pas encore appliqué la nouvelle valeur.
                // Le HUD est donc publié avec la valeur observée juste avant, ce
                // qui est exactement ce que l'utilisateur attend de voir.
                BrightnessChanged?.Invoke(this, info);
            }
        }

        return CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMonitorBrightness(IntPtr monitor, uint brightness);
}
