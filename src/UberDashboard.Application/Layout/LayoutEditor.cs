using Microsoft.Extensions.Logging;
using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Layout;

/// <summary>Wynik zapisu układu.</summary>
public sealed record LayoutSaveResult(bool IsSuccess, LayoutErrorCode Error, string? Message)
{
    /// <summary>Zapis udany.</summary>
    public static LayoutSaveResult Ok { get; } = new(true, LayoutErrorCode.None, null);

    /// <summary>Zapis odrzucony; układ w magazynie pozostaje bez zmian.</summary>
    public static LayoutSaveResult Fail(LayoutErrorCode error, string message) => new(false, error, message);
}

/// <summary>
/// Use-case edycji układu jednego pulpitu. Trzyma układ w pamięci, deleguje czyste
/// operacje do <see cref="LayoutOperations"/>, waliduje przed zapisem i utrwala
/// wynik przez <see cref="ILayoutStore"/>. Edytor nigdy nie zapisuje stanu
/// częściowo zmodyfikowanego — nieudana operacja zwraca układ wejściowy.
/// </summary>
public sealed class LayoutEditor
{
    private readonly ILayoutStore _store;
    private readonly ILogger<LayoutEditor> _logger;
    private HashSet<PanelInstanceId> _visibleInstances = [];

    /// <summary>Tworzy edytor dla źródła minimów rozmiaru paneli.</summary>
    public LayoutEditor(
        ILayoutStore store,
        IPanelMinSizeSource minSizes,
        ILogger<LayoutEditor> logger,
        LayoutLimits? limits = null,
        LayoutMetrics? metrics = null)
    {
        _store = store;
        _logger = logger;
        MinSizes = minSizes;
        Limits = limits ?? LayoutLimits.Default;
        Metrics = metrics ?? LayoutMetrics.Default;
    }

    /// <summary>Identyfikator edytowanego pulpitu.</summary>
    public Guid DashboardId { get; private set; }

    /// <summary>Bieżący układ w pamięci.</summary>
    public LayoutTree Tree { get; private set; } = LayoutTree.Empty;

    /// <summary>Wersja schematu ostatnio wczytanego dokumentu.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary><see langword="true"/>, gdy w pamięci są zmiany nieutrwalone.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Błąd ostatniego wczytania układu; <see cref="LayoutErrorCode.None"/> przy powodzeniu.</summary>
    public LayoutErrorCode LastLoadError { get; private set; } = LayoutErrorCode.None;

    /// <summary>Opis ostatniego błędu wczytania.</summary>
    public string? LastLoadMessage { get; private set; }

    /// <summary><see langword="true"/>, gdy oryginalny dokument musi zostać zachowany.</summary>
    public bool OriginalDocumentPreserved { get; private set; }

    /// <summary>Źródło minimów rozmiaru paneli.</summary>
    public IPanelMinSizeSource MinSizes { get; }

    /// <summary>Limity chroniące układ.</summary>
    public LayoutLimits Limits { get; }

    /// <summary>Geometria układu.</summary>
    public LayoutMetrics Metrics { get; }

    /// <summary>Zbiór instancji, które istnieją i nie są ukryte.</summary>
    public IReadOnlyCollection<PanelInstanceId> VisibleInstances => _visibleInstances;

    /// <summary>Ustawia zbiór widocznych instancji używany przy walidacji.</summary>
    public void SetVisibleInstances(IEnumerable<PanelInstanceId> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _visibleInstances = [.. instances];
    }

