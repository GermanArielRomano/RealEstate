using System.ComponentModel.DataAnnotations;
using RealEstate.Domain;

namespace RealEstate.Api.Public;

public enum PublicSort { Newest, PriceAsc, PriceDesc }

public sealed class PublicListQuery
{
    public OperationType? Operation { get; init; }
    public PropertyType? Type { get; init; }
    [StringLength(100)] public string? City { get; init; }
    [StringLength(100)] public string? Search { get; init; }

    // El rango de precio solo se aplica junto con la moneda (no se mezclan USD y ARS)
    public Currency? Currency { get; init; }
    [Range(typeof(decimal), "0", "1000000000000")] public decimal? MinPrice { get; init; }
    [Range(typeof(decimal), "0", "1000000000000")] public decimal? MaxPrice { get; init; }
    [Range(0, 50)] public int? MinBedrooms { get; init; }

    [StringLength(2000)] public string? Ids { get; init; }   // ids separados por coma (favoritos)

    public PublicSort Sort { get; init; } = PublicSort.Newest;
    [Range(1, 100_000)] public int Page { get; init; } = 1;
    [Range(1, 24)] public int PageSize { get; init; } = 12;
}

// Solo datos aptos para el público: sin TenantId, estado, dueño, agente ni fechas internas
public sealed record PublicListItemDto(
    Guid Id, string Title, OperationType Operation, PropertyType Type,
    decimal Price, Currency Currency, string City, string? Province,
    int Bedrooms, int Bathrooms, decimal AreaM2, decimal? CoveredAreaM2, int Rooms, int Garages,
    string AgencyName, string? CoverThumbnailUrl);

public sealed record PublicImageDto(string Url, string ThumbnailUrl);

public sealed record PublicDetailDto(
    Guid Id, string Code, string Title, string? Description,
    OperationType Operation, PropertyType Type, decimal Price, Currency Currency,
    string Street, string City, string? Province, double? Latitude, double? Longitude,
    int Bedrooms, int Bathrooms, int HalfBaths, int Rooms, int Garages,
    decimal AreaM2, decimal? CoveredAreaM2, int? AgeYears,
    string AgencyName, string? AgencyWhatsApp, IReadOnlyList<PublicImageDto> Images);

public sealed record PublicFiltersDto(IReadOnlyList<string> Cities);
