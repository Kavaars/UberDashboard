using UberDashboard.Core.Localization;
using LibreHardwareMonitor.Hardware;

namespace UberDashboard.App.Features.Resources;

public sealed record ResourceReading(string Device, string Name, string Value, string Key = "", float? Percent = null)
{
    public string IconPath => Key.Contains("Temperature", StringComparison.Ordinal) ? "M 10,14 L 10,4 A 2,2 0 0 1 14,4 L 14,14 A 4,4 0 1 1 10,14 M 12,8 L 12,17" :
        Key.Contains("Fan", StringComparison.Ordinal) || Key.Contains("Control:", StringComparison.Ordinal) || Key.Contains("Speed:", StringComparison.Ordinal) ? "M 12,12 C 4,10 6,3 10,4 C 13,5 12,9 12,12 M 12,12 C 17,5 22,10 20,14 C 18,17 15,14 12,12 M 12,12 C 15,20 8,22 6,18 C 5,15 9,13 12,12" :
        Key.Contains("Ram", StringComparison.OrdinalIgnoreCase) ? "M 3,8 L 21,8 L 21,16 L 3,16 Z M 6,10 L 6,14 M 10,10 L 10,14 M 14,10 L 14,14 M 18,10 L 18,14 M 6,16 L 6,19 M 10,16 L 10,19 M 14,16 L 14,19 M 18,16 L 18,19" :
        Key.Contains("Gpu", StringComparison.Ordinal) ? "M 3,6 L 21,6 L 21,18 L 3,18 Z M 5,9 L 9,9 M 5,12 L 9,12 M 16,12 m -3,0 a 3,3 0 1 0 6,0 a 3,3 0 1 0 -6,0" :
        "M 6,6 L 18,6 L 18,18 L 6,18 Z M 9,9 L 15,9 L 15,15 L 9,15 Z M 9,3 L 9,6 M 15,3 L 15,6 M 9,18 L 9,21 M 15,18 L 15,21 M 3,9 L 6,9 M 3,15 L 6,15 M 18,9 L 21,9 M 18,15 L 21,15";
    public string? ProgressDash => Percent is float value && float.IsFinite(value) && value is >= 0 and <= 100
        ? (value * 3 * Math.PI / 50).ToString(System.Globalization.CultureInfo.InvariantCulture) + " 18.85" : null;
    /// <summary>Opis tego, co pokazuje moduł; używany w podpowiedzi po najechaniu.</summary>
    public string Description
    {
        get
        {
            var part = Key[(Key.IndexOf('|') + 1)..];
            var text = part switch
            {
                "CpuUsage" or "Usage" => UiText.Get("ResourceCpuUsageHelp"),
                "CpuTemperature" or "Temperature" => UiText.Get("ResourceCpuTemperatureHelp"),
                "RamUsage" => UiText.Get("ResourceRamUsageHelp"),
                "RamUsed" => UiText.Get("ResourceRamUsedHelp"),
                "GpuUsage" => UiText.Get("ResourceGpuUsageHelp"),
                "GpuTemperature" => UiText.Get("ResourceGpuTemperatureHelp"),
                "VramUsage" => UiText.Get("ResourceVramUsageHelp"),
                "VramUsed" => UiText.Get("ResourceVramUsedHelp"),
                "FanControl" => UiText.Get("ResourceFanControlHelp"),
                _ when part.StartsWith("Control:", StringComparison.Ordinal) => UiText.Get("ResourceFanControlHelp"),
                _ when part.StartsWith("Speed:", StringComparison.Ordinal) => UiText.Get("ResourceFanSpeedHelp"),
                _ => Name,
            };
            if (part is "CpuTemperature" or "Temperature" && Value == UiText.Get("Unavailable") && !HardwareSampler.IsElevated)
                text += " " + UiText.Get("ResourceCpuTemperatureNeedsAdmin");
            return text;
        }
    }
    public string TileLabel => Key.Contains("Temperature", StringComparison.Ordinal)
        ? (Key.Contains("Gpu", StringComparison.Ordinal) ? "GPU °" : "CPU °")
        : Symbol;
    public string TileValue => Key.Contains("Temperature", StringComparison.Ordinal) && Value.EndsWith(" °C", StringComparison.Ordinal)
        ? Value[..^3] : Value;
    public string Symbol => Key.Contains("Temperature", StringComparison.Ordinal) ? "°C" :
        Key.Contains("Fan", StringComparison.Ordinal) || Key.Contains("Control:", StringComparison.Ordinal) || Key.Contains("Speed:", StringComparison.Ordinal) ? "◉" :
        Key.Contains("Vram", StringComparison.Ordinal) ? "VRAM" :
        Key.Contains("Ram", StringComparison.Ordinal) ? "RAM" :
        Key.Contains("Gpu", StringComparison.Ordinal) ? "GPU" : "CPU";
}

