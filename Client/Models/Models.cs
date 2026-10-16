namespace RealEstate.Client.Models;

// Espejo de los DTOs públicos de la API; los enums llegan como string
public sealed record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record PublicListItem(
    Guid Id, string Title, string Operation, string Type,
    decimal Price, string Currency, string City, string? Province,
    int Bedrooms, int Bathrooms, decimal AreaM2, decimal? CoveredAreaM2, int Rooms, int Garages,
    string AgencyName, string? CoverThumbnailUrl);

public sealed record PublicImage(string Url, string ThumbnailUrl);

public sealed record PublicDetail(
    Guid Id, string Code, string Title, string? Description,
    string Operation, string Type, decimal Price, string Currency,
    string Street, string City, string? Province, double? Latitude, double? Longitude,
    int Bedrooms, int Bathrooms, int HalfBaths, int Rooms, int Garages,
    decimal AreaM2, decimal? CoveredAreaM2, int? AgeYears,
    string AgencyName, string? AgencyWhatsApp, List<PublicImage> Images);

public sealed record PublicFilters(List<string> Cities);

public sealed class SearchFilter
{
    public string? Search { get; set; }
    public string? Operation { get; set; }
    public string? Type { get; set; }
    public string? City { get; set; }
    public int? MinBedrooms { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string Sort { get; set; } = "Newest";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public static class Catalog
{
    public static readonly (string Value, string Label)[] Operations =
        [("Sale", "Venta"), ("Rent", "Alquiler"), ("TemporaryRent", "Alquiler temporario")];

    public static readonly (string Value, string Label)[] Types =
        [("House", "Casa"), ("Apartment", "Departamento"), ("Land", "Terreno"), ("Office", "Oficina"),
         ("Commercial", "Local comercial"), ("Warehouse", "Depósito")];

    public static readonly (string Value, string Label)[] Currencies = [("USD", "USD"), ("ARS", "ARS ($)")];

    public static readonly (string Value, string Label)[] Sorts =
        [("Newest", "Más recientes"), ("PriceAsc", "Menor precio"), ("PriceDesc", "Mayor precio")];

    public static string Label((string Value, string Label)[] list, string? value) =>
        list.FirstOrDefault(x => x.Value == value).Label ?? value ?? "";

    /// <summary>Sin CultureInfo (InvariantGlobalization): 98000 => "USD 98.000"; pesos => "$ 320.000".</summary>
    public static string Price(decimal price, string currency)
    {
        var symbol = currency switch { "ARS" => "$", "EUR" => "€", _ => currency };
        var number = price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');
        return $"{symbol} {number}";
    }

    /// <summary>"Venta USD 98.000" como en los portales.</summary>
    public static string Headline(string operation, decimal price, string currency) =>
        $"{Label(Operations, operation)} {Price(price, currency)}";

    public static string? AgeText(int? years) => years switch
    {
        null => null,
        0 => "A estrenar",
        1 => "1 año",
        var n => $"{n} años"
    };
}