    /// <summary>Wczytuje układ pulpitu i ustawia bieżący stan edytora.</summary>
    public async Task<LayoutLoadResult> LoadAsync(Guid dashboardId, CancellationToken cancellationToken = default)
    {
        var result = await _store.LoadAsync(dashboardId, cancellationToken).ConfigureAwait(false);

        DashboardId = dashboardId;
        SchemaVersion = result.SchemaVersion;
        LastLoadError = result.Error;
        LastLoadMessage = result.Message;
        OriginalDocumentPreserved = result.MustPreserveOriginalDocument;
        Tree = result.IsSuccess ? result.Tree : LayoutTree.Empty;
        IsDirty = false;

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Nie udało się wczytać układu pulpitu {DashboardId}: {Error} — {Message}",
                dashboardId,
                result.Error,
                result.Message);
        }

        return result;
    }

    /// <summary>Dzieli liść wskazanej instancji i wstawia nową instancję obok.</summary>
    public LayoutOperationResult Split(PanelInstanceId target, SplitAxis axis, PanelInstanceId newInstance, InsertSide side) =>
        Apply(LayoutOperations.Split(Tree, target, axis, newInstance, side));

    /// <summary>Przenosi liść w strefę upuszczenia innego liścia.</summary>
    public LayoutOperationResult Move(PanelInstanceId instance, PanelInstanceId target, DropZone dropZone) =>
        Apply(LayoutOperations.Move(Tree, instance, target, dropZone));

    /// <summary>Zamienia dwa liście miejscami bez przenoszenia danych.</summary>
    public LayoutOperationResult Swap(PanelInstanceId first, PanelInstanceId second) =>
        Apply(LayoutOperations.Swap(Tree, first, second));

    /// <summary>Wstawia nową instancję w miejsce dotychczasowej.</summary>
    public LayoutOperationResult Replace(PanelInstanceId instance, PanelInstanceId replacement) =>
        Apply(LayoutOperations.Replace(Tree, instance, replacement));

    /// <summary>Zmienia oś podziału, zachowując kolejność dzieci.</summary>
    public LayoutOperationResult ToggleAxis(Guid splitId) =>
        Apply(LayoutOperations.ToggleAxis(Tree, splitId));

    /// <summary>Ustawia proporcję podziału, respektując minima obu poddrzew.</summary>
    public LayoutOperationResult Resize(Guid splitId, double ratio, double regionLength) =>
        Apply(LayoutOperations.Resize(Tree, splitId, ratio, regionLength, MinSizes, Metrics));

    /// <summary>Usuwa liść i kompaktuje podziały z jednym dzieckiem.</summary>
    public LayoutOperationResult Remove(PanelInstanceId instance) =>
        Apply(LayoutOperations.Remove(Tree, instance));

    /// <summary>Przywraca ukryty panel według wskazówki odtworzenia.</summary>
    public LayoutOperationResult Restore(PanelInstanceId instance, RestoreHint hint) =>
        Apply(LayoutOperations.Restore(Tree, instance, hint));

    /// <summary>Buduje wskazówkę odtworzenia dla instancji na podstawie bieżącego układu.</summary>
    public RestoreHint CaptureRestoreHint(PanelInstanceId instance) => RestoreHint.For(Tree, instance);

    /// <summary>Rozwiązuje układ do prostokątów w DIP dla zadanego obszaru.</summary>
    public LayoutSolution Solve(LayoutRect available) =>
        LayoutSolver.Solve(Tree, available, MinSizes, Metrics);

    /// <summary>Waliduje bieżący układ względem zbioru widocznych instancji.</summary>
    public LayoutValidationResult Validate() => LayoutValidator.Validate(Tree, _visibleInstances, Limits);

    /// <summary>
    /// Zapisuje układ, o ile jest brudny (albo gdy wymuszono zapis). Zapis poprzedza
    /// walidacja; niepoprawny układ nie trafia do magazynu.
    /// </summary>
    public async Task<LayoutSaveResult> SaveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!IsDirty && !force)
        {
            return LayoutSaveResult.Ok;
        }

        var validation = Validate();
        if (!validation.IsValid)
        {
            var first = validation.Issues[0];
            _logger.LogWarning(
                "Zapis układu pulpitu {DashboardId} odrzucony przez walidację: {Issues}",
                DashboardId,
                validation.Describe());
            return LayoutSaveResult.Fail(first.Code, validation.Describe());
        }

        await _store.SaveAsync(DashboardId, Tree, cancellationToken).ConfigureAwait(false);
        IsDirty = false;
        return LayoutSaveResult.Ok;
    }

    /// <summary>Przywraca edytor do stanu pustego.</summary>
    public void Reset()
    {
        DashboardId = Guid.Empty;
        Tree = LayoutTree.Empty;
        SchemaVersion = 0;
        IsDirty = false;
        LastLoadError = LayoutErrorCode.None;
        LastLoadMessage = null;
        OriginalDocumentPreserved = false;
        _visibleInstances = [];
    }

    private LayoutOperationResult Apply(LayoutOperationResult result)
    {
        if (!result.IsSuccess)
        {
            _logger.LogDebug(
                "Operacja układu odrzucona: {Error} — {Message}",
                result.Error,
                result.ErrorMessage);
            return result;
        }

        Tree = result.Tree;
        IsDirty = true;
        return result;
    }
}
