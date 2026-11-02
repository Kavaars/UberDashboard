namespace UberDashboard.Application.Platform;

/// <summary>
/// Prostokąt we współrzędnych pikselowych Win32. Prawy i dolny brzeg są wyłączne,
/// zgodnie z konwencją <c>RECT</c> (<c>right - left</c> to szerokość).
/// Wartości mogą być ujemne — ekran wirtualny rozciąga się na lewo i powyżej zera.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Pusty prostokąt o zerowym rozmiarze.</summary>
    public static PixelRect Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Szerokość w pikselach; może być ujemna dla niepoprawnego prostokąta.</summary>
    public int Width => Right - Left;

    /// <summary>Wysokość w pikselach; może być ujemna dla niepoprawnego prostokąta.</summary>
    public int Height => Bottom - Top;

    /// <summary>Prostokąt ma dodatnią szerokość i wysokość.</summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <summary>Zwraca prostokąt o wskazanej szerokości, zachowując lewy brzeg.</summary>
    public PixelRect WithWidth(int width) => this with { Right = Left + width };

    /// <summary>Zwraca prostokąt o wskazanej wysokości, zachowując górny brzeg.</summary>
    public PixelRect WithHeight(int height) => this with { Bottom = Top + height };
}
