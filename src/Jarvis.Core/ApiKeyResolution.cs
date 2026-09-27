using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.Core;

public static class ApiKeyResolver
{
    public static string Resolve(string directValue, string credentialTarget, string envVar)
    {
        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue.Trim();
        }

        if (WindowsCredentialStore.TryReadGenericSecret(credentialTarget, out var credentialValue))
        {
            return credentialValue;
        }

        if (string.IsNullOrWhiteSpace(envVar))
        {
            return string.Empty;
        }

        return Environment.GetEnvironmentVariable(envVar)?.Trim() ?? string.Empty;
    }
}

internal static class WindowsCredentialStore
{
    private const int CredTypeGeneric = 1;

    public static bool TryReadGenericSecret(string target, out string secret)
    {
        secret = string.Empty;

        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (!CredReadW(target.Trim(), CredTypeGeneric, 0, out var credentialPointer))
        {
            // Not found (ERROR_NOT_FOUND) and every other failure both mean "no usable secret".
            return false;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);

            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
            {
                return false;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            secret = Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
            return !string.IsNullOrWhiteSpace(secret);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public static bool TryWriteGenericSecret(string target, string secret)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE: this user, this PC, kept across restarts
                UserName = Environment.UserName
            };

            return CredWriteW(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPtr);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
