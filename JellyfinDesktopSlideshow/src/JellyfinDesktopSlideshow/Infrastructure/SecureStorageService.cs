using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace JellyfinDesktopSlideshow.Infrastructure;

public sealed class SecureStorageService : ISecureStorageService
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public void SaveSecret(string key, string username, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        var bytes = Encoding.UTF8.GetBytes(secret);
        var blobPtr = Marshal.AllocCoTaskMem(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, blobPtr, bytes.Length);
            var credential = new NativeCredential
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = key,
                Comment = "Jellyfin Desktop Slideshow Access Token",
                LastWritten = default,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = string.IsNullOrWhiteSpace(username) ? "jellyfin-user" : username
            };

            if (!CredWriteW(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ذخیرهٔ توکن در Windows Credential Manager ناموفق بود.");
            }
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.Copy(bytes, 0, blobPtr, bytes.Length);
            Marshal.FreeCoTaskMem(blobPtr);
        }
    }

    public string? ReadSecret(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!CredReadW(key, CredTypeGeneric, 0, out var credPtr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(err, "خواندن توکن از Windows Credential Manager ناموفق بود.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public void DeleteSecret(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!CredDeleteW(key, CredTypeGeneric, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != ErrorNotFound)
            {
                throw new Win32Exception(err, "حذف توکن از Windows Credential Manager ناموفق بود.");
            }
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW([In] ref NativeCredential userCredential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(
        [In] string target,
        [In] uint type,
        [In] uint reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW([In] string target, [In] uint type, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
