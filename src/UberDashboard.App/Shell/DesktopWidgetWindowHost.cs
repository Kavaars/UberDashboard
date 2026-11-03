namespace UberDashboard.App.Shell;

public interface IDesktopWidgetWindowHost
{
    Task<bool> TryAttachAsync(IntPtr windowHandle, double opacity, CancellationToken cancellationToken);
    Task DetachAsync(IntPtr windowHandle, CancellationToken cancellationToken);
}

/// <summary>Jawny fallback: brak wspieranej, zweryfikowanej powierzchni pulpitu.</summary>
public sealed class DesktopWidgetWindowHost : IDesktopWidgetWindowHost
{
    public Task<bool> TryAttachAsync(IntPtr windowHandle, double opacity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task DetachAsync(IntPtr windowHandle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
