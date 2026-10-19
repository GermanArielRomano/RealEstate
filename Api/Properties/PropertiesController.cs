using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using RealEstate.Infrastructure.Imaging;
using RealEstate.Infrastructure.Storage;

namespace RealEstate.Api.Properties;

[ApiController]
[Route("api/properties")]
[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.TenantAdmin},{Roles.Agent}")]
[Produces("application/json")]
public sealed class PropertiesController(
    AppDbContext db,
    ICurrentTenant tenant,
    IImageStorage storage,
    IImageOptimizer optimizer,
    IOptions<ImageOptions> imageOptions,
    ILogger<PropertiesController> logger) : ControllerBase
{
    private const long MaxUploadBytes = 60L * 1024 * 1024; // tope de la request completa
    private readonly ImageOptions _img = imageOptions.Value;

    // =====================================================================
    //  LISTAR  (el filtro global de tenant + soft delete se aplica solo)
    // =====================================================================
    [HttpGet]
    public async Task<ActionResult<PagedResult<PropertyListItemDto>>> List(
        [FromQuery] PropertyListQuery q, CancellationToken ct)
    {
        var query = db.Properties.AsNoTracking();

        if (q.Status is { } status) query = query.Where(p => p.Status == status);
        if (q.Operation is { } op) query = query.Where(p => p.Operation == op);
        if (q.Type is { } type) query = query.Where(p => p.Type == type);
        if (q.Currency is { } cur) query = query.Where(p => p.Currency == cur);
        if (q.MinPrice is { } min) query = query.Where(p => p.Price >= min);
        if (q.MaxPrice is { } max) query = query.Where(p => p.Price <= max);
        if (q.MinBedrooms is { } beds) query = query.Where(p => p.Bedrooms >= beds);
        if (q.AssignedAgentId is { } agent) query = query.Where(p => p.AssignedAgentId == agent);

        // PostgreSQL distingue mayúsculas de minúsculas: ILike hace la búsqueda insensible
        if (!string.IsNullOrWhiteSpace(q.City))
        {
            var city = EscapeLike(q.City.Trim());
            query = query.Where(p => EF.Functions.ILike(p.City, city));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = $"%{EscapeLike(q.Search.Trim())}%";
            query = query.Where(p => EF.Functions.ILike(p.Title, pattern) ||
                                     EF.Functions.ILike(p.Code, pattern) ||
                                     EF.Functions.ILike(p.Street, pattern));
        }

        // Orden estable (desempate por Id) para que la paginación no repita ni se salte filas
        query = q.Sort switch
        {
            PropertySort.PriceAsc => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            PropertySort.PriceDesc => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
        };

        var total = await query.CountAsync(ct);

        var rows = await query
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .Select(p => new ListRow(
                p.Id, p.TenantId, p.Code, p.Title, p.Operation, p.Type, p.Status,
                p.Price, p.Currency, p.City, p.Bedrooms, p.Bathrooms, p.AreaM2, p.Rooms, p.Garages,
                p.Images.OrderByDescending(i => i.IsCover).ThenBy(i => i.SortOrder)
                        .Select(i => (Guid?)i.Id).FirstOrDefault()))
            .ToListAsync(ct);

        var items = rows.Select(r => new PropertyListItemDto(
            r.Id, r.Code, r.Title, r.Operation, r.Type, r.Status,
            r.Price, r.Currency, r.City, r.Bedrooms, r.Bathrooms, r.AreaM2, r.Rooms, r.Garages,
            r.CoverImageId is { } coverId
                ? storage.GetUrl(ImageKeys.Thumbnail(r.TenantId, r.Id, coverId))
                : null)).ToList();

        return new PagedResult<PropertyListItemDto>(items, q.Page, q.PageSize, total);
    }

    // =====================================================================
    //  DETALLE
    // =====================================================================
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PropertyDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var property = await db.Properties.AsNoTracking()
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return property is null ? NotFound() : ToDetail(property);
    }

    // =====================================================================
    //  CREAR
    // =====================================================================
    /// <param name="tenantId">Solo lo respeta el SuperAdmin (no tiene tenant propio en el JWT).</param>
    [HttpPost]
    [ProducesResponseType<PropertyDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] SavePropertyRequest req, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var tid = tenant.TenantId ?? (tenant.IsSuperAdmin ? tenantId : null);
        if (tid is null)
            return Problem(statusCode: 400, title: "Falta el tenant",
                detail: "El SuperAdmin debe indicar ?tenantId=<guid>.");

        if (!await db.Tenants.AnyAsync(t => t.Id == tid.Value && t.IsActive, ct))
            return Problem(statusCode: 403, title: "Inmobiliaria inexistente o inactiva.");

        var code = NormalizeCode(req.Code);
        if (await CodeExistsAsync(tid.Value, code, excludeId: null, ct))
            return CodeConflict();

        await ValidateReferencesAsync(req, tid.Value, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var entity = new Property { TenantId = tid.Value };
        Apply(entity, req);

        // Un agente que crea una propiedad sin asignar queda como responsable
        if (entity.AssignedAgentId is null && User.IsInRole(Roles.Agent) && CurrentUserId is { } uid)
            entity.AssignedAgentId = uid;

        db.Properties.Add(entity);
        db.PriceChanges.Add(new PriceChange   // precio inicial: base del historial
        {
            TenantId = tid.Value, PropertyId = entity.Id, OldPrice = null, NewPrice = entity.Price, Currency = entity.Currency
        });

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return CodeConflict(); }

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDetail(entity));
    }

    // =====================================================================
    //  EDITAR
    // =====================================================================
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SavePropertyRequest req, CancellationToken ct)
    {
        var entity = await db.Properties.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound();
        if (!CanManage(entity)) return Forbid();

        var code = NormalizeCode(req.Code);
        if (code != entity.Code && await CodeExistsAsync(entity.TenantId, code, entity.Id, ct))
            return CodeConflict();

        // Se valida contra el tenant DE LA PROPIEDAD (correcto también para el SuperAdmin)
        await ValidateReferencesAsync(req, entity.TenantId, ct);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Historial: se registra solo si cambió el precio o la moneda. Con otra moneda no hay "antes" comparable.
        if (entity.Price != req.Price || entity.Currency != req.Currency)
            db.PriceChanges.Add(new PriceChange
            {
                TenantId = entity.TenantId,
                PropertyId = entity.Id,
                OldPrice = entity.Currency == req.Currency ? entity.Price : null,
                NewPrice = req.Price,
                Currency = req.Currency
            });

        Apply(entity, req);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return CodeConflict(); }

        return Ok(ToDetail(entity));
    }

    // =====================================================================
    //  ELIMINAR (borrado lógico: SaveChanges convierte DELETE en UPDATE)
    // =====================================================================
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await db.Properties.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound();
        if (!CanManage(entity)) return Forbid();

        db.Properties.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // =====================================================================
    //  IMÁGENES: SUBIR (multipart/form-data, campo "files", varias a la vez)
    // =====================================================================
    [HttpPost("{id:guid}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType<UploadImagesResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<UploadImagesResult>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadImages(Guid id, [FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files.Count == 0)
            return Problem(statusCode: 400, title: "No se envió ningún archivo (campo 'files').");
        if (files.Count > _img.MaxFilesPerRequest)
            return Problem(statusCode: 400, title: $"Máximo {_img.MaxFilesPerRequest} imágenes por solicitud.");

        var property = await db.Properties.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();
        if (!CanManage(property)) return Forbid();

        if (property.Images.Count + files.Count > _img.MaxImagesPerProperty)
            return Problem(statusCode: 409,
                title: $"Una propiedad admite hasta {_img.MaxImagesPerProperty} imágenes.");

        var nextOrder = property.Images.Select(i => i.SortOrder).DefaultIfEmpty(-1).Max() + 1;
        var hasCover = property.Images.Any(i => i.IsCover);

        var created = new List<PropertyImage>();
        var failed = new List<UploadFailureDto>();
        var uploadedKeys = new List<string>(); // para compensar si falla la BD

        try
        {
            foreach (var file in files)
            {
                var name = Path.GetFileName(file.FileName);

                if (file.Length == 0 || file.Length > _img.MaxFileSizeBytes)
                {
                    failed.Add(new(name, $"El archivo debe pesar entre 1 byte y {_img.MaxFileSizeBytes / 1024 / 1024} MB."));
                    continue;
                }

                OptimizedImage optimized;
                try
                {
                    await using var stream = file.OpenReadStream();
                    optimized = await optimizer.OptimizeAsync(stream, ct);
                }
                catch (InvalidImageException ex)
                {
                    failed.Add(new(name, ex.Message));
                    continue;
                }

                var imageId = Guid.CreateVersion7();
                var mainKey = ImageKeys.Main(property.TenantId, property.Id, imageId);
                var thumbKey = ImageKeys.Thumbnail(property.TenantId, property.Id, imageId);

                await storage.SaveAsync(mainKey, optimized.Main, ImageKeys.ContentType, ct);
                uploadedKeys.Add(mainKey);
                await storage.SaveAsync(thumbKey, optimized.Thumbnail, ImageKeys.ContentType, ct);
                uploadedKeys.Add(thumbKey);

                var image = new PropertyImage
                {
                    Id = imageId,
                    TenantId = property.TenantId,
                    PropertyId = property.Id,
                    Url = storage.GetUrl(mainKey),
                    SortOrder = nextOrder++,
                    IsCover = !hasCover // la primera imagen pasa a ser la portada
                };
                hasCover = true;

                db.PropertyImages.Add(image);
                created.Add(image);
            }

            if (created.Count > 0)
                await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
        {
            // Se responde con un error controlado: un 500 sin manejar llega al navegador sin encabezados CORS
            // y se ve como "error de CORS" en lugar del mensaje real.
            await TryDeleteFilesAsync(uploadedKeys);
            logger.LogError(ex, "Falló la subida de imágenes de la propiedad {PropertyId}", id);
            return Problem(statusCode: 502, title: "No se pudo guardar la imagen", detail: ex.Message);
        }
        catch
        {
            await TryDeleteFilesAsync(uploadedKeys); // no dejar archivos huérfanos
            throw;
        }

        var result = new UploadImagesResult(created.Select(i => ToImageDto(property, i)).ToList(), failed);

        return created.Count == 0
            ? BadRequest(result)
            : CreatedAtAction(nameof(GetById), new { id }, result);
    }

    // =====================================================================
    //  IMÁGENES: MARCAR PORTADA
    // =====================================================================
    [HttpPut("{id:guid}/images/{imageId:guid}/cover")]
    public async Task<IActionResult> SetCover(Guid id, Guid imageId, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();
        if (!CanManage(property)) return Forbid();
        if (property.Images.All(i => i.Id != imageId)) return NotFound();

        foreach (var image in property.Images)
            image.IsCover = image.Id == imageId;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // =====================================================================
    //  IMÁGENES: ELIMINAR
    // =====================================================================
    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();
        if (!CanManage(property)) return Forbid();

        var target = property.Images.FirstOrDefault(i => i.Id == imageId);
        if (target is null) return NotFound();

        // Si era la portada, promueve la siguiente
        if (target.IsCover)
        {
            target.IsCover = false;
            property.Images.Where(i => i.Id != imageId).OrderBy(i => i.SortOrder)
                .FirstOrDefault()?.IsCover = true;
        }

        db.PropertyImages.Remove(target); // borrado lógico en BD
        await db.SaveChangesAsync(ct);

        // Archivos: se borran después del commit (si falla, solo queda un archivo huérfano, no un enlace roto)
        await TryDeleteFilesAsync([
            ImageKeys.Main(property.TenantId, property.Id, target.Id),
            ImageKeys.Thumbnail(property.TenantId, property.Id, target.Id)
        ]);

        return NoContent();
    }

    // =====================================================================
    //  Helpers
    // =====================================================================
    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
            ? id : null;

    /// <summary>Admins gestionan todo su tenant; un agente solo las propiedades que tiene asignadas.</summary>
    private bool CanManage(Property p) =>
        User.IsInRole(Roles.SuperAdmin) ||
        User.IsInRole(Roles.TenantAdmin) ||
        (p.AssignedAgentId is not null && p.AssignedAgentId == CurrentUserId);

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    /// <summary>
    /// Incluye propiedades borradas lógicamente: el índice único (TenantId, Code) también las cuenta.
    /// Filtra por TenantId explícito porque el SuperAdmin ve todos los tenants.
    /// </summary>
    private Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) =>
        db.Properties
            .IgnoreQueryFilters([AppDbContext.SoftDeleteFilter])
            .AnyAsync(p => p.TenantId == tenantId && p.Code == code &&
                           (excludeId == null || p.Id != excludeId), ct);

    /// <summary>
    /// Evita referencias a datos de otro tenant. Contacts pasa por el filtro global; AppUser NO tiene
    /// filtro (Identity lo necesita en el login), por eso se compara el TenantId a mano.
    /// </summary>
    private async Task ValidateReferencesAsync(SavePropertyRequest r, Guid tenantId, CancellationToken ct)
    {
        if (r.OwnerId is { } ownerId &&
            !await db.Contacts.AnyAsync(c => c.Id == ownerId && c.TenantId == tenantId, ct))
            ModelState.AddModelError("ownerId", "El propietario no existe.");

        if (r.AssignedAgentId is { } agentId &&
            !await db.Users.AnyAsync(u => u.Id == agentId && u.TenantId == tenantId && u.IsActive, ct))
            ModelState.AddModelError("assignedAgentId", "El agente no existe o no está activo.");
    }

    private static void Apply(Property p, SavePropertyRequest r)
    {
        p.Code = NormalizeCode(r.Code);
        p.Title = r.Title.Trim();
        p.Description = r.Description?.Trim();
        p.Operation = r.Operation;
        p.Type = r.Type;
        p.Status = r.Status;
        p.Price = r.Price;
        p.Currency = r.Currency;
        p.Street = r.Street.Trim();
        p.City = r.City.Trim();
        p.Province = r.Province?.Trim();
        p.Latitude = r.Latitude;
        p.Longitude = r.Longitude;
        p.Bedrooms = r.Bedrooms;
        p.Bathrooms = r.Bathrooms;
        p.AreaM2 = r.AreaM2;
        p.CoveredAreaM2 = r.CoveredAreaM2;
        p.Rooms = r.Rooms;
        p.Garages = r.Garages;
        p.HalfBaths = r.HalfBaths;
        p.AgeYears = r.AgeYears;
        p.OwnerId = r.OwnerId;
        p.AssignedAgentId = r.AssignedAgentId;
    }

    private PropertyImageDto ToImageDto(Property p, PropertyImage i) =>
        new(i.Id, i.Url, storage.GetUrl(ImageKeys.Thumbnail(p.TenantId, p.Id, i.Id)), i.SortOrder, i.IsCover);

    private PropertyDetailDto ToDetail(Property p) => new(
        p.Id, p.Code, p.Title, p.Description,
        p.Operation, p.Type, p.Status, p.Price, p.Currency,
        p.Street, p.City, p.Province, p.Latitude, p.Longitude,
        p.Bedrooms, p.Bathrooms, p.AreaM2,
        p.CoveredAreaM2, p.Rooms, p.Garages, p.HalfBaths, p.AgeYears,
        p.OwnerId, p.AssignedAgentId,
        p.Images.OrderBy(i => i.SortOrder).Select(i => ToImageDto(p, i)).ToList(),
        p.CreatedAt, p.UpdatedAt);

    private ObjectResult CodeConflict() =>
        Problem(statusCode: 409, title: "Código duplicado",
            detail: "Ya existe una propiedad con ese código en tu inmobiliaria (incluye las eliminadas).");

    private static bool IsUniqueViolation(DbUpdateException ex) => DbErrors.IsUniqueViolation(ex);

    /// <summary>Escapa los comodines de LIKE (% _ \) para que el texto del usuario se busque literalmente.</summary>
    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private async Task TryDeleteFilesAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try { await storage.DeleteAsync(key, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "No se pudo borrar el archivo {Key}", key); }
        }
    }

    private sealed record ListRow(
        Guid Id, Guid TenantId, string Code, string Title,
        OperationType Operation, PropertyType Type, PropertyStatus Status,
        decimal Price, Currency Currency, string City,
        int Bedrooms, int Bathrooms, decimal AreaM2, int Rooms, int Garages, Guid? CoverImageId);
}
