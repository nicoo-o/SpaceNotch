# Décompte des descripteurs par type et de la mémoire privée engagée d'un processus (n° 24).
# Usage : powershell -File tools/memoire/descripteurs.ps1 -ProcId <pid>   (lecture seule, sans outil Sysinternals)
param([int]$ProcId)
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public static class H{
 [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int c,IntPtr b,int l,out int r);
 [DllImport("ntdll.dll")] static extern int NtQueryObject(IntPtr h,int c,IntPtr b,int l,out int r);
 [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a,bool i,int p);
 [DllImport("kernel32.dll")] static extern bool DuplicateHandle(IntPtr sp,IntPtr sh,IntPtr tp,out IntPtr th,uint a,bool i,uint o);
 [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [DllImport("kernel32.dll")] static extern IntPtr VirtualQueryEx(IntPtr p,IntPtr a,out MBI m,IntPtr l);
 [StructLayout(LayoutKind.Sequential)] public struct MBI{public IntPtr Base,AllocBase;public uint AllocProt;public ushort pad1,pad2;public IntPtr Size;public uint State,Prot,Type;public uint pad3;}
 public static string Handles(int pid){
  IntPtr proc=OpenProcess(0x40,false,pid);int len=1<<24,r;IntPtr buf=Marshal.AllocHGlobal(len);
  while(NtQuerySystemInformation(64,buf,len,out r)==unchecked((int)0xC0000004)){Marshal.FreeHGlobal(buf);len*=2;buf=Marshal.AllocHGlobal(len);}
  long n=Marshal.ReadInt64(buf);var cnt=new Dictionary<int,int>();var names=new Dictionary<int,string>();
  for(long i=0;i<n;i++){IntPtr e=buf+16+(int)(i*40);long owner=Marshal.ReadInt64(e,8);if(owner!=pid)continue;
   int ti=Marshal.ReadInt16(e,30);IntPtr hv=Marshal.ReadIntPtr(e,16);
   cnt[ti]=cnt.ContainsKey(ti)?cnt[ti]+1:1;
   if(!names.ContainsKey(ti)){IntPtr d;if(DuplicateHandle(proc,hv,GetCurrentProcess(),out d,0,false,2)){int l2=4096,r2;IntPtr b2=Marshal.AllocHGlobal(l2);
     if(NtQueryObject(d,2,b2,l2,out r2)==0){names[ti]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(b2,8));}CloseHandle(d);Marshal.FreeHGlobal(b2);}}}
  var s="";foreach(var k in cnt)s+=(names.ContainsKey(k.Key)?names[k.Key]:"type"+k.Key)+"="+k.Value+"\n";return s;}
 public static string Memory(int pid){
  IntPtr proc=OpenProcess(0x410,false,pid);long priv=0,img=0,map=0;var big=new List<string>();IntPtr a=IntPtr.Zero;MBI m;
  while(VirtualQueryEx(proc,a,out m,(IntPtr)Marshal.SizeOf(typeof(MBI)))!=IntPtr.Zero){long sz=m.Size.ToInt64();
   if(m.State==0x1000){if(m.Type==0x20000){priv+=sz;if(sz>=4<<20)big.Add("priv "+sz/1048576+"Mo @"+m.Base.ToString("x"));}else if(m.Type==0x1000000)img+=sz;else map+=sz;}
   a=(IntPtr)(m.Base.ToInt64()+sz);if(a.ToInt64()<0)break;}
  return "engage prive="+priv/1048576+"Mo image="+img/1048576+"Mo mapped="+map/1048576+"Mo\n"+string.Join("\n",big);}
}
"@
"--- descripteurs par type"; [H]::Handles($ProcId) -split "`n" | Where-Object {$_} | Sort-Object { [int](($_ -split '=')[1]) } -Descending | Select-Object -First 12
"--- memoire"; [H]::Memory($ProcId)
