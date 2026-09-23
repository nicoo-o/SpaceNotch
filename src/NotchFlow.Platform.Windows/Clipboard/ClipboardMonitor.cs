using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NotchFlow.Platform.Windows.Clipboard;

/// <summary>
/// Écouteur natif de presse-papier Windows (AddClipboardFormatListener).
/// 100% événementiel via les notifications système de presse-papier.
/// </summary>
public sealed partial class ClipboardMonitor : IDisposable
{
    private IntPtr _hWnd;
    private bool _isListening;

    public event Action? ClipboardUpdated;

    /// <summary>Vrai tant que les notifications de presse-papier sont reçues.</summary>
    public bool IsListening => _isListening;

    /// <summary>
    /// S'inscrit auprès de Windows pour recevoir <c>WM_CLIPBOARDUPDATE</c>.
    ///
    /// L'échec est signalé plutôt qu'avalé : sans cela, une fonctionnalité
    /// activée se croirait à l'écoute alors qu'aucune notification ne lui
    /// parviendrait — exactement le genre de panne silencieuse que le projet
    /// s'interdit. L'appelant est le socle de fonctionnalité, qui marque alors la
    /// fonctionnalité en échec et le journalise.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// La fenêtre est nulle, ou l'inscription a été refusée par Windows.
    /// </exception>
    public void Start(IntPtr hWnd)
    {
        if (_isListening)
        {
            return;
        }

        if (hWnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Le presse-papier ne peut pas être surveillé sans fenêtre cible.");
        }

        _hWnd = hWnd;

        if (!AddClipboardFormatListener(_hWnd))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "AddClipboardFormatListener a refusé l'inscription.");
        }

        _isListening = true;
    }

    public void OnClipboardMessageReceived()
    {
        ClipboardUpdated?.Invoke();
    }

    public void Stop()
    {
        if (_isListening && _hWnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hWnd);
            _isListening = false;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AddClipboardFormatListener(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveClipboardFormatListener(IntPtr hwnd);
}
