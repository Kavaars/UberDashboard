using System.Windows.Input;
namespace UberDashboard.App.Shell;

public sealed record PanelLayoutAction(string Header, ICommand Command);
