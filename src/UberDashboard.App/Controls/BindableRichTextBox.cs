using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using UberDashboard.App.Features.Notes;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;

namespace UberDashboard.App.Controls;

/// <summary>RichTextBox z prostym wiązaniem dokumentu do trwałego payloadu.</summary>
public sealed class BindableRichTextBox : WpfRichTextBox
{
    private bool _isInternalUpdate;
    private const double UnwrappedPageWidth = 4096d;

    public static readonly DependencyProperty DocumentPayloadProperty =
        DependencyProperty.Register(
            nameof(DocumentPayload),
            typeof(string),
            typeof(BindableRichTextBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDocumentPayloadChanged));

    public static readonly DependencyProperty PlainTextProperty =
        DependencyProperty.Register(
            nameof(PlainText),
            typeof(string),
            typeof(BindableRichTextBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsDocumentWordWrapEnabledProperty =
        DependencyProperty.Register(
            nameof(IsDocumentWordWrapEnabled),
            typeof(bool),
            typeof(BindableRichTextBox),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnWordWrapChanged));

    /// <summary>Tworzy kontrolkę z domyślnym dokumentem i aktywnym zawijaniem.</summary>
    public BindableRichTextBox()
    {
        ApplyWordWrap();
    }

    /// <summary>Zakodowany dokument XAML package.</summary>
    public string DocumentPayload
    {
        get => (string)GetValue(DocumentPayloadProperty);
        set => SetValue(DocumentPayloadProperty, value);
    }

    /// <summary>Tekstowy fallback dokumentu.</summary>
    public string PlainText
    {
        get => (string)GetValue(PlainTextProperty);
        set => SetValue(PlainTextProperty, value);
    }

    /// <summary>Przełącza zawijanie wierszy wewnątrz dokumentu.</summary>
    public bool IsDocumentWordWrapEnabled
    {
        get => (bool)GetValue(IsDocumentWordWrapEnabledProperty);
        set => SetValue(IsDocumentWordWrapEnabledProperty, value);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);

        if (_isInternalUpdate)
        {
            return;
        }

        var content = NotesRichContentSerializer.Serialize(Document);
        SetCurrentValue(DocumentPayloadProperty, content.Payload);
        SetCurrentValue(PlainTextProperty, content.PlainText);
    }

    private static void OnDocumentPayloadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BindableRichTextBox richTextBox || richTextBox._isInternalUpdate)
        {
            return;
        }

        richTextBox._isInternalUpdate = true;
        try
        {
            richTextBox.Document = NotesRichContentSerializer.Deserialize(
                e.NewValue as string,
                NotesContentFormat.XamlPackage,
                richTextBox.PlainText);
            richTextBox.ApplyWordWrap();
        }
        finally
        {
            richTextBox._isInternalUpdate = false;
        }
    }

    private static void OnWordWrapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BindableRichTextBox richTextBox)
        {
            richTextBox.ApplyWordWrap();
        }
    }

    private void ApplyWordWrap()
    {
        if (Document is null)
        {
            return;
        }

        Document.PageWidth = IsDocumentWordWrapEnabled ? double.NaN : UnwrappedPageWidth;
        HorizontalScrollBarVisibility = IsDocumentWordWrapEnabled ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }
}
