namespace UberDashboard.Application.Synchronization;

/// <summary>Receives sanitized integration diagnostics without account or resource identifiers.</summary>
public interface IIntegrationDiagnostics
{
    void Report(string provider, string operation, Exception error);
}
