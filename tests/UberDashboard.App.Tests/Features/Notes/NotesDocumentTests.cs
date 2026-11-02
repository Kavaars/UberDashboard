using UberDashboard.App.Features.Notes;

namespace UberDashboard.App.Tests.Features.Notes;

public sealed class NotesDocumentTests
{
    [Fact]
    public void Schema_version_tracks_rich_content_format()
    {
        Assert.True(NotesDocument.SchemaVersion >= 2);
    }

    [Fact]
    public void New_document_keeps_plain_text_fallback_for_search_and_recovery()
    {
        var document = new NotesDocument
        {
            Title = "Plan",
            PlainText = "Lista zadan",
            RichContent = "base64-payload",
            RichContentFormat = NotesContentFormat.XamlPackage,
            Revision = 3,
        };

        Assert.Equal("Plan", document.Title);
        Assert.Equal("Lista zadan", document.PlainText);
        Assert.Equal("base64-payload", document.RichContent);
        Assert.Equal(NotesContentFormat.XamlPackage, document.RichContentFormat);
        Assert.Equal(3, document.Revision);
    }
}
