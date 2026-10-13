using System.Globalization;

namespace RealEstate.Mobile.Models;

// ---------- Contratos de la API (espejo de PropertyListItemDto / PagedResult del backend) ----------
// Operation, Currency, etc. llegan como string: si el backend agrega un valor nuevo, la app no se rompe.

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record PropertyListItemDto(
    Guid Id, string Code, string Title,
    string Operation, string Type, string Status,
    decimal Price, string Currency, string City,
    int Bedrooms, int Bathrooms, decimal AreaM2,
    string? CoverThumbnailUrl);

public static class AppRoutes
{
    public const string PropertyDetail = "propertydetail"; // Routing.RegisterRoute(AppRoutes.PropertyDetail, typeof(PropertyDetailPage))
}

// ---------- Item que consume la tarjeta: toda la lógica de presentación vive aquí, no en el XAML ----------

public sealed class PropertyCardItem(PropertyListItemDto dto)
{
    private static readonly CultureInfo Culture = new("es-AR");

    public Guid Id => dto.Id;
    public string Title => dto.Title;
    public string LocationText => dto.City;

    public string? ImageUrl => dto.CoverThumbnailUrl;
    public bool HasImage => !string.IsNullOrWhiteSpace(dto.CoverThumbnailUrl);
    public bool HasNoImage => !HasImage;

    public string PriceText => $"{CurrencySymbol(dto.Currency)} {dto.Price.ToString("N0", Culture)}";

    public string OperationText => dto.Operation switch
    {
        "Sale" => "Venta",
        "Rent" => "Alquiler",
        "TemporaryRent" => "Temporario",
        _ => dto.Operation
    };

    public string SpecsText
    {
        get
        {
            var parts = new List<string>(3);
            if (dto.Bedrooms > 0) parts.Add($"{dto.Bedrooms} dorm.");
            if (dto.Bathrooms > 0) parts.Add($"{dto.Bathrooms} baños");
            if (dto.AreaM2 > 0) parts.Add($"{dto.AreaM2.ToString("0.#", Culture)} m²");
            return string.Join(" · ", parts);
        }
    }

    private static string CurrencySymbol(string currency) => currency switch
    {
        "ARS" => "$",
        "USD" => "US$",
        "EUR" => "€",
        _ => currency
    };
}
