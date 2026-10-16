using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using RealEstate.Infrastructure.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace RealEstate.Api.Public;

/// <summary>
/// Enlaces para compartir (WhatsApp, Instagram, Telegram...). Estas apps NO ejecutan JavaScript: leen las etiquetas
/// Open Graph del HTML para armar la tarjeta con foto y título. Como la app de compradores es una SPA estática,
/// esta página las genera por propiedad y redirige al instante a la app. Los usuarios casi no notan el salto.
/// </summary>
[ApiController]
[EnableRateLimiting("public")]
public sealed class SharePreviewController(
    AppDbContext db, IImageStorage storage, IConfiguration config,
    IHttpClientFactory httpFactory, ILogger<SharePreviewController> logger) : ControllerBase
{
    // ------------------------------------------------------------------
    [HttpGet("/p/{id:guid}")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Page(Guid id, CancellationToken ct)
    {
        var appUrl = config["App:PublicUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(appUrl))
            return Problem(statusCode: 500, title: "Falta configurar App__PublicUrl en el servidor.");

        var row = await FindAsync(id, ct);
        if (row is null) return Redirect($"{appUrl}/");   // ya no está disponible: la app muestra el listado

        var coverId = await CoverIdAsync(id, ct);
        var shareUrl = $"{Request.Scheme}://{Request.Host}/p/{id}";
        var target = $"{appUrl}/?id={id}";

        var title = $"{OperationLabel(row.Operation)} {PriceText(row.Price, row.Currency)} · {row.Title}";
        var description = string.Join(" · ", new[]
        {
            row.City, TypeLabel(row.Type),
            row.Bedrooms > 0 ? $"{row.Bedrooms} dorm." : null,
            row.Bathrooms > 0 ? $"{row.Bathrooms} {(row.Bathrooms == 1 ? "baño" : "baños")}" : null,
            row.AreaM2 > 0 ? $"{row.AreaM2:0.#} m²" : null,
            row.AgencyName
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var image = coverId is null ? "" : $"""
            <meta property="og:image" content="{E($"{shareUrl}/preview.jpg")}">
            <meta property="og:image:width" content="1200">
            <meta property="og:image:height" content="630">
            <meta name="twitter:card" content="summary_large_image">
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(title)}</title>
            <meta property="og:type" content="website">
            <meta property="og:site_name" content="Inmobiliarias">
            <meta property="og:title" content="{E(title)}">
            <meta property="og:description" content="{E(description)}">
            <meta property="og:url" content="{E(shareUrl)}">
            {image}
            <meta http-equiv="refresh" content="0;url={E(target)}">
            </head>
            <body style="font-family:sans-serif;padding:24px">
            <p>Abriendo la propiedad… <a href="{E(target)}">Continuar</a></p>
            <script>location.replace({JsonSerializer.Serialize(target)});</script>
            </body>
            </html>
            """;

        return Content(html, "text/html; charset=utf-8");
    }

    // ------------------------------------------------------------------
    /// <summary>
    /// Portada en JPEG 1200×630. Las fotos se guardan en WebP, formato que no todas las apps muestran en las tarjetas
    /// de enlace; JPEG funciona en todas. Se cachea 1 día.
    /// </summary>
    [HttpGet("/p/{id:guid}/preview.jpg")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var row = await FindAsync(id, ct);
        var coverId = row is null ? null : await CoverIdAsync(id, ct);
        if (row is null || coverId is null) return NotFound();

        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            var bytes = await http.GetByteArrayAsync(storage.GetUrl(ImageKeys.Main(row.TenantId, id, coverId.Value)), ct);
            if (bytes.Length > 8 * 1024 * 1024) return NotFound();

            using var image = Image.Load(bytes);
            image.Mutate(c => c.Resize(new ResizeOptions { Size = new Size(1200, 630), Mode = ResizeMode.Crop }));

            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 82 }, ct);
            return File(ms.ToArray(), "image/jpeg");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo generar la vista previa de la propiedad {PropertyId}", id);
            return NotFound();
        }
    }

    // ------------------------------------------------------------------
    // Solo propiedades DISPONIBLES de inmobiliarias ACTIVAS (igual que el catálogo público)
    private async Task<ShareRow?> FindAsync(Guid id, CancellationToken ct) =>
        await db.Properties.AsNoTracking()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(p => p.Id == id && p.Status == PropertyStatus.Available)
            .Join(db.Tenants.AsNoTracking().IgnoreQueryFilters([AppDbContext.TenantFilter]).Where(t => t.IsActive),
                p => p.TenantId, t => t.Id,
                (p, t) => new ShareRow(p.Id, p.TenantId, p.Title, p.Operation, p.Type, p.Price, p.Currency,
                    p.City, p.Bedrooms, p.Bathrooms, p.AreaM2, t.Name))
            .FirstOrDefaultAsync(ct);

    private Task<Guid?> CoverIdAsync(Guid propertyId, CancellationToken ct) =>
        db.PropertyImages.AsNoTracking()
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(i => i.PropertyId == propertyId)
            .OrderByDescending(i => i.IsCover).ThenBy(i => i.SortOrder)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);

    // Todo texto que viene de los datos se escapa: el título lo escribe cada inmobiliaria
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string PriceText(decimal price, Currency currency)
    {
        var symbol = currency switch { Currency.ARS => "$", Currency.EUR => "€", _ => currency.ToString() };
        var number = price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');
        return $"{symbol} {number}";
    }

    private static string OperationLabel(OperationType o) => o switch
    {
        OperationType.Sale => "Venta",
        OperationType.Rent => "Alquiler",
        _ => "Alquiler temporario"
    };

    private static string TypeLabel(PropertyType t) => t switch
    {
        PropertyType.House => "Casa",
        PropertyType.Apartment => "Departamento",
        PropertyType.Land => "Terreno",
        PropertyType.Office => "Oficina",
        PropertyType.Commercial => "Local comercial",
        _ => "Depósito"
    };

    private sealed record ShareRow(
        Guid Id, Guid TenantId, string Title, OperationType Operation, PropertyType Type,
        decimal Price, Currency Currency, string City, int Bedrooms, int Bathrooms, decimal AreaM2, string AgencyName);
}
