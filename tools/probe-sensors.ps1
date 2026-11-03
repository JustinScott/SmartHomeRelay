# Smart Home Relay - sensor probe (read-only, run as a NORMAL user, not admin)
# Checks which temperature / power sources this PC exposes without elevation.
# Usage:  powershell -ExecutionPolicy Bypass -File .\tools\probe-sensors.ps1

$ErrorActionPreference = 'Continue'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
"Elevated: $isAdmin   (should be False for a fair test)"
"CPU: " + (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
""

"=== 1. GPU via Windows (D3DKMT adapter perf data - what Task Manager uses) ==="
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Kmt {
    [StructLayout(LayoutKind.Sequential)] public struct AdapterInfo { public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint NumSources; public int Precise; }
    [StructLayout(LayoutKind.Sequential)] public struct Enum2 { public uint NumAdapters; public IntPtr pAdapters; }
    [StructLayout(LayoutKind.Sequential)] public struct Query { public uint hAdapter; public int Type; public IntPtr pData; public uint Size; }
    [StructLayout(LayoutKind.Sequential)] public struct PerfData {
        public uint PhysicalAdapterIndex; public ulong MemoryFrequency; public ulong MaxMemoryFrequency; public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth; public ulong PCIEBandwidth; public uint FanRPM; public uint Power; public uint Temperature; public byte PowerStateOverride; }
    [DllImport("gdi32.dll")] public static extern int D3DKMTEnumAdapters2(ref Enum2 e);
    [DllImport("gdi32.dll")] public static extern int D3DKMTQueryAdapterInfo(ref Query q);
    public static string Probe() {
        var sb = new System.Text.StringBuilder();
        var e = new Enum2();
        int rc = D3DKMTEnumAdapters2(ref e);
        if (rc != 0) return "EnumAdapters2 count failed: 0x" + rc.ToString("X");
        int sz = Marshal.SizeOf(typeof(AdapterInfo));
        e.pAdapters = Marshal.AllocHGlobal(sz * (int)e.NumAdapters);
        try {
            rc = D3DKMTEnumAdapters2(ref e);
            if (rc != 0) return "EnumAdapters2 failed: 0x" + rc.ToString("X");
            for (int i = 0; i < e.NumAdapters; i++) {
                var a = (AdapterInfo)Marshal.PtrToStructure(e.pAdapters + i * sz, typeof(AdapterInfo));
                var buf = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PerfData)));
                try {
                    var p = new PerfData();
                    Marshal.StructureToPtr(p, buf, false);
                    var q = new Query { hAdapter = a.hAdapter, Type = 62, pData = buf, Size = (uint)Marshal.SizeOf(typeof(PerfData)) };
                    rc = D3DKMTQueryAdapterInfo(ref q);
                    if (rc != 0) { sb.AppendLine("  adapter " + i + ": no perf data (0x" + rc.ToString("X") + ")"); continue; }
                    p = (PerfData)Marshal.PtrToStructure(buf, typeof(PerfData));
                    sb.AppendLine(string.Format("  adapter {0} (LUID {1:X}): temp {2:0.0} C, power {3:0.0} % of max, fan {4} RPM",
                        i, a.LuidLow, p.Temperature / 10.0, p.Power / 10.0, p.FanRPM));
                } finally { Marshal.FreeHGlobal(buf); }
            }
        } finally { Marshal.FreeHGlobal(e.pAdapters); }
        return sb.ToString();
    }
}
'@
try { [Kmt]::Probe() } catch { "  failed: $_" }
""

"=== 2. NVIDIA (nvidia-smi / NVML) ==="
$smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if ($smi) { & $smi.Source --query-gpu=name,temperature.gpu,power.draw,power.limit,fan.speed --format=csv }
else { "  nvidia-smi not found (no NVIDIA GPU or not on PATH)" }
""

"=== 3. CPU temperature: ACPI thermal zones (performance counters) ==="
try {
    (Get-Counter '\Thermal Zone Information(*)\High Precision Temperature' -ErrorAction Stop).CounterSamples |
        ForEach-Object { "  {0}: {1:0.0} C" -f $_.InstanceName, ($_.CookedValue / 10 - 273.15) }
} catch { "  not available: $($_.Exception.Message)" }
""

"=== 4. CPU power: Energy Meter counters (Windows EMI / RAPL) ==="
try {
    $sets = Get-Counter -ListSet 'Energy Meter*','Power Meter*' -ErrorAction Stop
    $sets | ForEach-Object { "  set: $($_.CounterSetName)" }
    (Get-Counter '\Energy Meter(*)\Power' -ErrorAction Stop).CounterSamples |
        ForEach-Object { "  {0}: {1:0.00} W" -f $_.InstanceName, ($_.CookedValue / 1000) }
} catch { "  not available: $($_.Exception.Message)" }
""
"Done. Paste everything above back into the chat."
