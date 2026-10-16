using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RealEstate.Api.Properties;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using RealEstate.Infrastructure.Storage;

namespace RealEstate.Api.Public;

/// <summary>
/// Catálogo público para compradores: sin autenticación y de solo lectura.
/// Muestra únicamente propiedades DISPONIBLES de inmobiliarias ACTIVAS, de todas las inmobiliarias.
/// </summary>
[ApiController]
[Route("api/public")]
[EnableRateLimiting("public")]
[ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]   // cache de 1 min en navegador/CDN
[Produces("application/json")]
public sealed class PublicPropertiesController(AppDbContext db, IImageStorage storage) : ControllerBase
{
    // Se ignora SOLO el filtro de tenant (aquí se busca en todas las inmobiliarias);
    // el filtro de borrado lógico sigue activo. Los filtros opcionales se aplican ANTES de proyectar.
    private IQueryable<Property> BaseProperties() =>
        db.Properties.AsNoTracking()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(p => p.Status == PropertyStatus.Available);

    private IQueryable<Tenant> ActiveTenants() =>
        db.Tenants.AsNoTracking()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(t => t.IsActive);

    // ------------------------------------------------------------------
    [HttpGet("properties")]
    public async Task<ActionResult<PagedResult<PublicListItemDto>>> List([FromQuery] PublicListQuery q, CancellationToken ct)
    {
        var (props, zoneError) = await ApplyZoneAsync(ApplyFilters(BaseProperties(), q), q, ct);
        if (props is null) return Problem(statusCode: 400, title: zoneError);

        var joined = props.Join(ActiveTenants(), p => p.TenantId, t => t.Id,
            (p, t) => new { p, AgencyName = t.Name });

        var ordered = q.Sort switch
        {
            PublicSort.PriceAsc => joined.OrderBy(x => x.p.Price).ThenBy(x => x.p.Id),
            PublicSort.PriceDesc => joined.OrderByDescending(x => x.p.Price).ThenBy(x => x.p.Id),
            _ => joined.OrderByDescending(x => x.p.CreatedAt).ThenByDescending(x => x.p.Id)
        };

        var total = await joined.CountAsync(ct);

        var rows = await ordered.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(x => new Row(x.p.Id, x.p.TenantId, x.p.Title, x.p.Operation, x.p.Type,
                x.p.Price, x.p.Currency, x.p.City, x.p.Province,
                x.p.Bedrooms, x.p.Bathrooms, x.p.AreaM2, x.p.CoveredAreaM2, x.p.Rooms, x.p.Garages,
                x.AgencyName))
            .ToListAsync(ct);

        // Portadas de la página en una sola consulta
        var pageIds = rows.Select(r => r.Id).ToList();
        var covers = await db.PropertyImages.AsNoTracking()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(i => pageIds.Contains(i.PropertyId))
            .OrderByDescending(i => i.IsCover).ThenBy(i => i.SortOrder)
            .Select(i => new { i.PropertyId, i.Id })
            .ToListAsync(ct);
        var coverByProperty = covers.GroupBy(c => c.PropertyId).ToDictionary(g => g.Key, g => g.First().Id);

        var items = rows.Select(r => new PublicListItemDto(
            r.Id, r.Title, r.Operation, r.Type, r.Price, r.Currency, r.City, r.Province,
            r.Bedrooms, r.Bathrooms, r.AreaM2, r.CoveredAreaM2, r.Rooms, r.Garages, r.AgencyName,
            coverByProperty.TryGetValue(r.Id, out var imageId)
                ? storage.GetUrl(ImageKeys.Thumbnail(r.TenantId, r.Id, imageId))
                : null)).ToList();

        return new PagedResult<PublicListItemDto>(items, q.Page, q.PageSize, total);
    }

    // ------------------------------------------------------------------
    [HttpGet("properties/{id:guid}")]
    public async Task<ActionResult<PublicDetailDto>> Detail(Guid id, CancellationToken ct)
    {
        var x = await BaseProperties().Where(p => p.Id == id)
            .Join(ActiveTenants(), p => p.TenantId, t => t.Id, (p, t) => new { p, t.Name, t.WhatsApp })
            .FirstOrDefaultAsync(ct);

        if (x is null) return NotFound();   // no existe, no está disponible o su inmobiliaria está desactivada

        var imageIds = await db.PropertyImages.AsNoTracking()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(i => i.PropertyId == id)
            .OrderByDescending(i => i.IsCover).ThenBy(i => i.SortOrder)
            .Select(i => i.Id)
            .ToListAsync(ct);

        var p = x.p;
        var images = imageIds.Select(img => new PublicImageDto(
            storage.GetUrl(ImageKeys.Main(p.TenantId, p.Id, img)),
            storage.GetUrl(ImageKeys.Thumbnail(p.TenantId, p.Id, img)))).ToList();

        return new PublicDetailDto(
            p.Id, p.Code, p.Title, p.Description, p.Operation, p.Type, p.Price, p.Currency,
            p.Street, p.City, p.Province, p.Latitude, p.Longitude,
            p.Bedrooms, p.Bathrooms, p.HalfBaths, p.Rooms, p.Garages,
            p.AreaM2, p.CoveredAreaM2, p.AgeYears, x.Name, x.WhatsApp, images);
    }

    // ------------------------------------------------------------------
    [HttpGet("filters")]
    public async Task<PublicFiltersDto> Filters(CancellationToken ct)
    {
        var cities = await BaseProperties()
            .Join(ActiveTenants(), p => p.TenantId, t => t.Id, (p, t) => p.City)
            .Distinct().OrderBy(c => c).Take(200)
            .ToListAsync(ct);

        return new PublicFiltersDto(cities);
    }

