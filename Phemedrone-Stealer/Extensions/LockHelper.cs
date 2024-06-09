using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Phemedrone.Extensions
{
    public class LockHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        const int CCH_RM_MAX_APP_NAME = 255;
        const int CCH_RM_MAX_SVC_NAME = 63;

        enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices,
            string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
        static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            ref uint lpdwRebootReasons);

        public static IEnumerable<Process> GetLockingProcesses(string path)
        {
            var key = Guid.NewGuid().ToString();
            var processes = new List<Process>();

            var ret = RmStartSession(out var handle, 0, key);
            if (ret != 0) return [];

            try
            {
                const int errorMoreData = 234;
                uint pnProcInfo = 0,
                    lpdwRebootReasons = 0;

                var resources = new[] { path };

                ret = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);

                if (ret != 0) return [];

                ret = RmGetList(handle, out var pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);

                if (ret == errorMoreData)
                {
                    var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;

                    ret = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                    if (ret == 0)
                    {
                        processes = new List<Process>((int)pnProcInfo);

                        for (var i = 0; i < pnProcInfo; i++)
                        {
                            try
                            {
                                processes.Add(Process.GetProcessById(processInfo[i].Process.dwProcessId));
                            }
                            catch (ArgumentException)
                            {
                            }
                        }
                    }
                    else return [];
                }
                else if (ret != 0)
                    return [];
            }
            finally
            {
                RmEndSession(handle);
            }

            return processes;
        }

        public static Process GetOwnerProcess(int childProcessId)
        {
            Process ownerProcess = null;
            var query = $"SELECT * FROM Win32_Process WHERE ProcessId = {childProcessId}";
            var searcher = new ManagementObjectSearcher("root\\CIMV2", query);

            foreach (var o in searcher.Get())
            {
                var process = (ManagementObject)o;
                var parentId = (uint)process["ParentProcessId"];

                try
                {
                    ownerProcess = Process.GetProcessById((int)parentId);
                }
                catch
                {
                    // ignored
                }
            }

            return ownerProcess;
        }
        
        // Read locked files
        private static Dictionary<string, string> BuildDeviceMap()
        {
            const string networkDevicePrefix = @"\Device\LanmanRedirector\";

            var logicalDrives = Environment.GetLogicalDrives();
            var localDeviceMap = new Dictionary<string, string>(logicalDrives.Length);
            var lpTargetPath = new StringBuilder(260);
            foreach (var drive in logicalDrives)
            {
                var lpDeviceName = drive.Substring(0, 2);
                Interop.Kernel32.QueryDosDevice(lpDeviceName, lpTargetPath, 260);
                localDeviceMap.Add(NormalizeDeviceName(lpTargetPath.ToString()), lpDeviceName);
            }
            localDeviceMap.Add(networkDevicePrefix.Substring(0, networkDevicePrefix.Length - 1), "\\");
            return localDeviceMap;
        }

        private static string NormalizeDeviceName(string deviceName)
        {
            const string networkDevicePrefix = @"\Device\LanmanRedirector\";

            if (string.Compare(deviceName, 0, networkDevicePrefix, 0, networkDevicePrefix.Length,
                    StringComparison.InvariantCulture) != 0) return deviceName;
            var shareName = deviceName.Substring(deviceName.IndexOf('\\', networkDevicePrefix.Length) + 1);
            return string.Concat(networkDevicePrefix, shareName);
        }

        private static Dictionary<int, string> ConvertDevicePathsToDosPaths(Dictionary<int, string> devicePaths)
        {
            var dosPaths = new Dictionary<int, string>();

            foreach (var devicePath in devicePaths)
            {
                var deviceMap = BuildDeviceMap();
                var i = devicePath.Value.Length;
                while (i > 0 && (i = devicePath.Value.LastIndexOf('\\', i - 1)) != -1)
                {
                    if (deviceMap.TryGetValue(devicePath.Value.Substring(0, i), out var drive))
                    {
                        dosPaths.Add(devicePath.Key, string.Concat(drive, devicePath.Value.Substring(i)));
                    }
                }
            }

            return dosPaths;
        }

        private static Dictionary<int, string> GetHandleNames(int targetPid)
        {
            var fileHandles = new Dictionary<int, string>();

            var length = 0x10000;
            var ptr = IntPtr.Zero;

            try
            {
                while (true)
                {
                    ptr = Marshal.AllocHGlobal(length);
                    
                    var result = Interop.Ntdll.NtQuerySystemInformation(Interop.SYSTEM_INFORMATION_CLASS.SystemHandleInformation, ptr, length, out var wantedLength);
                    if (result == Interop.NtStatus.STATUS_INFO_LENGTH_MISMATCH)
                    {
                        length = Math.Max(length, wantedLength);
                        Marshal.FreeHGlobal(ptr);
                        ptr = IntPtr.Zero;
                    }
                    else if (result == Interop.NtStatus.STATUS_SUCCESS)
                        break;
                    else
                        break;
                }

                var offset = ptr.ToInt64();
                offset += IntPtr.Size;
                var size = Marshal.SizeOf(typeof(Interop.SystemHandleInformation));

                var handleCount = IntPtr.Size == 4 ? Marshal.ReadInt32(ptr) : (int)Marshal.ReadInt64(ptr);

                
                var processHandle = Interop.Kernel32.OpenProcess(Interop.ProcessAccessFlags.DuplicateHandle, true, (uint)targetPid);
                var currentProcessHandle = Interop.Kernel32.GetCurrentProcess();

                for (var i = 0; i < handleCount; i++)
                {
                    if (Marshal.ReadInt32((IntPtr)offset) == targetPid)
                    {
                        var info = (Interop.SystemHandleInformation)Marshal.PtrToStructure(new IntPtr(offset), typeof(Interop.SystemHandleInformation));

                        var dummy = 0;
                        var success = Interop.Kernel32.DuplicateHandle(processHandle, new IntPtr(info.HandleValue), currentProcessHandle, out var duplicatedHandle, 0, false, Interop.DuplicateOptions.DUPLICATE_SAME_ACCESS);

                        if (Interop.Kernel32.GetFileType(duplicatedHandle) == Interop.FileType.Disk)
                        {
                            if (success)
                            {
                                const int length2 = 0x200;
                                var buffer = Marshal.AllocHGlobal(length2);

                                 
                                var status = Interop.Ntdll.NtQueryObject(duplicatedHandle, Interop.OBJECT_INFORMATION_CLASS.ObjectNameInformation, buffer, length2, out dummy);

                                if (status == Interop.NtStatus.STATUS_SUCCESS)
                                {
                                    var temp = (Interop.ObjectNameInformation)Marshal.PtrToStructure(buffer, typeof(Interop.ObjectNameInformation));
                                    if (!string.IsNullOrEmpty(temp.Name.ToString()) && !string.IsNullOrEmpty(temp.Name.ToString().Trim()))
                                    {
                                        fileHandles.Add(info.HandleValue, temp.Name.ToString().Trim());
                                    }
                                }
                                Marshal.FreeHGlobal(buffer);
                            }
                        }
                        Interop.Kernel32.CloseHandle(duplicatedHandle);
                    }

                    offset += size;
                }

                Interop.Kernel32.CloseHandle(processHandle);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }

            return ConvertDevicePathsToDosPaths(fileHandles);
        }

        public static Interop.ProcessFileHandle FindFileHandle(string targetFile, Process candidateProcesses)
        {
            var processes = new List<Process>();
            
            processes.AddRange(candidateProcesses == null
                ? Process.GetProcesses().Where(p => p.HandleCount != 0)
                : new[] { candidateProcesses });

            foreach (var process in processes)
            {
                var processHandle = GetHandleNames(process.Id);

                foreach (var handle in processHandle.Where(handle => handle.Value.EndsWith(targetFile, StringComparison.CurrentCultureIgnoreCase)))
                {
                    return new Interop.ProcessFileHandle(process.ProcessName, process.Id, handle.Value, handle.Key);
                }
            }

            return new Interop.ProcessFileHandle();
        }

        public static byte[] ReadLockedFile(Interop.ProcessFileHandle fileHandle)
        {

            byte[] fileBytes = null;

            var processHandle = Interop.Kernel32.OpenProcess(Interop.ProcessAccessFlags.DuplicateHandle, true, (uint)fileHandle.ProcessID);
            var currentProcessHandle = Interop.Kernel32.GetCurrentProcess();

            var success = Interop.Kernel32.DuplicateHandle(processHandle, new IntPtr(fileHandle.FileHandleID), currentProcessHandle, out var duplicatedHandle, 0, false, Interop.DuplicateOptions.DUPLICATE_SAME_ACCESS);

            if (success)
            {
                Interop.Kernel32.GetFileSizeEx(duplicatedHandle, out var fileSize);

                var mappedPtr = Interop.Kernel32.CreateFileMapping(duplicatedHandle, IntPtr.Zero, Interop.FileMapProtection.PageReadonly, 0, 0, null);
                var mappedViewPtr = Interop.Kernel32.MapViewOfFile(mappedPtr, Interop.FileMapAccess.FileMapRead, 0, 0, 0);

                fileBytes = new byte[fileSize];
                Marshal.Copy(mappedViewPtr, fileBytes, 0, (int)fileSize);

                Interop.Kernel32.UnmapViewOfFile(mappedViewPtr);
                Interop.Kernel32.CloseHandle(duplicatedHandle);
            }

            Interop.Kernel32.CloseHandle(processHandle);

            return fileBytes;
        }
    }
}