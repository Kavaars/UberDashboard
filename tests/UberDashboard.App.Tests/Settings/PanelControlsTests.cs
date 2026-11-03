using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using UberDashboard.App.Features.Notes;

namespace UberDashboard.App.Tests.Settings;

public sealed class PanelControlsTests
{
    [Fact]
    public void Adaptive_grid_restores_rows_after_resizing_back_to_vertical()
    {
        RunSta(() =>
        {
            var grid = new AdaptivePanelGrid();
            for (var i = 0; i < 3; i++) { grid.RowDefinitions.Add(new RowDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); }
            var child = new Border(); Grid.SetRow(child, 1); AdaptivePanelGrid.SetWideColumn(child, 1); grid.Children.Add(child);
            grid.Measure(new Size(900, 180)); grid.Arrange(new Rect(0, 0, 900, 180)); grid.UpdateLayout();
            Assert.Equal(0, Grid.GetRow(child)); Assert.Equal(1, Grid.GetColumn(child));
            grid.Measure(new Size(320, 700)); grid.Arrange(new Rect(0, 0, 320, 700)); grid.UpdateLayout();
            Assert.Equal(1, Grid.GetRow(child)); Assert.Equal(0, Grid.GetColumn(child));
        });
    }
    [Theory]
    [InlineData(900, 180, true)]
    [InlineData(320, 700, false)]
    [InlineData(900, 500, false)]
    public void Compact_weather_requires_wide_and_short_region(double width, double height, bool expected) =>
        Assert.Equal(expected, new CompactPanelConverter().Convert([width, height], typeof(bool), null!, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(0, 1, 1, "#FFFF0000")]
    [InlineData(120, 1, 1, "#FF00FF00")]
    [InlineData(240, 1, 1, "#FF0000FF")]
    [InlineData(60, 0, 1, "#FFFFFFFF")]
    [InlineData(120, 1, 0, "#FF000000")]
    public void Wheel_maps_hue_saturation_and_brightness(double hue, double saturation, double value, string expected) =>
        Assert.Equal(expected, ColorWheel.FromHsv(hue, saturation, value).ToString());

    [Fact]
    public void Saved_neutral_text_inherits_theme_without_losing_formatting_or_colored_text()
    {
        RunSta(() =>
        {
            var neutral = new Run("Czytelne") { Foreground = Brushes.Black, FontWeight = FontWeights.Bold };
            var colored = new Run("Kolorowe") { Foreground = Brushes.Red };
            var paragraph = new Paragraph { Foreground = Brushes.Black };
            paragraph.Inlines.Add(neutral); paragraph.Inlines.Add(colored);
            var payload = NotesRichContentSerializer.Serialize(new FlowDocument(paragraph)).Payload;
            var editor = new BindableRichTextBox { DocumentPayload = payload, Foreground = Brushes.White };
            var restored = Assert.IsType<Paragraph>(editor.Document.Blocks.FirstBlock);
            var runs = restored.Inlines.OfType<Run>().ToArray();
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(runs[0].Foreground).Color);
            Assert.Equal(FontWeights.Bold, runs[0].FontWeight);
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(runs[1].Foreground).Color);
            editor.Foreground = Brushes.Black;
            Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(runs[0].Foreground).Color);
        });
    }
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Kontrolka WPF nie zakończyła testu.");
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
