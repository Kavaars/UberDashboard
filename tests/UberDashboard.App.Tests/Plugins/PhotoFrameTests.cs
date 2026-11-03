using System.IO;
using PhotoFrame.Plugin;

namespace UberDashboard.App.Tests.Plugins;

public sealed class PhotoFrameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "UberDashboard-photo-tests", Guid.NewGuid().ToString("N"));
    public PhotoFrameTests() => Directory.CreateDirectory(_root);
    [Theory]
    [InlineData(-10, 1)]
    [InlineData(0, 1)]
    [InlineData(30, 30)]
    [InlineData(int.MaxValue, 86400)]
    public void Normalizes_slide_interval(int input, int expected)
        => Assert.Equal(expected, new PhotoFrameDocument { IntervalSeconds = input }.Normalize().IntervalSeconds);
    [Theory]
    [InlineData("photo.JPG", true)]
    [InlineData("photo.png", true)]
    [InlineData("photo.tiff", true)]
    [InlineData("photo.exe", false)]
    [InlineData("photo.svg", false)]
    public void Filters_supported_formats(string path, bool expected) => Assert.Equal(expected, PhotoFiles.IsSupported(path));
    [Fact]
    public void Enumerates_top_level_images_in_stable_order()
    {
        File.WriteAllText(Path.Combine(_root, "b.JPG"), "test");
        File.WriteAllText(Path.Combine(_root, "a.png"), "test");
        File.WriteAllText(Path.Combine(_root, "ignore.txt"), "test");
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(_root, "nested", "c.png"), "test");
        Assert.Equal(["a.png", "b.JPG"], PhotoFiles.Enumerate(_root, true, default).Select(path => Path.GetFileName(path)!).ToArray());
    }
    [Fact]
    public void Empty_folder_is_supported() => Assert.Empty(PhotoFiles.Enumerate(_root, true, default));
    [Fact]
    public void Missing_image_reports_error() => Assert.Throws<FileNotFoundException>(() => PhotoFiles.Enumerate(Path.Combine(_root, "missing.png"), false, default));
    [Fact]
    public void Missing_folder_reports_error() => Assert.Throws<DirectoryNotFoundException>(() => PhotoFiles.Enumerate(Path.Combine(_root, "missing"), true, default));
    [Fact]
    public void Cancellation_interrupts_enumeration()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PhotoFiles.Enumerate(_root, true, cts.Token));
    }
    [Fact]
    public void Corrupt_image_reports_error()
    {
        var path = Path.Combine(_root, "corrupt.png"); File.WriteAllText(path, "invalid");
        Assert.ThrowsAny<Exception>(() => PhotoFiles.Decode(path, default));
    }
    public void Dispose() => Directory.Delete(_root, true);
}
