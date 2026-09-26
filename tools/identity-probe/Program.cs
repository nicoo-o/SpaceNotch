using System.Runtime.InteropServices;

uint length = 0;
int result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
Console.WriteLine(result == 15700 ? "IDENTITE: absente" : $"IDENTITE: presente (code {result}, longueur {length})");
return 0;

[DllImport("kernel32.dll")]
static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
