namespace UberDashboard.App.Panels;

internal sealed record ImportedPanelTab(string Name, string StateJson, int SchemaVersion, Features.Todo.MicrosoftTodoLink? Link = null);
