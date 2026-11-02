using UberDashboard.Core.Layout;

namespace UberDashboard.Contracts.Panels;

/// <summary>
/// Instancja panelu o tożsamości <see cref="InstanceId"/>. Cykl życia jest
/// sterowany przez hosta; operacje sieciowe i plikowe wykonywane są poza
/// wątkiem interfejsu.
/// </summary>
public interface IPanelInstance : IAsyncDisposable
{
    /// <summary>Identyfikator instancji; zgodny z wartością z <see cref="PanelContext"/>.</summary>
    PanelInstanceId InstanceId { get; }

    /// <summary>
    /// Obiekt wiązany przez warstwę widoku (ViewModelem). Warstwa WPF mapuje
    /// <see cref="IPanelDescriptor.TypeId"/> na szablon i wiąże ten obiekt.
    /// </summary>
    object ViewModel { get; }

    /// <summary>Przygotowuje panel do pracy po utworzeniu.</summary>
    ValueTask InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Odtwarza stan zapisany wcześniej przez <see cref="CaptureStateAsync"/>.</summary>
    ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken);

    /// <summary>Zwraca bieżący stan do zapisania przez hosta.</summary>
    ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken);

    /// <summary>Wstrzymuje pracę panelu np. przy ukryciu.</summary>
    ValueTask SuspendAsync(CancellationToken cancellationToken);

    /// <summary>Wznawia pracę panelu np. po przywróceniu.</summary>
    ValueTask ResumeAsync(CancellationToken cancellationToken);

    /// <summary>Pozwala panelowi zwolnić zasoby przed trwałym usunięciem.</summary>
    ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken);
}
