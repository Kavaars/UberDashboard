using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PhotoFrame.Plugin;

/// <summary>Widok pluginu korzysta z tokenów motywu hosta; nie modyfikuje globalnych zasobów.</summary>
public sealed class PhotoFrameView : UserControl, UberDashboard.Contracts.Panels.IResponsivePanel
{
    private readonly Grid _root;
    private readonly TextBlock _status;
    public PhotoFrameView()
    {
        var root = new Grid { Margin = new Thickness(8) };
        _root = root;
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var photo = new Image { Stretch = Stretch.Uniform };
        photo.SetBinding(Image.SourceProperty, new Binding(nameof(PhotoFrameModel.CurrentPhoto)));
        System.Windows.Automation.AutomationProperties.SetName(photo, "Wyświetlane zdjęcie");
        root.Children.Add(photo);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 2, 2) };
        _status = status;
        status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Muted");
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(PhotoFrameModel.Status)));
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        Grid.SetRow(status, 1); root.Children.Add(status);

        var options = new StackPanel { Margin = new Thickness(12) };
        options.Children.Add(Label("Opcje ramki", true));
        options.Children.Add(Button("Wybierz obraz…", nameof(PhotoFrameModel.ChooseFileCommand), "Wyświetla jeden wskazany obraz. Zdjęcie nie jest kopiowane ani zmieniane."));
        options.Children.Add(Button("Wybierz folder…", nameof(PhotoFrameModel.ChooseFolderCommand), "Pokazuje zdjęcia bezpośrednio z folderu, w kolejności nazw, bez podfolderów."));
        var path = Label("");
        path.SetBinding(TextBlock.TextProperty, new Binding(nameof(PhotoFrameModel.SourcePath)));
        path.TextTrimming = TextTrimming.CharacterEllipsis;
        path.SetBinding(ToolTipProperty, new Binding(nameof(PhotoFrameModel.SourcePath)));
        options.Children.Add(path);
        var intervalLabel = Label("Zmieniaj obraz co (sekundy, 1–86400)");
        options.Children.Add(intervalLabel);
        var interval = new TextBox { Margin = new Thickness(0, 6, 0, 8), MinWidth = 80 };
        interval.SetResourceReference(StyleProperty, "ThemedTextBox");
        interval.SetBinding(TextBox.TextProperty, new Binding(nameof(PhotoFrameModel.IntervalSeconds)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true });
        interval.SetBinding(IsEnabledProperty, new Binding(nameof(PhotoFrameModel.HasMultipleImages)));
        interval.ToolTip = "Czas pokazu jednego zdjęcia. Ustawienie działa, gdy folder zawiera co najmniej dwa obrazy.";
        System.Windows.Automation.AutomationProperties.SetName(interval, "Czas zmiany zdjęcia w sekundach");
        options.Children.Add(interval);
        options.Children.Add(Button("Odśwież źródło", nameof(PhotoFrameModel.ReloadCommand), "Ponownie odczytuje folder i zdjęcia, uwzględniając dodane lub usunięte pliki."));
        options.Children.Add(Button("Zamknij opcje", nameof(PhotoFrameModel.ToggleOptionsCommand), "Wraca do wyświetlania zdjęcia. Opcje można otworzyć kołem zębatym w nagłówku."));
        var card = new Border { MaxWidth = 330, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = options } };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        card.SetBinding(VisibilityProperty, new Binding(nameof(PhotoFrameModel.IsOptionsOpen)) { Converter = new BooleanToVisibilityConverter() });
        root.Children.Add(card);
        var loading = new ProgressBar { Height = 3, VerticalAlignment = VerticalAlignment.Top, IsIndeterminate = true };
        loading.SetBinding(VisibilityProperty, new Binding(nameof(PhotoFrameModel.IsLoading)) { Converter = new BooleanToVisibilityConverter() });
        root.Children.Add(loading);
        Content = root;
    }
    public void OnPanelSizeChanged(double width, double height)
    {
        var horizontal = width >= 480 && height < 260;
        Grid.SetRow(_status, horizontal ? 0 : 1);
        Grid.SetColumn(_status, horizontal ? 1 : 0);
        _status.MaxWidth = horizontal ? Math.Max(120, Math.Min(220, width * .3)) : double.PositiveInfinity;
        _root.RowDefinitions[1].Height = horizontal ? new GridLength(0) : GridLength.Auto;
    }
    private static TextBlock Label(string text, bool bold = false)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        return label;
    }
    private static Button Button(string label, string command, string tip)
    {
        var button = new Button { Content = label, ToolTip = tip, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.SetResourceReference(StyleProperty, "ToolbarButton");
        button.SetBinding(System.Windows.Controls.Button.CommandProperty, new Binding(command));
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
}
