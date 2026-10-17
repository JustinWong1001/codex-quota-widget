using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

sealed class HardwareSample
{
    public double? CpuLoad, CpuTemperature, MemoryLoad, MemoryUsedGb, MemoryTotalGb;
    public double? GpuLoad, GpuTemperature, GpuMemoryUsedGb, GpuMemoryTotalGb;
    public string GpuName = "NVIDIA GPU";
    public bool OnBattery;
    public DateTime Time = DateTime.UtcNow;
}

sealed class CpuTemperatureReader : IDisposable
{
    ManagementObject sensor;
    bool discovered;
    public double? Read()
    {
        try {
            if (!discovered) {
                discovered = true;
                using (var search = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM LENOVO_GAMEZONE_DATA")) {
                    search.Options.Timeout = TimeSpan.FromSeconds(2);
                    using (var results = search.Get()) foreach (ManagementObject item in results) { sensor = item; break; }
                }
            }
            if (sensor == null) return null;
            using (var result = sensor.InvokeMethod("GetCPUTemp", null, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(2) }))
                return HardwareSampler.Temperature(Convert.ToDouble(result["Data"]));
        } catch { Dispose(); return null; }
    }
    public void Dispose() { if (sensor != null) { sensor.Dispose(); sensor = null; } }
}

// A user-started, read-only helper for firmware that requires elevation. It
// exchanges only a temperature and timestamp, never account data or commands.
sealed class CpuTemperatureBridge : IDisposable
{
    MemoryMappedFile memory;
    MemoryMappedViewAccessor view;
    EventWaitHandle stop;
    Process process;
    public bool Running { get { try { return process != null && !process.HasExited; } catch { return false; } } }
    public int Start(string executable, bool elevate = true)
    {
        Dispose(); string id = Guid.NewGuid().ToString("N"), name = "Local\\CodexQuotaCpu_" + id;
        var sid = WindowsIdentity.GetCurrent().User;
        var mapSecurity = new MemoryMappedFileSecurity();
        mapSecurity.AddAccessRule(new AccessRule<MemoryMappedFileRights>(sid, MemoryMappedFileRights.FullControl, AccessControlType.Allow));
        var eventSecurity = new EventWaitHandleSecurity();
        eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(sid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        // The administrator token must also be able to open IPC when Windows uses another admin account.
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        mapSecurity.AddAccessRule(new AccessRule<MemoryMappedFileRights>(admins, MemoryMappedFileRights.FullControl, AccessControlType.Allow));
        eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(admins, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        bool created;
        try {
            memory = MemoryMappedFile.CreateNew(name, 32, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, mapSecurity, HandleInheritability.None);
            view = memory.CreateViewAccessor(); view.Write(16, 1);
            stop = new EventWaitHandle(false, EventResetMode.ManualReset, name + "_stop", out created, eventSecurity);
            process = Process.Start(new ProcessStartInfo(executable, "--cpu-sensor-helper " + Process.GetCurrentProcess().Id + " " + id) {
                UseShellExecute = elevate, Verb = elevate ? "runas" : "", CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
            return process.Id;
        } catch { Dispose(); throw; }
    }
    public void SetActive(bool active) { if (view != null) view.Write(16, active ? 1 : 0); }
    public double? Read()
    {
        if (!Running || view == null) return null;
        long before = view.ReadInt64(8); double value = view.ReadDouble(0); long after = view.ReadInt64(8);
        if (before != after || after <= 0 || DateTime.UtcNow.Ticks - after > TimeSpan.FromSeconds(30).Ticks) return null;
        return HardwareSampler.Temperature(value);
    }
    public void Dispose()
    {
        if (stop != null) { stop.Set(); stop.Dispose(); stop = null; }
        if (view != null) { view.Dispose(); view = null; }
        if (memory != null) { memory.Dispose(); memory = null; }
        if (process != null) { process.Dispose(); process = null; }
    }
    public static void Run(string[] args)
    {
        int pid; Guid id;
        if (args.Length != 3 || !int.TryParse(args[1], out pid) || !Guid.TryParseExact(args[2], "N", out id)) return;
        try {
            using (var parent = Process.GetProcessById(pid)) {
                if (!string.Equals(parent.MainModule.FileName, Process.GetCurrentProcess().MainModule.FileName, StringComparison.OrdinalIgnoreCase)) return;
                string name = "Local\\CodexQuotaCpu_" + id.ToString("N");
                using (var map = MemoryMappedFile.OpenExisting(name))
                using (var data = map.CreateViewAccessor())
                using (var end = EventWaitHandle.OpenExisting(name + "_stop"))
                using (var reader = new CpuTemperatureReader()) {
                    while (!parent.HasExited && !end.WaitOne(0)) {
                        if (data.ReadInt32(16) != 0) {
                            double? value = reader.Read();
                            data.Write(8, 0L); data.Write(0, value ?? double.NaN); data.Write(8, DateTime.UtcNow.Ticks);
                        }
                        if (end.WaitOne(10000)) break;
                    }
                }
            }
        } catch { /* No logging, startup changes, driver installation or UI in the helper. */ }
    }
}

sealed class HardwareSampler : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length, Load; public ulong Total, Available, PageTotal, PageAvailable, VirtualTotal, VirtualAvailable, Extended; }
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    readonly CpuTemperatureReader cpu = new CpuTemperatureReader();
    readonly NvidiaSensors gpu = new NvidiaSensors();
    ulong lastIdle, lastTotal;
    bool primed;
    DateTime nextSensors = DateTime.MinValue;
    double? cpuTemperature;
    HardwareSample sensors;
    public static double? Temperature(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0 && value <= 125 ? (double?)value : null; }
    public static double? CpuPercent(ulong previousIdle, ulong previousTotal, ulong idle, ulong total)
    {
        if (total <= previousTotal || idle < previousIdle || idle - previousIdle > total - previousTotal) return null;
        return 100.0 * (1.0 - (double)(idle - previousIdle) / (total - previousTotal));
    }
    public void Pause() { primed = false; nextSensors = DateTime.MinValue; gpu.Dispose(); }
    public HardwareSample Read(bool battery)
    {
        var sample = new HardwareSample { OnBattery = battery };
        ulong idle, kernel, user;
        if (GetSystemTimes(out idle, out kernel, out user)) {
            ulong total = kernel + user;
            if (primed) sample.CpuLoad = CpuPercent(lastIdle, lastTotal, idle, total);
            lastIdle = idle; lastTotal = total; primed = true;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
        if (GlobalMemoryStatusEx(ref memory) && memory.Total > 0) {
            sample.MemoryLoad = 100.0 * (memory.Total - memory.Available) / memory.Total;
            sample.MemoryUsedGb = (memory.Total - memory.Available) / 1073741824.0;
            sample.MemoryTotalGb = memory.Total / 1073741824.0;
        }
        if (battery) { gpu.Dispose(); sensors = null; }
        if (DateTime.UtcNow >= nextSensors) {
            nextSensors = DateTime.UtcNow.AddSeconds(battery ? 30 : 10);
            cpuTemperature = cpu.Read();
            if (!battery) sensors = gpu.Read();
        }
        sample.CpuTemperature = cpuTemperature;
        if (!battery && sensors != null && DateTime.UtcNow - sensors.Time < TimeSpan.FromSeconds(30)) {
            sample.GpuName = sensors.GpuName; sample.GpuLoad = sensors.GpuLoad; sample.GpuTemperature = sensors.GpuTemperature;
            sample.GpuMemoryUsedGb = sensors.GpuMemoryUsedGb; sample.GpuMemoryTotalGb = sensors.GpuMemoryTotalGb;
        }
        return sample;
    }
    public void Dispose() { cpu.Dispose(); gpu.Dispose(); }
}

sealed class NvidiaSensors : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Init();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Handle(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Temperature(IntPtr device, uint sensor, out uint value);
    [StructLayout(LayoutKind.Sequential)] struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] struct Memory { public ulong Total, Free, Used; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Util(IntPtr device, out Utilization value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Mem(IntPtr device, out Memory value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Name(IntPtr device, StringBuilder value, uint length);
    IntPtr module, device;
    Init shutdown;
    Temperature temperature;
    Util utilization;
    Mem memory;
    string name = "NVIDIA GPU";
    DateTime retryAt;
    T Function<T>(string symbol) where T : class
    {
        IntPtr address = GetProcAddress(module, symbol);
        if (address == IntPtr.Zero) throw new EntryPointNotFoundException(symbol);
        return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
    }
    bool Open()
    {
        if (device != IntPtr.Zero) return true;
        if (DateTime.UtcNow < retryAt) return false;
        retryAt = DateTime.UtcNow.AddMinutes(2);
        try {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
            if (!File.Exists(path)) return false;
            module = LoadLibraryEx(path, IntPtr.Zero, 0x800); // Driver DLL from System32 only.
            if (module == IntPtr.Zero) return false;
            if (Function<Init>("nvmlInit_v2")() != 0) { Dispose(); return false; }
            shutdown = Function<Init>("nvmlShutdown");
            if (Function<Handle>("nvmlDeviceGetHandleByIndex_v2")(0, out device) != 0) { Dispose(); return false; }
            temperature = Function<Temperature>("nvmlDeviceGetTemperature"); utilization = Function<Util>("nvmlDeviceGetUtilizationRates"); memory = Function<Mem>("nvmlDeviceGetMemoryInfo");
            var buffer = new StringBuilder(128); if (Function<Name>("nvmlDeviceGetName")(device, buffer, 128) == 0) name = buffer.ToString();
            return true;
        } catch { Dispose(); return false; }
    }
    public HardwareSample Read()
    {
        var result = new HardwareSample();
        if (!Open()) return result;
        result.GpuName = name;
        uint temp; Utilization use; Memory mem;
        if (temperature(device, 0, out temp) == 0) result.GpuTemperature = HardwareSampler.Temperature(temp);
        if (utilization(device, out use) == 0 && use.Gpu <= 100) result.GpuLoad = use.Gpu;
        if (memory(device, out mem) == 0 && mem.Total > 0 && mem.Used <= mem.Total) { result.GpuMemoryUsedGb = mem.Used / 1073741824.0; result.GpuMemoryTotalGb = mem.Total / 1073741824.0; }
        if (!result.GpuTemperature.HasValue && !result.GpuLoad.HasValue) { Dispose(); retryAt = DateTime.UtcNow.AddMinutes(2); }
        return result;
    }
    public void Dispose()
    {
        if (shutdown != null) { shutdown(); shutdown = null; }
        if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
        device = IntPtr.Zero;
    }
}
