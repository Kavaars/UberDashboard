using UberDashboard.Application.Import;

namespace UberDashboard.Application.Tests.Import;

public sealed class TodoImportTests
{
    [Fact]
    public void Markdown_skips_fenced_code_and_preserves_unknown_states()
    {
        var items = TodoTextImport.Parse(
            "# Lista\r\n- [ ] Pierwsze\n  - [?] Drugie\n~~~md\n- [x] Przykład\n~~~\n1. [X] Trzecie", true).ToArray();

        Assert.Equal(
            new[] { new ImportedTask("Pierwsze", false, 2), new ImportedTask("Drugie", null, 3, true),
                new ImportedTask("Trzecie", true, 7) },
            items);
    }

    [Fact]
    public void Plain_text_keeps_duplicates_and_accepts_explicit_statuses()
    {
        var items = TodoTextImport.Parse("Zakupy\n\n- [x] Zakupy\nZakupy", false).ToArray();

        Assert.Equal(new[] { false, true, false }, items.Select(item => item.IsDone!.Value));
        Assert.All(items, item => Assert.Equal("Zakupy", item.Text));
    }

    [Fact]
    public void Csv_reads_quoted_multiline_fields_and_maps_selected_columns()
    {
        var items = TodoCsvImport.Parse("Status;Treść\r\nTak;\"Telefon; dom\r\nPraca\"\r\n?;Inne",
            ';', true, 1, 0).ToArray();

        Assert.Equal(new ImportedTask("Telefon; dom\r\nPraca", true, 2), items[0]);
        Assert.Equal(new ImportedTask("Inne", null, 4), items[1]);
    }
}
