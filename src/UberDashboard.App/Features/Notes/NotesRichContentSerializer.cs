using System.IO;
using System.Windows;
using System.Windows.Documents;
using WpfDataFormats = System.Windows.DataFormats;

namespace UberDashboard.App.Features.Notes;

/// <summary>Konwertuje dokument WPF notatnika do trwałego formatu z obsługą obrazów.</summary>
public static class NotesRichContentSerializer
{
    /// <summary>Tworzy dokument z trwałego zapisu; przy błędzie zwraca prosty dokument tekstowy.</summary>
    public static FlowDocument Deserialize(string? payload, NotesContentFormat format, string? plainTextFallback)
    {
        var document = CreateDocument();
        if (string.IsNullOrWhiteSpace(payload))
        {
            ApplyPlainText(document, plainTextFallback);
            return document;
        }

        try
        {
            switch (format)
            {
                case NotesContentFormat.XamlPackage:
                    var buffer = Convert.FromBase64String(payload);
                    using (var stream = new MemoryStream(buffer))
                    {
                        new TextRange(document.ContentStart, document.ContentEnd).Load(stream, WpfDataFormats.XamlPackage);
                    }
                    break;

                default:
                    ApplyPlainText(document, payload);
                    break;
            }
        }
        catch (FormatException)
        {
            ApplyPlainText(document, plainTextFallback ?? payload);
        }
        catch (InvalidOperationException)
        {
            ApplyPlainText(document, plainTextFallback);
        }

        return document;
    }

    /// <summary>Serializuje dokument do formatu trwałego oraz tekstowego fallbacku.</summary>
    public static NotesSerializedContent Serialize(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var stream = new MemoryStream();
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        range.Save(stream, WpfDataFormats.XamlPackage);

        return new NotesSerializedContent(
            Convert.ToBase64String(stream.ToArray()),
            range.Text.TrimEnd('\r', '\n'),
            NotesContentFormat.XamlPackage);
    }

    /// <summary>Eksportuje dokument do czystego tekstu.</summary>
    public static string ToPlainText(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd('\r', '\n');
    }

    private static FlowDocument CreateDocument() =>
        new()
        {
            PagePadding = new Thickness(0),
        };

    private static void ApplyPlainText(FlowDocument document, string? text)
    {
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(new Run(text ?? string.Empty)));
    }
}

/// <summary>Pakiet trwałego zapisu bogatej treści notatki.</summary>
public sealed record NotesSerializedContent(string Payload, string PlainText, NotesContentFormat Format);
