using Microsoft.AspNetCore.Identity;

namespace RealEstate.Domain;

public interface ITenantEntity { Guid TenantId { get; set; } }

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
}

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7(); // GUID v7: ordenable, ideal para índices
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public abstract class TenantEntity : BaseEntity, ITenantEntity, ISoftDelete
{
    public Guid TenantId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public static class Roles
{
    public const string SuperAdmin = nameof(SuperAdmin);   // Tú (plataforma)
    public const string TenantAdmin = nameof(TenantAdmin); // Socio: dueño de la inmobiliaria
    public const string Agent = nameof(Agent);             // Agente inmobiliario
}

// ---------- Enums ----------
public enum TenantPlan { Free, Pro, Enterprise }
public enum OperationType { Sale, Rent, TemporaryRent }
public enum PropertyType { House, Apartment, Land, Office, Commercial, Warehouse }
public enum PropertyStatus { Draft, Available, Reserved, Sold, Rented, Withdrawn }
public enum Currency { ARS, USD, EUR }
public enum ContactType { Owner, Buyer, Tenant, Other }
public enum LeadStatus { New, Contacted, Qualified, Closed, Lost }
public enum VisitStatus { Scheduled, Completed, Cancelled, NoShow }

// ---------- Tenant (la inmobiliaria) ----------
public class Tenant : BaseEntity
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? TaxId { get; set; }
    public TenantPlan Plan { get; set; } = TenantPlan.Free;
    public bool IsActive { get; set; } = true;

    public ICollection<AppUser> Users { get; set; } = [];
}

// ---------- Identity ----------
public class AppUser : IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }            // null => SuperAdmin
    public string FullName { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public Tenant? Tenant { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}

// ---------- Negocio ----------
public class Property : TenantEntity
{
    public string Code { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public OperationType Operation { get; set; }
    public PropertyType Type { get; set; }
    public PropertyStatus Status { get; set; } = PropertyStatus.Draft;

    public decimal Price { get; set; }
    public Currency Currency { get; set; }

    public string Street { get; set; } = default!;
    public string City { get; set; } = default!;
    public string? Province { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public int Bedrooms { get; set; }
    public int Bathrooms { get; set; }
    public decimal AreaM2 { get; set; }                // superficie total
    public decimal? CoveredAreaM2 { get; set; }        // superficie cubierta
    public int Rooms { get; set; }                     // ambientes
    public int Garages { get; set; }                   // cocheras
    public int HalfBaths { get; set; }                 // toilettes
    public int? AgeYears { get; set; }                 // antigüedad en años; 0 = a estrenar; null = sin dato

    public Guid? OwnerId { get; set; }
    public Contact? Owner { get; set; }
    public Guid? AssignedAgentId { get; set; }
    public AppUser? AssignedAgent { get; set; }

    public ICollection<PropertyImage> Images { get; set; } = [];
    public ICollection<Lead> Leads { get; set; } = [];
    public ICollection<Visit> Visits { get; set; } = [];
}

public class PropertyImage : TenantEntity
{
    public Guid PropertyId { get; set; }
    public Property Property { get; set; } = default!;
    public string Url { get; set; } = default!;
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
}

public class Contact : TenantEntity
{
    public string FullName { get; set; } = default!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public ContactType Type { get; set; }
}

public class Lead : TenantEntity
{
    public Guid PropertyId { get; set; }
    public Property Property { get; set; } = default!;
    public Guid ContactId { get; set; }
    public Contact Contact { get; set; } = default!;
    public Guid? AssignedAgentId { get; set; }
    public AppUser? AssignedAgent { get; set; }
    public string? Message { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.New;
}

public class Visit : TenantEntity
{
    public Guid PropertyId { get; set; }
    public Property Property { get; set; } = default!;
    public Guid ContactId { get; set; }
    public Contact Contact { get; set; } = default!;
    public Guid AgentId { get; set; }
    public AppUser Agent { get; set; } = default!;
    public DateTimeOffset ScheduledAt { get; set; }
    public VisitStatus Status { get; set; } = VisitStatus.Scheduled;
    public string? Notes { get; set; }
}
