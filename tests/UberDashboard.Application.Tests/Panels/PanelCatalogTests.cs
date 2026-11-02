using UberDashboard.Application.Panels;
using UberDashboard.Application.Tests.TestSupport;
using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Tests.Panels;

public sealed class PanelCatalogTests
{
    [Fact]
    public void Registers_and_finds_descriptor()
    {
        var catalog = new PanelCatalog();
        var descriptor = new FakePanelDescriptor("todo", "Zadania");

        Assert.True(catalog.TryRegister("pl.host.builtin", descriptor));
        Assert.True(catalog.Contains("todo"));
        Assert.Same(descriptor, catalog.DescriptorOf("todo"));
        Assert.Equal("pl.host.builtin", catalog.Find("todo")!.PluginId);
    }

    [Fact]
    public void Duplicate_type_id_is_rejected_without_overwrite()
    {
        var catalog = new PanelCatalog();
        var first = new FakePanelDescriptor("todo", "Zadania");
        var second = new FakePanelDescriptor("todo", "Inny");

        Assert.True(catalog.TryRegister("pl.host.builtin", first));
        Assert.False(catalog.TryRegister("pl.example", second));
        Assert.Same(first, catalog.DescriptorOf("todo"));
        Assert.Single(catalog.Registrations);
    }

    [Fact]
    public void RemovePlugin_removes_only_that_plugin_entries()
    {
        var catalog = new PanelCatalog();
        catalog.TryRegister("pl.a", new FakePanelDescriptor("a.todo"));
        catalog.TryRegister("pl.b", new FakePanelDescriptor("b.notes"));

        catalog.RemovePlugin("pl.a");

        Assert.False(catalog.Contains("a.todo"));
        Assert.True(catalog.Contains("b.notes"));
        Assert.Single(catalog.Registrations);
    }

    [Fact]
    public void MinSizeSource_returns_zero_for_unknown_instance()
    {
        var source = new PanelCatalog.MinSizeSource();
        var known = PanelInstanceId.New();

        source.Set(known, new MinSize(120d, 80d));

        Assert.Equal(new MinSize(120d, 80d), source.MinimumOf(known));
        Assert.Equal(MinSize.Zero, source.MinimumOf(PanelInstanceId.New()));
    }

    [Fact]
    public void MinSizeSource_remove_drops_entry()
    {
        var source = new PanelCatalog.MinSizeSource();
        var instance = PanelInstanceId.New();
        source.Set(instance, new MinSize(10d, 10d));

        Assert.True(source.Remove(instance));
        Assert.Equal(MinSize.Zero, source.MinimumOf(instance));
        Assert.False(source.Remove(instance));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_rejects_blank_plugin_id(string pluginId)
    {
        var catalog = new PanelCatalog();

        Assert.Throws<ArgumentException>(() => catalog.TryRegister(pluginId, new FakePanelDescriptor("todo")));
    }
}
