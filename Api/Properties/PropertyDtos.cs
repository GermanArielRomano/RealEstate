using System.ComponentModel.DataAnnotations;
using RealEstate.Domain;

namespace RealEstate.Api.Properties;

// ---------- Entrada ----------

/// <summary>Filtros de listado. El tenant NO se recibe: sale del JWT.</summary>
public sealed class PropertyListQuery
{
    public PropertyStatus? Status { get; init; }
    public OperationType? Operation { get; init; }
    public PropertyType? Type { get; init; }
    public Currency? Currency { get; init; }

    [StringLength(100)] public string? City { get; init; }
    [StringLength(100)] public string? Search { get; init; }   // título, código o calle

    [Range(typeof(decimal), "0", "1000000000000")] public decimal? MinPrice { get; init; }
    [Range(typeof(decimal), "0", "1000000000000")] public decimal? MaxPrice { get; init; }
    [Range(0, 50)] public int? MinBedrooms { get; init; }
    public Guid? AssignedAgentId { get; init; }

    public PropertySort Sort { get; init; } = PropertySort.Newest;

    [Range(1, 100_000)] public int Page { get; init; } = 1;
    [Range(1, 50)] public int PageSize { get; init; } = 20;
}

public enum PropertySort { Newest, PriceAsc, PriceDesc }

/// <summary>
/// Se usa para crear y editar. Nunca se enlaza directo a la entidad
/// (evita "mass assignment" de TenantId, IsDeleted, etc.).
/// </summary>
public sealed class SavePropertyRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = default!;
    [Required, StringLength(200)] public string Title { get; init; } = default!;
    [StringLength(5000)] public string? Description { get; init; }

    [EnumDataType(typeof(OperationType))] public OperationType Operation { get; init; }
    [EnumDataType(typeof(PropertyType))] public PropertyType Type { get; init; }
    [EnumDataType(typeof(PropertyStatus))] public PropertyStatus Status { get; init; } = PropertyStatus.Draft;

    [Range(typeof(decimal), "0", "1000000000000")] public decimal Price { get; init; }
    [EnumDataType(typeof(Currency))] public Currency Currency { get; init; }

    [Required, StringLength(250)] public string Street { get; init; } = default!;
    [Required, StringLength(100)] public string City { get; init; } = default!;
    [StringLength(100)] public string? Province { get; init; }
    [Range(-90, 90)] public double? Latitude { get; init; }
    [Range(-180, 180)] public double? Longitude { get; init; }

    [Range(0, 50)] public int Bedrooms { get; init; }
    [Range(0, 50)] public int Bathrooms { get; init; }
    [Range(typeof(decimal), "0", "1000000")] public decimal AreaM2 { get; init; }
    [Range(typeof(decimal), "0", "1000000")] public decimal? CoveredAreaM2 { get; init; }
    [Range(0, 50)] public int Rooms { get; init; }
    [Range(0, 20)] public int Garages { get; init; }
    [Range(0, 20)] public int HalfBaths { get; init; }
    [Range(0, 200)] public int? AgeYears { get; init; }

    public Guid? OwnerId { get; init; }          // Contact del mismo tenant
    public Guid? AssignedAgentId { get; init; }  // AppUser del mismo tenant
}

// ---------- Salida ----------

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record PropertyListItemDto(
    Guid Id, string Code, string Title,
    OperationType Operation, PropertyType Type, PropertyStatus Status,
    decimal Price, Currency Currency, string City,
    int Bedrooms, int Bathrooms, decimal AreaM2, int Rooms, int Garages,
    string? CoverThumbnailUrl);

public sealed record PropertyImageDto(Guid Id, string Url, string ThumbnailUrl, int SortOrder, bool IsCover);

public sealed record PropertyDetailDto(
    Guid Id, string Code, string Title, string? Description,
    OperationType Operation, PropertyType Type, PropertyStatus Status,
    decimal Price, Currency Currency,
    string Street, string City, string? Province, double? Latitude, double? Longitude,
    int Bedrooms, int Bathrooms, decimal AreaM2,
    decimal? CoveredAreaM2, int Rooms, int Garages, int HalfBaths, int? AgeYears,
    Guid? OwnerId, Guid? AssignedAgentId,
    IReadOnlyList<PropertyImageDto> Images,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record UploadFailureDto(string FileName, string Reason);

public sealed record UploadImagesResult(
    IReadOnlyList<PropertyImageDto> Uploaded,
    IReadOnlyList<UploadFailureDto> Failed);
