using System;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

static class HardwareTests
{
    static void Check(bool success, string label) { if (!success) throw new Exception(label); }
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--cpu-sensor-helper") { CpuTemperatureBridge.Run(args); return; }
        Check(HardwareSampler.CpuPercent(100, 200, 150, 300) == 50, "CPU busy delta");
        Check(HardwareSampler.CpuPercent(100, 200, 200, 300) == 0, "CPU idle");
        Check(HardwareSampler.CpuPercent(100, 200, 100, 300) == 100, "CPU busy");
        Check(!HardwareSampler.CpuPercent(100, 200, 90, 300).HasValue, "Counter reset");
        Check(!HardwareSampler.CpuPercent(100, 200, 100, 200).HasValue, "Zero interval");
        Check(!HardwareSampler.CpuPercent(100, 200, 300, 300).HasValue, "Invalid delta");
        Check(!HardwareSampler.Temperature(0).HasValue && !HardwareSampler.Temperature(double.NaN).HasValue && !HardwareSampler.Temperature(999).HasValue, "Invalid temperature");
        Check(HardwareSampler.Temperature(65) == 65, "Temperature");
        Console.WriteLine("8 hardware checks passed");
        using (var bridge = new CpuTemperatureBridge()) {
            int pid = bridge.Start(Process.GetCurrentProcess().MainModule.FileName, false);
            using (var child = Process.GetProcessById(pid)) {
                Thread.Sleep(1500); Check(bridge.Running, "Helper startup");
                bridge.SetActive(false); bridge.Dispose();
                Check(child.WaitForExit(5000), "Helper must exit after stop");
            }
        }
        Console.WriteLine("2 helper lifecycle checks passed (without elevation)");
        if (args.Length == 0) return;
        using (var sampler = new HardwareSampler()) using (var process = Process.GetCurrentProcess()) {
            sampler.Read(false); var clock = Stopwatch.StartNew(); TimeSpan cpu = process.TotalProcessorTime;
            HardwareSample last = null;
            for (int i = 0; i < 6; i++) { Thread.Sleep(5000); last = sampler.Read(false); }
            process.Refresh();
            Console.WriteLine(new JavaScriptSerializer().Serialize(new { sample = last, elapsedSeconds = clock.Elapsed.TotalSeconds,
                cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds, cpuPercentOfMachine = 100 * (process.TotalProcessorTime - cpu).TotalSeconds / clock.Elapsed.TotalSeconds / Environment.ProcessorCount,
                workingSetMb = process.WorkingSet64 / 1048576.0, privateMb = process.PrivateMemorySize64 / 1048576.0 }));
            sampler.Pause(); var resumed = sampler.Read(false); Check(!resumed.CpuLoad.HasValue, "Resume must re-prime CPU");
            var battery = sampler.Read(true); Check(!battery.GpuTemperature.HasValue && !battery.GpuLoad.HasValue, "Battery clears GPU readings");
            Console.WriteLine("Pause/resume and battery checks passed");
        }
    }
}
