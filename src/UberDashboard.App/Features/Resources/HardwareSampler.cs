using UberDashboard.Core.Localization;
using LibreHardwareMonitor.Hardware;

namespace UberDashboard.App.Features.Resources;

public sealed record ResourceReading(string Device, string Name, string Value);

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
            Add(rows, cpu.Name, UiText.Get("CpuUsage"), Read(cpu, SensorType.Load, "CPU Total"), "%");
            Add(rows, cpu.Name, UiText.Get("CpuTemperature"),
                Read(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "Core Average", "CCD Max (Tdie)", "CPU Core Max"), "°C");
        }
        if (cpus.Length == 0)
        {
            Add(rows, "CPU", UiText.Get("Usage"), null, "%");
            Add(rows, "CPU", UiText.Get("Temperature"), null, "°C");
        }

        AddFans(rows, UiText.Get("CpuCooling"), cpuFanSensors);
        var ram = hardware.FirstOrDefault(device => device.HardwareType == HardwareType.Memory);
        Add(rows, UiText.Get("Memory"), UiText.Get("RamUsage"), Read(ram, SensorType.Load, "Memory"), "%");
        Add(rows, UiText.Get("Memory"), UiText.Get("RamUsed"), Read(ram, SensorType.Data, "Memory Used"), "GiB");

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
        Add(rows, name, UiText.Get("GpuUsage"), Read(gpu, SensorType.Load, "GPU Core", "GPU Total", "D3D 3D"), "%");
        Add(rows, name, UiText.Get("GpuTemperature"), Read(gpu, SensorType.Temperature, "GPU Core", "GPU"), "°C");

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
        Add(rows, name, UiText.Get("VramUsage"), MemoryPercent(used, total), "%");
        Add(rows, name, UiText.Get("VramUsed"), used / 1024, "GiB");
        AddFans(rows, name, gpu?.Sensors ?? []);
    }

    private static void AddFans(List<ResourceReading> rows, string device, ISensor[] sensors)
    {
        var controls = sensors.Where(sensor => sensor.SensorType == SensorType.Control).ToArray();
        if (controls.Length == 0)
            Add(rows, device, UiText.Get("FanControl"), null, "%");
        foreach (var sensor in controls)
            Add(rows, device, UiText.Format("Control", sensor.Name), sensor.Value, "%");
        foreach (var sensor in sensors.Where(sensor => sensor.SensorType == SensorType.Fan))
            Add(rows, device, UiText.Format("Speed", sensor.Name), sensor.Value, "RPM");
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

    private static void Add(List<ResourceReading> rows, string device, string name, float? value, string unit) =>
        rows.Add(new(device, name, Format(value, unit)));

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