internal sealed class HardwareSampler : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private Computer? _computer;
    private IReadOnlyList<ResourceReading> _last = [];
    private long _sampledAt;
    private bool _disposed;

    public IReadOnlyList<ResourceReading> Sample(CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_last.Count > 0 && Environment.TickCount64 - _sampledAt < 1500)
                return _last;

            try
            {
                if (_computer is null)
                {
                    _computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = true,
                        IsMotherboardEnabled = true,
                    };
                    _computer.Open();
                }

                var hardware = _computer.Hardware.SelectMany(UpdateTree).ToArray();
                _last = BuildReadings(hardware);
                _sampledAt = Environment.TickCount64;
                return _last;
            }
            catch
            {
                CloseComputer();
                throw;
            }
        }
    }

    private static IEnumerable<IHardware> UpdateTree(IHardware hardware)
    {
        hardware.Update();
        yield return hardware;
        foreach (var child in hardware.SubHardware.SelectMany(UpdateTree))
            yield return child;
    }

    private static IReadOnlyList<ResourceReading> BuildReadings(IHardware[] hardware)
    {
        var rows = new List<ResourceReading>();
        var cpuFanSensors = hardware.Where(device => device.HardwareType is HardwareType.SuperIO or HardwareType.EmbeddedController)
            .SelectMany(device => device.Sensors)
            .Where(sensor => sensor.Name.StartsWith("CPU Fan", StringComparison.OrdinalIgnoreCase) ||
                sensor.Name.Equals("CPU", StringComparison.OrdinalIgnoreCase)).ToArray();

        var cpus = hardware.Where(device => device.HardwareType == HardwareType.Cpu).ToArray();
        foreach (var cpu in cpus)
        {
            Add(rows, cpu.Name, UiText.Get("CpuUsage"), "CpuUsage", Read(cpu, SensorType.Load, "CPU Total"), "%");
            Add(rows, cpu.Name, UiText.Get("CpuTemperature"), "CpuTemperature",
                ReadCpuTemperature(cpu), "°C");
        }
        if (cpus.Length == 0)
        {
            Add(rows, "CPU", UiText.Get("Usage"), "Usage", null, "%");
            Add(rows, "CPU", UiText.Get("Temperature"), "Temperature", null, "°C");
        }

        AddFans(rows, UiText.Get("CpuCooling"), cpuFanSensors);
        var ram = hardware.FirstOrDefault(device => device.HardwareType == HardwareType.Memory);
        Add(rows, UiText.Get("Memory"), UiText.Get("RamUsage"), "RamUsage", Read(ram, SensorType.Load, "Memory"), "%");
        Add(rows, UiText.Get("Memory"), UiText.Get("RamUsed"), "RamUsed", Read(ram, SensorType.Data, "Memory Used"), "GiB");

        var gpus = hardware.Where(device => device.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel).ToArray();
        if (gpus.Length == 0)
            AddGpu(rows, null);
        foreach (var gpu in gpus)
            AddGpu(rows, gpu);

        return rows;
    }

    private static void AddGpu(List<ResourceReading> rows, IHardware? gpu)
    {
        var name = gpu?.Name ?? "GPU";
        Add(rows, name, UiText.Get("GpuUsage"), "GpuUsage", Read(gpu, SensorType.Load, "GPU Core", "GPU Total", "D3D 3D"), "%");
        Add(rows, name, UiText.Get("GpuTemperature"), "GpuTemperature", Read(gpu, SensorType.Temperature, "GPU Core", "GPU"), "°C");

        var used = Read(gpu, SensorType.SmallData, "GPU Memory Used");
        var total = Read(gpu, SensorType.SmallData, "GPU Memory Total");
        if (used is null || total is null)
        {
            var dedicatedUsed = Read(gpu, SensorType.SmallData, "D3D Dedicated Memory Used");
            var dedicatedTotal = Read(gpu, SensorType.SmallData, "D3D Dedicated Memory Total");
            if (dedicatedUsed is not null && dedicatedTotal > 0)
                (used, total) = (dedicatedUsed, dedicatedTotal);
            else if (used is null)
                (used, total) = (dedicatedUsed, null);
        }
        Add(rows, name, UiText.Get("VramUsage"), "VramUsage", MemoryPercent(used, total), "%");
        Add(rows, name, UiText.Get("VramUsed"), "VramUsed", used / 1024, "GiB");
        AddFans(rows, name, gpu?.Sensors ?? []);
    }

    private static void AddFans(List<ResourceReading> rows, string device, ISensor[] sensors)
    {
        var controls = sensors.Where(sensor => sensor.SensorType == SensorType.Control).ToArray();
        if (controls.Length == 0)
            Add(rows, device, UiText.Get("FanControl"), "FanControl", null, "%");
        foreach (var sensor in controls)
            Add(rows, device, UiText.Format("Control", sensor.Name), "Control:" + sensor.Name, sensor.Value, "%");
        foreach (var sensor in sensors.Where(sensor => sensor.SensorType == SensorType.Fan))
            Add(rows, device, UiText.Format("Speed", sensor.Name), "Speed:" + sensor.Name, sensor.Value, "RPM");
    }

    /// <summary>Nazwy czujników różnią się między CPU Intel i AMD; gdy żadna nie pasuje, bierze najgorętszy rdzeń.</summary>
    private static float? ReadCpuTemperature(IHardware cpu)
    {
        var named = Read(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "Core Average", "CCD Max (Tdie)", "CPU Core Max");
        if (named is not null) return named;
        float? hottest = null;
        foreach (var sensor in cpu.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature || sensor.Value is not float value || !float.IsFinite(value) || value <= 0) continue;
            if (!(sensor.Name.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) || sensor.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase)) ||
                sensor.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase)) continue;
            hottest = Math.Max(hottest ?? value, value);
        }
        return hottest;
    }

    /// <summary>Sterownik LibreHardwareMonitor czyta temperatury CPU tylko z uprawnieniami administratora.</summary>
    internal static readonly bool IsElevated = CheckElevated();
    private static bool CheckElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception) { return true; }
    }

    private static float? Read(IHardware? device, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            if (device?.Sensors.FirstOrDefault(sensor => sensor.SensorType == type &&
                sensor.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value is float value && float.IsFinite(value) &&
                (type != SensorType.Temperature || value != 0))
                return value;
        }
        return null;
    }

    internal static float? MemoryPercent(float? used, float? total) => total > 0 ? used * 100 / total : null;

    internal static string Format(float? value, string unit) =>
        value is float number && float.IsFinite(number) && (unit != "%" || number is >= 0 and <= 100)
            ? $"{number:0.#} {unit}" : UiText.Get("Unavailable");

    private static void Add(List<ResourceReading> rows, string device, string name, string key, float? value, string unit) =>
        rows.Add(new(device, name, Format(value, unit), (device == UiText.Get("Memory") ? "Memory" : device == UiText.Get("CpuCooling") ? "CpuCooling" : device) + "|" + key, unit == "%" ? value : null));

    private void CloseComputer()
    {
        var computer = _computer;
        _computer = null;
        _last = [];
        computer?.Close();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CloseComputer();
        }
    }

    public ValueTask DisposeAsync() => new(Task.Run(Dispose));
}