    // ------------------------------------------------------------------
    /// <summary>Pines del mapa (máx. 300) con los mismos filtros que el listado. Sin paginar.</summary>
    [HttpGet("map")]
    public async Task<ActionResult<PublicMapDto>> Map([FromQuery] PublicListQuery q, CancellationToken ct)
    {
        var (filtered, zoneError) = await ApplyZoneAsync(ApplyFilters(BaseProperties(), q), q, ct);
        if (filtered is null) return Problem(statusCode: 400, title: zoneError);

        var matching = filtered.Join(ActiveTenants(), p => p.TenantId, t => t.Id, (p, t) => p);
        var located = matching.Where(p => p.Latitude != null && p.Longitude != null);

        var total = await matching.CountAsync(ct);
        var locatedCount = await located.CountAsync(ct);

        var points = await located
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Take(300)
            .Select(p => new PublicMapPointDto(p.Id, p.Latitude!.Value, p.Longitude!.Value, p.Title,
                p.Operation, p.Price, p.Currency, p.Bedrooms, p.AreaM2))
            .ToListAsync(ct);

        return new PublicMapDto(points, total, locatedCount);
    }

    // ------------------------------------------------------------------
    /// <summary>
    /// Zona dibujada: la base de datos descarta rápido todo lo que queda fuera del rectángulo que contiene el dibujo
    /// y aquí se comprueba, punto por punto, cuáles caen realmente dentro. Devuelve la consulta ya acotada a esos ids.
    /// Suficiente para miles de propiedades; si la plataforma crece se reemplaza por PostGIS sin cambiar la API.
    /// </summary>
    private static async Task<(IQueryable<Property>? Props, string? Error)> ApplyZoneAsync(
        IQueryable<Property> props, PublicListQuery q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q.Polygon)) return (props, null);

        var poly = ParsePolygon(q.Polygon);
        if (poly is null) return (null, "La zona dibujada no es válida.");

        var south = poly.Min(v => v.Lat);
        var north = poly.Max(v => v.Lat);
        var west = poly.Min(v => v.Lon);
        var east = poly.Max(v => v.Lon);

        var candidates = await props
            .Where(p => p.Latitude >= south && p.Latitude <= north && p.Longitude >= west && p.Longitude <= east)
            .Select(p => new { p.Id, Lat = p.Latitude!.Value, Lon = p.Longitude!.Value })
            .Take(5000)
            .ToListAsync(ct);

        var ids = candidates.Where(c => Inside(poly, c.Lat, c.Lon)).Select(c => c.Id).ToList();
        return (props.Where(p => ids.Contains(p.Id)), null);
    }

    private static List<(double Lat, double Lon)>? ParsePolygon(string raw)
    {
        var vertices = new List<(double, double)>();
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var xy = part.Split(',');
            if (xy.Length != 2 ||
                !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
                lat is < -90 or > 90 || lon is < -180 or > 180)
                return null;

            vertices.Add((lat, lon));
        }

        return vertices.Count is >= 3 and <= 100 ? vertices : null;
    }

    /// <summary>Punto dentro de polígono (trazado de rayos).</summary>
    private static bool Inside(List<(double Lat, double Lon)> poly, double lat, double lon)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (yi, xi) = poly[i];
            var (yj, xj) = poly[j];
            if ((yi > lat) != (yj > lat) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    // ------------------------------------------------------------------
    /// <summary>Filtros comunes del listado y del mapa. Se aplican ANTES de proyectar.</summary>
    private static IQueryable<Property> ApplyFilters(IQueryable<Property> props, PublicListQuery q)
    {
        if (q.Operation is { } op) props = props.Where(p => p.Operation == op);
        if (q.Type is { } type) props = props.Where(p => p.Type == type);
        if (q.MinBedrooms is { } beds) props = props.Where(p => p.Bedrooms >= beds);

        if (!string.IsNullOrWhiteSpace(q.City))
        {
            var city = EscapeLike(q.City.Trim());
            props = props.Where(p => EF.Functions.ILike(p.City, city));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = $"%{EscapeLike(q.Search.Trim())}%";
            props = props.Where(p => EF.Functions.ILike(p.Title, pattern) ||
                                     EF.Functions.ILike(p.City, pattern) ||
                                     EF.Functions.ILike(p.Street, pattern));
        }

        if (q.Currency is { } cur)
        {
            props = props.Where(p => p.Currency == cur);
            if (q.MinPrice is { } min) props = props.Where(p => p.Price >= min);
            if (q.MaxPrice is { } max) props = props.Where(p => p.Price <= max);
        }

        if (ParseIds(q.Ids) is { } ids)
            props = props.Where(p => ids.Contains(p.Id));

        // Zona del mapa: las propiedades sin coordenadas quedan fuera automáticamente
        if (q.MinLat is { } south && q.MaxLat is { } north && q.MinLon is { } west && q.MaxLon is { } east)
            props = props.Where(p => p.Latitude >= south && p.Latitude <= north &&
                                     p.Longitude >= west && p.Longitude <= east);

        return props;
    }

    // ------------------------------------------------------------------
    private static List<Guid>? ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .Distinct().Take(50).ToList();
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed record Row(
        Guid Id, Guid TenantId, string Title, OperationType Operation, PropertyType Type,
        decimal Price, Currency Currency, string City, string? Province,
        int Bedrooms, int Bathrooms, decimal AreaM2, decimal? CoveredAreaM2, int Rooms, int Garages,
        string AgencyName);
}
