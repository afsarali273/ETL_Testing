using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace EtlTesting.SqlServer;

public sealed record SqlCredentials(string UserName, [property: System.Text.Json.Serialization.JsonIgnore] string Password)
{
    public override string ToString() => "SQL credentials [redacted]";
}

public static class WindowsCredentialStore
{
    public const string TargetPrefix = "EtlTestStudio/profile/";

    public static SqlCredentials Read(string target)
    {
        RequireWindows();
        if (!CredRead(target, 1, 0, out var pointer)) throw new InvalidDataException("SQL credentials are not saved for this profile on this computer. Enter a username and password, or choose environment-variable credentials.");
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var password = Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2)) ?? "";
            return new(credential.UserName ?? "", password);
        }
        finally { CredFree(pointer); }
    }

    public static bool TryRead(string target, out SqlCredentials? credentials)
    {
        try { credentials = Read(target); return true; }
        catch (InvalidDataException) { credentials = null; return false; }
    }

    public static void Write(string target, SqlCredentials credentials)
    {
        RequireWindows();
        var bytes = Encoding.Unicode.GetBytes(credentials.Password);
        if (bytes.Length > 2560) throw new InvalidDataException("Password exceeds the Windows Credential Manager size limit.");
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var credential = new Credential { Type = 1, TargetName = target, CredentialBlob = buffer, CredentialBlobSize = (uint)bytes.Length, Persist = 2, UserName = credentials.UserName, Comment = "ETL Test Studio SQL Server connection" };
            if (!CredWrite(ref credential, 0)) throw new InvalidDataException("Windows could not save the SQL credentials. " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        finally { for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(buffer, i, 0); Marshal.FreeHGlobal(buffer); Array.Clear(bytes); }
    }

    public static void Delete(string target)
    {
        RequireWindows();
        if (!CredDelete(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new InvalidDataException("Windows could not remove the saved credential.");
    }

    private static void RequireWindows() { if (!OperatingSystem.IsWindows()) throw new InvalidDataException("This connection uses Windows Credential Manager. On this runner use SQL environment-variable references or integrated authentication."); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags; public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}
