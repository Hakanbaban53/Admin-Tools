using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FTP_Tool.Services
{
    // Simple wrapper for Windows Credential Manager using Cred* APIs.
    public class CredentialService
    {
        private static string MakeTarget(string host, string username)
        {
            host ??= string.Empty;
            username ??= string.Empty;
            return $"FTP-Tool:{host}:{username}";
        }

        public void Save(string host, string username, string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                Delete(host, username);
                return;
            }

            var target = MakeTarget(host, username);

            // Marshal strings to unmanaged memory
            IntPtr targetPtr = IntPtr.Zero;
            IntPtr userPtr = IntPtr.Zero;
            IntPtr blobPtr = IntPtr.Zero;
            try
            {
                targetPtr = Marshal.StringToCoTaskMemUni(target);
                userPtr = Marshal.StringToCoTaskMemUni(username ?? string.Empty);

                // CredentialBlob should be a byte buffer. We'll allocate Unicode bytes including trailing null.
                var blobSize = (uint)((password.Length + 1) * 2);
                blobPtr = Marshal.StringToCoTaskMemUni(password);

                var credential = new NativeMethods.CREDENTIAL
                {
                    Flags = 0,
                    Type = NativeMethods.CredentialType.Generic,
                    TargetName = targetPtr,
                    Comment = IntPtr.Zero,
                    LastWritten = default,
                    CredentialBlobSize = blobSize,
                    CredentialBlob = blobPtr,
                    Persist = NativeMethods.CredentialPersist.LocalMachine,
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    TargetAlias = IntPtr.Zero,
                    UserName = userPtr
                };

                var result = NativeMethods.CredWrite(ref credential, 0);
                if (!result)
                {
                    // ignore failures
                }
            }
            finally
            {
                if (targetPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(targetPtr);
                if (userPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(userPtr);
                if (blobPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(blobPtr);
            }
        }

        public (string Username, string Password)? Load(string host, string username)
        {
            var target = MakeTarget(host, username);
            if (NativeMethods.CredRead(target, NativeMethods.CredentialType.Generic, 0, out var credPtr))
            {
                try
                {
                    var native = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credPtr);

                    string user = string.Empty;
                    if (native.UserName != IntPtr.Zero)
                        user = Marshal.PtrToStringUni(native.UserName) ?? string.Empty;

                    string pass = string.Empty;
                    if (native.CredentialBlob != IntPtr.Zero && native.CredentialBlobSize > 0)
                    {
                        // CredentialBlob may not be null-terminated, but we stored it as Unicode string with terminator.
                        pass = Marshal.PtrToStringUni(native.CredentialBlob) ?? string.Empty;
                    }

                    return (user, pass);
                }
                finally
                {
                    NativeMethods.CredFree(credPtr);
                }
            }
            return null;
        }

        public void Delete(string host, string username)
        {
            var target = MakeTarget(host, username);
            try
            {
                NativeMethods.CredDelete(target, NativeMethods.CredentialType.Generic, 0);
            }
            catch { }
        }

        private static class NativeMethods
        {
            public enum CredentialPersist : uint
            {
                Session = 1,
                LocalMachine = 2,
                Enterprise = 3
            }

            public enum CredentialType : uint
            {
                Generic = 1,
                DomainPassword = 2,
                DomainCertificate = 3,
                DomainVisiblePassword = 4,
                GenericCertificate = 5,
                DomainExtended = 6,
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct CREDENTIAL
            {
                public uint Flags;
                public CredentialType Type;
                public IntPtr TargetName;
                public IntPtr Comment;
                public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
                public uint CredentialBlobSize;
                public IntPtr CredentialBlob;
                public CredentialPersist Persist;
                public uint AttributeCount;
                public IntPtr Attributes;
                public IntPtr TargetAlias;
                public IntPtr UserName;
            }

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredWrite([In] ref CREDENTIAL Credential, [In] uint Flags);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredRead(string target, CredentialType type, int flags, out IntPtr credential);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredDelete(string target, CredentialType type, int flags);

            [DllImport("advapi32.dll", SetLastError = false)]
            public static extern void CredFree([In] IntPtr buffer);
        }
    }
}
