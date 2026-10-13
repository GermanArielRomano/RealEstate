using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RealEstate.Domain;

namespace RealEstate.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public const string TenantFilter = "Tenant";
    public const string SoftDeleteFilter = "SoftDelete";

    // Propiedades de instancia: EF las parametriza en cada consulta, por lo que
    // el valor se evalúa por cada DbContext (clave para que funcione multi-tenant).
    private Guid? CurrentTenantId => currentTenant.TenantId;
    private bool IsSuperAdmin => currentTenant.IsSuperAdmin;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyImage> PropertyImages => Set<PropertyImage>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Visit> Visits => Set<Visit>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ---- Tenant ----
        builder.Entity<Tenant>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(200).IsRequired();
            e.Property(t => t.Slug).HasMaxLength(100).IsRequired();
            e.HasIndex(t => t.Slug).IsUnique();

            // Un usuario normal solo ve su propia inmobiliaria
            e.HasQueryFilter(TenantFilter, t => IsSuperAdmin || t.Id == CurrentTenantId);
        });

        // ---- Usuarios ----
        builder.Entity<AppUser>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            e.HasOne(u => u.Tenant).WithMany(t => t.Users)
             .HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(u => u.TenantId);
            // Sin filtro automático: Identity consulta usuarios ANTES de conocer el tenant (login).
            // En listados de administración filtra explícitamente por TenantId.
        });

        // ---- Property ----
        builder.Entity<Property>(e =>
        {
            e.Property(p => p.Code).HasMaxLength(50).IsRequired();
            e.Property(p => p.Title).HasMaxLength(200).IsRequired();
            e.Property(p => p.Street).HasMaxLength(250).IsRequired();
            e.Property(p => p.City).HasMaxLength(100).IsRequired();

            // El código es único DENTRO de cada inmobiliaria, no globalmente
            e.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
            e.HasIndex(p => new { p.TenantId, p.Status, p.Operation, p.City });

            e.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.AssignedAgent).WithMany().HasForeignKey(p => p.AssignedAgentId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PropertyImage>(e =>
        {
            e.Property(i => i.Url).HasMaxLength(500).IsRequired();
            e.HasOne(i => i.Property).WithMany(p => p.Images)
             .HasForeignKey(i => i.PropertyId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Contact>(e =>
        {
            e.Property(c => c.FullName).HasMaxLength(200).IsRequired();
            e.HasIndex(c => new { c.TenantId, c.Email });
        });

        builder.Entity<Lead>(e =>
        {
            e.HasOne(l => l.Property).WithMany(p => p.Leads)
             .HasForeignKey(l => l.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Contact).WithMany()
             .HasForeignKey(l => l.ContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.AssignedAgent).WithMany()
             .HasForeignKey(l => l.AssignedAgentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Visit>(e =>
        {
            e.HasOne(v => v.Property).WithMany(p => p.Visits)
             .HasForeignKey(v => v.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Contact).WithMany()
             .HasForeignKey(v => v.ContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Agent).WithMany()
             .HasForeignKey(v => v.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.TenantId, v.AgentId, v.ScheduledAt });
        });

        ApplyGlobalFilters(builder);
    }

    // ---------- Filtros globales automáticos ----------
    private void ApplyGlobalFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;

            if (typeof(ITenantEntity).IsAssignableFrom(clr))
                InvokeGeneric(nameof(AddTenantFilter), clr, builder);

            if (typeof(ISoftDelete).IsAssignableFrom(clr))
                InvokeGeneric(nameof(AddSoftDeleteFilter), clr, builder);
        }
    }

    private void InvokeGeneric(string method, Type entityType, ModelBuilder builder) =>
        GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(entityType)
            .Invoke(this, [builder]);

    private void AddTenantFilter<T>(ModelBuilder builder) where T : class, ITenantEntity =>
        builder.Entity<T>().HasQueryFilter(TenantFilter,
            e => IsSuperAdmin || e.TenantId == CurrentTenantId);
        // Sin tenant y sin SuperAdmin => TenantId == null => no devuelve nada (fail-closed)

    private void AddSoftDeleteFilter<T>(ModelBuilder builder) where T : class, ISoftDelete =>
        builder.Entity<T>().HasQueryFilter(SoftDeleteFilter, e => !e.IsDeleted);

    // ---------- Reglas automáticas al guardar ----------
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplySaveRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ApplySaveRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void ApplySaveRules()
    {
        var now = DateTimeOffset.UtcNow; // PostgreSQL (timestamptz) exige offset 0

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is BaseEntity b)
            {
                if (entry.State == EntityState.Added) b.CreatedAt = now;
                if (entry.State == EntityState.Modified) b.UpdatedAt = now;
            }

            if (entry.Entity is not ITenantEntity te) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    if (te.TenantId == Guid.Empty)
                        te.TenantId = CurrentTenantId
                            ?? throw new InvalidOperationException(
                                "No hay tenant en contexto: el SuperAdmin debe indicar el TenantId explícitamente.");
                    else if (!IsSuperAdmin && te.TenantId != CurrentTenantId)
                        throw new UnauthorizedAccessException("No puedes crear datos en otro tenant.");
                    break;

                case EntityState.Modified or EntityState.Deleted:
                    if (!IsSuperAdmin && te.TenantId != CurrentTenantId)
                        throw new UnauthorizedAccessException("Acceso cruzado entre tenants.");
                    if (entry.Property(nameof(ITenantEntity.TenantId)).IsModified)
                        throw new InvalidOperationException("TenantId es inmutable.");
                    break;
            }

            // Borrado lógico: convierte DELETE en UPDATE
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDelete sd)
            {
                entry.State = EntityState.Modified;
                sd.IsDeleted = true;
                sd.DeletedAt = now;
            }
        }
    }
}
