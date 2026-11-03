using System.IO;
using System.IO.Compression;
using System.Text.Json;
using UberDashboard.App.Plugins;
using UberDashboard.Contracts.Plugins;

namespace UberDashboard.App.Tests.Plugins;

public sealed class PluginPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "UberDashboard-plugin-tests", Guid.NewGuid().ToString("N"));
    public PluginPackageTests() => Directory.CreateDirectory(_root);
    private static PluginManifest Manifest => new()
    {
        Id = "pl.test.photos", Name = "Photos", Version = "1.0.0", ApiVersion = "1.1.0",
        MinHostVersion = "1.2.0", TargetFramework = "net10.0-windows", EntryAssembly = "Photos.dll",
        EntryType = "Photos.Module", Panels = [new() { TypeId = "pl.test.photos.frame", Name = "Frame" }],
    };
    private string Package(PluginManifest? manifest = null, string extra = "Photos.dll")
    {
        var path = Path.Combine(_root, Guid.NewGuid() + ".udplugin");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open()))
            writer.Write(JsonSerializer.Serialize(manifest ?? Manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using (var writer = new StreamWriter(zip.CreateEntry(extra).Open())) writer.Write("fixture");
        return path;
    }
    [Fact]
    public async Task Extracts_valid_package_without_executing_code()
    {
        var result = await PluginPackage.ExtractAsync(Package(), Path.Combine(_root, "staging"), "1.2.0", default);
        Assert.Equal(Manifest.Id, result.Id);
        Assert.True(File.Exists(Path.Combine(_root, "staging", "Photos.dll")));
    }
    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("/escape.dll")]
    [InlineData("C:/escape.dll")]
    [InlineData("folder/../../escape.dll")]
    [InlineData("file.dll:stream")]
    [InlineData("CON.dll")]
    [InlineData("trailing./file.dll")]
    [InlineData(".enabled")]
    [InlineData(".ENABLED")]
    public async Task Rejects_unsafe_archive_paths(string path)
        => await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(Package(extra: path), Path.Combine(_root, "staging"), "1.2.0", default));
    [Theory]
    [InlineData("../bad", "1.1.0", "1.2.0")]
    [InlineData("pl.test.photos", "2.0.0", "1.2.0")]
    [InlineData("pl.test.photos", "1.1.0", "99.0.0")]
    public async Task Rejects_invalid_identity_or_incompatible_versions(string id, string api, string host)
        => await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(Package(Manifest with { Id = id, ApiVersion = api, MinHostVersion = host }), Path.Combine(_root, "staging"), "1.2.0", default));
    [Fact]
    public async Task Rejects_duplicate_case_insensitive_entries()
    {
        var path = Package();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) zip.CreateEntry("PHOTOS.dll");
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(path, Path.Combine(_root, "staging"), "1.2.0", default));
    }
    [Fact]
    public async Task Cancellation_does_not_enable_a_package()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PluginPackage.ExtractAsync(Package(), Path.Combine(_root, "staging"), "1.2.0", cts.Token));
        Assert.False(File.Exists(Path.Combine(_root, "staging", ".enabled")));
    }
    [Fact]
    public async Task Rejects_missing_entry_assembly()
        => await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(Package(extra: "wrong.dll"), Path.Combine(_root, "staging"), "1.2.0", default));
    [Fact]
    public async Task Rejects_wrong_framework()
        => await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(Package(Manifest with { TargetFramework = "net8.0-windows" }), Path.Combine(_root, "staging"), "1.2.0", default));
    [Fact]
    public async Task Rejects_empty_panel_declarations()
        => await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(Package(Manifest with { Panels = [] }), Path.Combine(_root, "staging"), "1.2.0", default));
    [Fact]
    public async Task Rejects_too_many_archive_entries()
    {
        var path = Package();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            for (var i = 0; i < PluginPackage.MaxFiles; i++) zip.CreateEntry($"file{i}.txt");
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(path, Path.Combine(_root, "staging"), "1.2.0", default));
    }
    [Fact]
    public async Task Rejects_expanded_archive_above_limit_before_extraction()
    {
        var path = Package();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        using (var stream = zip.CreateEntry("large.bin", CompressionLevel.Fastest).Open())
        {
            var buffer = new byte[1024 * 1024];
            for (var i = 0; i < 129; i++) await stream.WriteAsync(buffer);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(path, Path.Combine(_root, "staging"), "1.2.0", default));
        Assert.False(Directory.Exists(Path.Combine(_root, "staging")));
    }
    [Fact]
    public async Task Rejects_symbolic_links()
    {
        var path = Package();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) zip.CreateEntry("link.dll").ExternalAttributes = unchecked((int)0xA1FF0000);
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(path, Path.Combine(_root, "staging"), "1.2.0", default));
    }
    [Fact]
    public async Task Rejects_malformed_json()
    {
        var path = Package();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("plugin.json")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open()); writer.Write("{invalid");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginPackage.ExtractAsync(path, Path.Combine(_root, "staging"), "1.2.0", default));
    }    public void Dispose() => Directory.Delete(_root, true);
}
