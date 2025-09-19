using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

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
                    // try again with per-session persistence (safer for non-admin users)
                    Trace.TraceWarning("CredWrite(LocalMachine) failed, attempting Session persist.");
                    credential.Persist = NativeMethods.CredentialPersist.Session;
                    try
                    {
                        result = NativeMethods.CredWrite(ref credential, 0);
                    }
                    catch { }

                    if (!result)
                    {
                        Trace.TraceWarning("CredWrite(Session) also failed. Credential not stored.");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"CredentialService.Save exception: {ex.Message}");
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

            // Try reading the exact stored target first
            if (TryReadCredential(target, out var cred)) return cred;

            // As a fallback, try using trimmed host/username forms (normalize whitespace)
            var altTarget = MakeTarget((host ?? string.Empty).Trim(), (username ?? string.Empty).Trim());
            if (altTarget != target && TryReadCredential(altTarget, out cred)) return cred;

            // Final fallback: enumerate credentials and look for ones with our prefix and matching host or username
            try
            {
                if (NativeMethods.CredEnumerate("FTP-Tool:*", 0, out var count, out var pCredentials))
                {
                    try
                    {
                        for (int i = 0; i < (int)count; i++)
                        {
                            var credPtr = Marshal.ReadIntPtr(pCredentials, i * IntPtr.Size);
                            if (credPtr == IntPtr.Zero) continue;
                            var native = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credPtr);
                            var targetName = Marshal.PtrToStringUni(native.TargetName) ?? string.Empty;
                            if (!targetName.StartsWith("FTP-Tool:", StringComparison.OrdinalIgnoreCase)) continue;

                            // parse targetName format FTP-Tool:host:username
                            var parts = targetName.Split(new[] { ':' }, 3);
                            if (parts.Length >= 2)
                            {
                                var storedHost = parts.Length >= 2 ? parts[1] : string.Empty;
                                var storedUser = parts.Length >= 3 ? parts[2] : string.Empty;
                                if (!string.IsNullOrEmpty(host) && string.Equals(storedHost, host, StringComparison.OrdinalIgnoreCase) ||
                                    !string.IsNullOrEmpty(username) && string.Equals(storedUser, username, StringComparison.OrdinalIgnoreCase) ||
                                    (string.IsNullOrEmpty(host) && string.IsNullOrEmpty(username)))
                                {
                                    string user = string.Empty;
                                    if (native.UserName != IntPtr.Zero)
                                        user = Marshal.PtrToStringUni(native.UserName) ?? string.Empty;

                                    string pass = string.Empty;
                                    if (native.CredentialBlob != IntPtr.Zero && native.CredentialBlobSize > 0)
                                    {
                                        pass = Marshal.PtrToStringUni(native.CredentialBlob) ?? string.Empty;
                                    }

                                    return (user, pass);
                                }
                            }
                        }
                    }
                    finally
                    {
                        NativeMethods.CredFree(pCredentials);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"CredentialService.Load enumerate fallback error: {ex.Message}");
            }

            return null;
        }

        private bool TryReadCredential(string target, out (string Username, string Password)? result)
        {
            result = null;
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
                        pass = Marshal.PtrToStringUni(native.CredentialBlob) ?? string.Empty;
                    }

                    result = (user, pass);
                    return true;
                }
                catch (Exception ex)
                {
                    Trace.TraceError($"CredentialService.TryReadCredential error: {ex.Message}");
                }
                finally
                {
                    NativeMethods.CredFree(credPtr);
                }
            }
            return false;
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

        // New: list saved credentials (host, username) for the UI
        public (string Host, string Username)[] ListSavedCredentials()
        {
            var result = new List<(string Host, string Username)>();
            try
            {
                if (NativeMethods.CredEnumerate("FTP-Tool:*", 0, out var count, out var pCredentials))
                {
                    try
                    {
                        for (int i = 0; i < (int)count; i++)
                        {
                            var credPtr = Marshal.ReadIntPtr(pCredentials, i * IntPtr.Size);
                            if (credPtr == IntPtr.Zero) continue;
                            var native = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credPtr);
                            var targetName = Marshal.PtrToStringUni(native.TargetName) ?? string.Empty;
                            if (!targetName.StartsWith("FTP-Tool:", StringComparison.OrdinalIgnoreCase)) continue;

                            var parts = targetName.Split(new[] { ':' }, 3);
                            var storedHost = parts.Length >= 2 ? parts[1] : string.Empty;
                            var storedUser = parts.Length >= 3 ? parts[2] : string.Empty;

                            result.Add((storedHost, storedUser));
                        }
                    }
                    finally
                    {
                        NativeMethods.CredFree(pCredentials);
                    }
                }
            }
            catch { }

            return result.ToArray();
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

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredEnumerate(string filter, int flags, out uint count, out IntPtr pCredentials);
        }
    }
}
