using UberDashboard.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UberDashboard.App.Features.Todo;

public sealed class MicrosoftTodoListViewModel : ObservableObject
{
    private MicrosoftTodoSnapshot? _snapshot;
    private string _status = "";
    public IReadOnlyList<TodoItemState> Items => _snapshot?.Document.Items ?? [];
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    internal void Load(MicrosoftTodoLink link, MicrosoftTodoPreferences preferences)
    {
        _snapshot = preferences.Snapshots.FirstOrDefault(item => item.Link == link);
        OnPropertyChanged(nameof(Items));
        var enabled = preferences.ClientId == link.ClientId && preferences.AccountId == link.AccountId &&
            preferences.SelectedLists.Contains(link.ListId);
        Status = enabled
            ? UiText.Format("MicrosoftToDoReadOnlyLastRefreshed", (_snapshot?.RefreshedAt ?? preferences.LastSync)?.ToLocalTime())
            : UiText.Get("MicrosoftToDoLatestCopySyncIs");
    }
}
