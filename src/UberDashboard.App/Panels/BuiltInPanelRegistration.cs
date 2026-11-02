using UberDashboard.Contracts.Panels;

namespace UberDashboard.App.Panels;

/// <summary>
/// Zgłoszenie wbudowanego typu panelu. Kolekcja tych rekordów buduje rejestr
/// typów paneli, dzięki czemu dodanie nowego panelu to jedna nowa rejestracja.
/// </summary>
public sealed record BuiltInPanelRegistration(string TypeId, IPanelDescriptor Descriptor);
