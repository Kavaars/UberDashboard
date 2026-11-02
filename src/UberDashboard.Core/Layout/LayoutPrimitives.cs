namespace UberDashboard.Core.Layout;

/// <summary>Oś podziału drzewa układu.</summary>
public enum SplitAxis
{
    /// <summary>Podział poziomy: pierwsze dziecko po lewej, drugie po prawej.</summary>
    Horizontal = 0,

    /// <summary>Podział pionowy: pierwsze dziecko u góry, drugie u dołu.</summary>
    Vertical = 1,
}

/// <summary>Strona wstawienia nowego liścia względem istniejącego.</summary>
public enum InsertSide
{
    /// <summary>Nowy liść zajmuje miejsce pierwszego dziecka (lewo lub góra).</summary>
    Before = 0,

    /// <summary>Nowy liść zajmuje miejsce drugiego dziecka (prawo lub dół).</summary>
    After = 1,
}

/// <summary>Strefa upuszczenia przy przeciąganiu liścia na inny panel.</summary>
public enum DropZone
{
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3,
}

/// <summary>Minimalny rozmiar wyrażony w DIP.</summary>
public readonly record struct MinSize(double Width, double Height)
{
    public static MinSize Zero { get; } = new(0d, 0d);

    /// <summary>Składowa właściwa dla osi podziału.</summary>
    public double Along(SplitAxis axis) => axis == SplitAxis.Horizontal ? Width : Height;

    /// <summary>Większy z dwóch minimów, liczony niezależnie dla każdej osi.</summary>
    public MinSize CombineWith(MinSize other) =>
        new(Math.Max(Width, other.Width), Math.Max(Height, other.Height));
}

/// <summary>Stałe geometrii układu wyrażone w DIP.</summary>
public readonly record struct LayoutMetrics(double SplitterThickness)
{
    /// <summary>Domyślna grubość dzielnika.</summary>
    public static LayoutMetrics Default { get; } = new(6d);

    /// <summary>Wariant bez dzielnika — używany w obliczeniach czystej domeny.</summary>
    public static LayoutMetrics None { get; } = new(0d);
}

/// <summary>Prostokąt w DIP, niezależny od WPF i Win32.</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public static LayoutRect Empty { get; } = new(0d, 0d, 0d, 0d);

    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>Długość krawędzi właściwej dla osi podziału.</summary>
    public double LengthAlong(SplitAxis axis) => axis == SplitAxis.Horizontal ? Width : Height;

    /// <summary>Dzieli prostokąt wzdłuż osi na część o wskazanej długości.</summary>
    public (LayoutRect First, LayoutRect Second, LayoutRect Splitter) SplitAlong(SplitAxis axis, double firstLength, double splitterThickness)
    {
        var secondLength = Math.Max(0d, LengthAlong(axis) - firstLength - splitterThickness);

        if (axis == SplitAxis.Horizontal)
        {
            var first = new LayoutRect(X, Y, firstLength, Height);
            var splitter = new LayoutRect(X + firstLength, Y, splitterThickness, Height);
            var second = new LayoutRect(X + firstLength + splitterThickness, Y, secondLength, Height);
            return (first, second, splitter);
        }

        var firstVertical = new LayoutRect(X, Y, Width, firstLength);
        var splitterVertical = new LayoutRect(X, Y + firstLength, Width, splitterThickness);
        var secondVertical = new LayoutRect(X, Y + firstLength + splitterThickness, Width, secondLength);
        return (firstVertical, secondVertical, splitterVertical);
    }
}

/// <summary>Kody błędów walidacji i operacji układu.</summary>
public enum LayoutErrorCode
{
    None = 0,

    /// <summary>Operacja nie może się wykonać, ale układ pozostaje poprawny.</summary>
    InvalidOperation = 1,

    /// <summary>Nie znaleziono liścia wskazywanej instancji.</summary>
    LeafNotFound = 2,

    /// <summary>Nie znaleziono podziału o wskazanym identyfikatorze.</summary>
    SplitNotFound = 3,

    /// <summary>Instancja występuje w układzie więcej niż raz.</summary>
    DuplicateInstance = 4,

    /// <summary>Liść wskazuje instancję spoza zbioru widocznych instancji.</summary>
    UnknownInstance = 5,

    /// <summary>Próba przeniesienia liścia na samego siebie lub do własnego poddrzewa.</summary>
    SelfMove = 6,

    /// <summary>Proporcja jest niepoprawna: NaN, nieskończoność lub wartość poza (0; 1).</summary>
    InvalidRatio = 7,

    /// <summary>Identyfikator podziału jest pusty lub zduplikowany.</summary>
    InvalidSplitId = 8,

    /// <summary>Węzeł podziału nie ma dwójki dzieci.</summary>
    MissingChild = 9,

    /// <summary>Dokument układu przekracza limit głębokości.</summary>
    DepthLimitExceeded = 10,

    /// <summary>Dokument układu przekracza limit liczby węzłów.</summary>
    NodeLimitExceeded = 11,

    /// <summary>Dokument ma nieznaną wersję schematu — oryginał musi zostać zachowany.</summary>
    UnknownSchemaVersion = 12,

    /// <summary>Dokumentu nie można sparsować lub ma niepoprawny kształt.</summary>
    MalformedDocument = 13,

    /// <summary>Niepusty układ wymaga dokładnie jednego korzenia.</summary>
    MissingRoot = 14,

    /// <summary>Pusty układ ma korzeń albo niepusty układ nie ma korzenia.</summary>
    InconsistentEmptyState = 15,

    /// <summary>Dostępny obszar jest mniejszy niż suma minimów; operacja nie może zostać zapisana.</summary>
    InsufficientSpace = 16,

    /// <summary>Wskazówka odtworzenia jest niepoprawna i wymaga użycia pozycji zapasowej.</summary>
    RestoreHintInvalid = 17,
}

/// <summary>Limity chroniące parser i walidator przed dokumentami o patologicznym kształcie.</summary>
public readonly record struct LayoutLimits(int MaxNodes, int MaxDepth)
{
    /// <summary>Domyślne limity: 512 węzłów i 64 poziomy.</summary>
    public static LayoutLimits Default { get; } = new(512, 64);
}
