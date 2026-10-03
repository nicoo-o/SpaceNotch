using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SpaceNotch.Infrastructure.Plugins;

/// <summary>
/// Vérifie la signature Authenticode d'un greffon avec <c>WinVerifyTrust</c> :
/// intégrité du fichier (l'empreinte signée correspond au contenu) et chaîne
/// jusqu'à une racine approuvée par Windows.
///
/// <para>
/// Avant (audit SN-17), seul le certificat était extrait du fichier
/// (<c>X509Certificate.CreateFromSignedFile</c>) : un greffon signé par un
/// certificat auto-signé, ou modifié après signature, était accepté.
/// </para>
/// <para>
/// La révocation n'est pas consultée : elle demanderait un accès réseau à
/// chaque démarrage. Hors de Windows, la réponse est toujours non.
/// </para>
/// </summary>
public static class PluginSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdCacheOnlyUrlRetrieval = 0x00001000;

    /// <summary>Vrai si le fichier porte une signature Authenticode intacte et approuvée.</summary>
    public static bool HasValidSignature(string assemblyPath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(assemblyPath))
        {
            return false;
        }

        return Verify(Path.GetFullPath(assemblyPath)) == 0;
    }

    /// <summary>Code rendu par <c>WinVerifyTrust</c> (0 : signature valide et approuvée).</summary>
    public static int Verify(string fullPath)
    {
        IntPtr pathPtr = Marshal.StringToCoTaskMemUni(fullPath);
        IntPtr fileInfoPtr = IntPtr.Zero;
        IntPtr dataPtr = IntPtr.Zero;
        IntPtr actionPtr = IntPtr.Zero;

        try
        {
            var fileInfo = new WintrustFileInfo
            {
                CbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
                FilePath = pathPtr
            };

            fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WintrustData
            {
                CbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeNone,
                UnionChoice = WtdChoiceFile,
                File = fileInfoPtr,
                StateAction = WtdStateActionVerify,
                ProvFlags = WtdCacheOnlyUrlRetrieval
            };

            dataPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustData>());
            Marshal.StructureToPtr(data, dataPtr, false);

            actionPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(GenericVerifyV2, actionPtr, false);

            int result = WinVerifyTrust(new IntPtr(-1), actionPtr, dataPtr);

            // L'état ouvert par la vérification est toujours refermé.
            data = Marshal.PtrToStructure<WintrustData>(dataPtr);
            data.StateAction = WtdStateActionClose;
            Marshal.StructureToPtr(data, dataPtr, true);
            _ = WinVerifyTrust(new IntPtr(-1), actionPtr, dataPtr);

            return result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return -1;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);

            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPtr);
            }

            if (dataPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(dataPtr);
            }

            if (actionPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(actionPtr);
            }
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, IntPtr pgActionId, IntPtr pWvtData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint CbStruct;
        public IntPtr FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint CbStruct;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
