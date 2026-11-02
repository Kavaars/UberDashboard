using CommunityToolkit.Mvvm.Input;

namespace UberDashboard.App.Panels;

/// <summary>Umożliwia powłoce pokazanie przycisku formatowania w nagłówku panelu.</summary>
public interface IPanelFormattingPresenter
{
    /// <summary>Panel wspiera bogate formatowanie treści.</summary>
    bool HasFormatting { get; }

    /// <summary>Określa widoczność paska formatowania panelu.</summary>
    bool IsFormattingVisible { get; }

    /// <summary>Przełącza widoczność paska formatowania.</summary>
    IRelayCommand ToggleFormattingCommand { get; }
}
