using System.ComponentModel.DataAnnotations;

namespace RealEstate.Panel.Models;

// ---------- Sesión ----------
public sealed record UserInfo(Guid Id, string Email, string FullName, string[] Roles, Guid? TenantId, string? TenantName);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserInfo User);
public sealed record Session(string Token, DateTimeOffset ExpiresAt, UserInfo User);

// ---------- Propiedades (espejo de los DTOs de la API; los enums llegan como string) ----------
public sealed record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record PropertyListItem(
    Guid Id, string Code, string Title, string Operation, string Type, string Status,
    decimal Price, string Currency, string City, int Bedrooms, int Bathrooms, decimal AreaM2,
    int Rooms, int Garages, string? CoverThumbnailUrl);

public sealed record PropertyImage(Guid Id, string Url, string ThumbnailUrl, int SortOrder, bool IsCover);

public sealed record PropertyDetail(
    Guid Id, string Code, string Title, string? Description,
    string Operation, string Type, string Status, decimal Price, string Currency,
    string Street, string City, string? Province, double? Latitude, double? Longitude,
    int Bedrooms, int Bathrooms, decimal AreaM2,
    decimal? CoveredAreaM2, int Rooms, int Garages, int HalfBaths, int? AgeYears,
    Guid? OwnerId, Guid? AssignedAgentId,
    List<PropertyImage> Images, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record LeadItem(
    Guid Id, DateTimeOffset CreatedAt, string Status, string? Message,
    Guid PropertyId, string PropertyCode, string PropertyTitle,
    string ContactName, string? ContactEmail, string? ContactPhone, string? AssignedAgentName);

public sealed record LeadSummary(int NewCount);

public sealed record UploadFailure(string FileName, string Reason);
public sealed record UploadResult(List<PropertyImage> Uploaded, List<UploadFailure> Failed);

public sealed class PropertyFilter
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Operation { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class PropertyForm
{
    [Required(ErrorMessage = "El código es obligatorio."), StringLength(50)] public string Code { get; set; } = "";
    [Required(ErrorMessage = "El título es obligatorio."), StringLength(200)] public string Title { get; set; } = "";
    [StringLength(5000)] public string? Description { get; set; }

    public string Operation { get; set; } = "Sale";
    public string Type { get; set; } = "House";
    public string Status { get; set; } = "Draft";

    [Range(typeof(decimal), "0", "1000000000000", ErrorMessage = "Precio inválido.")] public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";

    [Required(ErrorMessage = "La calle es obligatoria."), StringLength(250)] public string Street { get; set; } = "";
    [Required(ErrorMessage = "La ciudad es obligatoria."), StringLength(100)] public string City { get; set; } = "";
    [StringLength(100)] public string? Province { get; set; }
    [Range(-90, 90, ErrorMessage = "Latitud inválida.")] public double? Latitude { get; set; }
    [Range(-180, 180, ErrorMessage = "Longitud inválida.")] public double? Longitude { get; set; }

    [Range(0, 50)] public int Bedrooms { get; set; }
    [Range(0, 50)] public int Bathrooms { get; set; }
    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Superficie inválida.")] public decimal AreaM2 { get; set; }
    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Superficie cubierta inválida.")] public decimal? CoveredAreaM2 { get; set; }
    [Range(0, 50)] public int Rooms { get; set; }
    [Range(0, 20)] public int Garages { get; set; }
    [Range(0, 20)] public int HalfBaths { get; set; }
    [Range(0, 200, ErrorMessage = "Antigüedad inválida.")] public int? AgeYears { get; set; }

    // No se editan en el panel, pero deben viajar de vuelta: el PUT reemplaza todos los campos
    public Guid? OwnerId { get; set; }
    public Guid? AssignedAgentId { get; set; }

    public static PropertyForm From(PropertyDetail d) => new()
    {
        Code = d.Code, Title = d.Title, Description = d.Description,
        Operation = d.Operation, Type = d.Type, Status = d.Status,
        Price = d.Price, Currency = d.Currency,
        Street = d.Street, City = d.City, Province = d.Province,
        Latitude = d.Latitude, Longitude = d.Longitude,
        Bedrooms = d.Bedrooms, Bathrooms = d.Bathrooms, AreaM2 = d.AreaM2,
        CoveredAreaM2 = d.CoveredAreaM2, Rooms = d.Rooms, Garages = d.Garages, HalfBaths = d.HalfBaths, AgeYears = d.AgeYears,
        OwnerId = d.OwnerId, AssignedAgentId = d.AssignedAgentId
    };
}

// ---------- Administración (SuperAdmin) ----------
public sealed record TenantItem(
    Guid Id, string Name, string Slug, string? TaxId, string Plan, bool IsActive,
    DateTimeOffset CreatedAt, int PropertyCount, int UserCount, string? WhatsApp);

public sealed record TenantCreated(TenantItem Tenant, Guid AdminUserId);
public sealed record TenantUser(Guid Id, string Email, string FullName, bool IsActive, string? Role);

public sealed class NewTenantForm
{
    [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(200)] public string Name { get; set; } = "";
    [StringLength(30)] public string? TaxId { get; set; }
    public string Plan { get; set; } = "Free";

    [Required(ErrorMessage = "El nombre del responsable es obligatorio."), StringLength(200)] public string AdminFullName { get; set; } = "";
    [Required(ErrorMessage = "El correo es obligatorio."), EmailAddress(ErrorMessage = "Correo inválido.")] public string AdminEmail { get; set; } = "";
    [Required(ErrorMessage = "La contraseña es obligatoria."), StringLength(100, MinimumLength = 8, ErrorMessage = "Mínimo 8 caracteres, con mayúscula, minúscula y número.")]
    public string AdminPassword { get; set; } = "";
}

public sealed class TenantEditForm
{
    [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(200)] public string Name { get; set; } = "";
    [StringLength(30)] public string? TaxId { get; set; }
    public string Plan { get; set; } = "Free";
    [StringLength(32)] public string? WhatsApp { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ChangePasswordForm
{
    [Required(ErrorMessage = "Ingresa tu contraseña actual.")] public string CurrentPassword { get; set; } = "";
    [Required(ErrorMessage = "Ingresa la nueva contraseña."), StringLength(100, MinimumLength = 8, ErrorMessage = "Mínimo 8 caracteres.")]
    public string NewPassword { get; set; } = "";
    [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden.")] public string Confirm { get; set; } = "";
}

public sealed class LoginForm
{
    [Required(ErrorMessage = "Ingresa tu correo."), EmailAddress(ErrorMessage = "Correo inválido.")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Ingresa tu contraseña.")] public string Password { get; set; } = "";
}

// ---------- Catálogos y formato ----------
public static class Catalog
{
    public static readonly (string Value, string Label)[] Operations =
        [("Sale", "Venta"), ("Rent", "Alquiler"), ("TemporaryRent", "Alquiler temporario")];

    public static readonly (string Value, string Label)[] Types =
        [("House", "Casa"), ("Apartment", "Departamento"), ("Land", "Terreno"), ("Office", "Oficina"),
         ("Commercial", "Local comercial"), ("Warehouse", "Depósito")];

    public static readonly (string Value, string Label)[] Statuses =
        [("Draft", "Borrador"), ("Available", "Disponible"), ("Reserved", "Reservada"),
         ("Sold", "Vendida"), ("Rented", "Alquilada"), ("Withdrawn", "Retirada")];

    public static readonly (string Value, string Label)[] Currencies = [("ARS", "ARS ($)"), ("USD", "USD (US$)"), ("EUR", "EUR (€)")];

    public static readonly (string Value, string Label)[] Plans = [("Free", "Free"), ("Pro", "Pro"), ("Enterprise", "Enterprise")];

    public static string Label((string Value, string Label)[] list, string? value) =>
        list.FirstOrDefault(x => x.Value == value).Label ?? value ?? "";

    public static string StatusBadge(string status) => status switch
    {
        "Available" => "bg-success",
        "Reserved" => "bg-warning text-dark",
        "Sold" or "Rented" => "bg-primary",
        "Withdrawn" => "bg-dark",
        _ => "bg-secondary"
    };

    /// <summary>Sin CultureInfo (InvariantGlobalization): 1234567 => "1.234.567".</summary>
    public static string Price(decimal price, string currency)
    {
        var symbol = currency switch { "ARS" => "$", "USD" => "USD", "EUR" => "€", _ => currency };
        var number = price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');
        return $"{symbol} {number}";
    }

    public static string Date(DateTimeOffset d) => d.ToString("dd/MM/yyyy");

    public static readonly (string Value, string Label)[] LeadStatuses =
        [("New", "Nueva"), ("Contacted", "Contactada"), ("Qualified", "Calificada"), ("Closed", "Cerrada"), ("Lost", "Perdida")];

    /// <summary>"hace 5 min", "hace 3 h", "hace 2 días"; pasado una semana, la fecha.</summary>
    public static string Ago(DateTimeOffset d)
    {
        var span = DateTimeOffset.UtcNow - d;
        if (span.TotalMinutes < 1) return "recién";
        if (span.TotalMinutes < 60) return $"hace {(int)span.TotalMinutes} min";
        if (span.TotalHours < 24) return $"hace {(int)span.TotalHours} h";
        if (span.TotalDays < 7) return $"hace {(int)span.TotalDays} {((int)span.TotalDays == 1 ? "día" : "días")}";
        return Date(d);
    }

    /// <summary>Número para wa.me (formato internacional). Heurística para Argentina: celular con código de área => 549 + número.</summary>
    public static string? WhatsAppNumber(string? phone)
    {
        var d = new string((phone ?? "").Where(char.IsDigit).ToArray()).TrimStart('0');
        if (d.Length < 8) return null;
        if (d.StartsWith("54")) return d;                          // ya trae el código de país
        if (d.StartsWith('9') && d.Length == 11) return "54" + d;  // 9 + código de área + número
        return d.Length <= 11 ? "549" + d : d;
    }
}
