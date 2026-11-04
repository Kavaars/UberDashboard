using System.Windows;
using System.Windows.Controls;

namespace UberDashboard.App.Controls;

public sealed class PanelOptionsTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        var key = item switch
        {
            Features.Affirmations.AffirmationPanelViewModel => "AffirmationOptionsTemplate",
            Features.Notes.NotesPanelViewModel => "NotesOptionsTemplate",
            Features.Todo.TodoPanelViewModel => "TodoOptionsTemplate",
            Features.Weather.WeatherPanelViewModel => "WeatherOptionsTemplate",
            _ => "ExtensionOptionsTemplate",
        };
        return (container as FrameworkElement)?.FindResource(key) as DataTemplate;
    }
}
