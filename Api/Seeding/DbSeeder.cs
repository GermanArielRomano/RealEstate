using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RealEstate.Domain;
using RealEstate.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace RealEstate.Api.Seeding;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    public string SuperAdminEmail { get; set; } = "";
    public string SuperAdminPassword { get; set; } = "";   // solo por variable de entorno, nunca en el repo
    public string SuperAdminName { get; set; } = "Super Admin";

    public bool DemoData { get; set; }
    public string DemoEmail { get; set; } = "socio@demo.test";
    public string DemoPassword { get; set; } = "";
}

public static class DbSeeder
{
    /// <summary>Aplica migraciones, crea roles, el SuperAdmin y (opcional) datos de demo. No tumba la app si falla.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");
        var seed = sp.GetRequiredService<IOptions<SeedOptions>>().Value;

        try
        {
            var db = sp.GetRequiredService<AppDbContext>();

            if (db.Database.GetMigrations().Any())
            {
                await db.Database.MigrateAsync(ct);
            }
            else
            {
                log.LogWarning("No hay migraciones en el proyecto. Se usa EnsureCreated (solo válido para una demo). " +
                               "Ejecuta 'dotnet ef migrations add Initial' y vuelve a desplegar.");
                //await db.Database.EnsureCreatedAsync(ct);
                var exists = await db.Database
                    .SqlQuery<bool>($"""SELECT to_regclass('public."AspNetRoles"') IS NOT NULL AS "Value" """)
                    .SingleAsync(ct);

                if (!exists)
                    await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
            }

            await EnsureRolesAsync(sp.GetRequiredService<RoleManager<AppRole>>());
            await EnsureSuperAdminAsync(sp.GetRequiredService<UserManager<AppUser>>(), seed, log);

            if (seed.DemoData)
                await EnsureDemoDataAsync(sp, seed, log, ct);
        }
        catch (Exception ex)
        {
            // Típico: Supabase pausado, cadena de conexión incorrecta o migraciones pendientes de generar
            log.LogError(ex, "La inicialización de la base de datos falló. La API arranca igual; revisa la configuración.");
        }
    }

    private static async Task EnsureRolesAsync(RoleManager<AppRole> roles)
    {
        foreach (var name in new[] { Roles.SuperAdmin, Roles.TenantAdmin, Roles.Agent })
            if (!await roles.RoleExistsAsync(name))
                await roles.CreateAsync(new AppRole(name));
    }

    private static async Task EnsureSuperAdminAsync(UserManager<AppUser> users, SeedOptions seed, ILogger log)
    {
        if (string.IsNullOrWhiteSpace(seed.SuperAdminEmail) || string.IsNullOrWhiteSpace(seed.SuperAdminPassword))
        {
            log.LogWarning("Seed__SuperAdminEmail / Seed__SuperAdminPassword no configurados: no se crea el SuperAdmin.");
            return;
        }

        if (await users.FindByEmailAsync(seed.SuperAdminEmail) is not null) return;

        var admin = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = seed.SuperAdminEmail,
            Email = seed.SuperAdminEmail,
            EmailConfirmed = true,
            FullName = seed.SuperAdminName,
            TenantId = null,       // null => SuperAdmin
            IsActive = true
        };

        var result = await users.CreateAsync(admin, seed.SuperAdminPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException("No se pudo crear el SuperAdmin: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(admin, Roles.SuperAdmin);
        log.LogInformation("SuperAdmin creado: {Email}", seed.SuperAdminEmail);
    }

    private static async Task EnsureDemoDataAsync(IServiceProvider sp, SeedOptions seed, ILogger log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(seed.DemoPassword))
        {
            log.LogWarning("Seed__DemoData=true pero falta Seed__DemoPassword: se omiten los datos de demo.");
            return;
        }

        // Contexto "de sistema" (SuperAdmin): puede escribir con un TenantId explícito
        var options = sp.GetRequiredService<DbContextOptions<AppDbContext>>();
        await using var db = new AppDbContext(options, new SystemTenant());

        const string slug = "demo-inmobiliaria";
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct)) return;

        var tenant = new Tenant { Name = "Inmobiliaria Demo", Slug = slug, TaxId = "30-00000000-0", Plan = TenantPlan.Free };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var socio = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = seed.DemoEmail,
            Email = seed.DemoEmail,
            EmailConfirmed = true,
            FullName = "Socio Demo",
            TenantId = tenant.Id,
            IsActive = true
        };

        var created = await users.CreateAsync(socio, seed.DemoPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException("No se pudo crear el socio demo: " +
                string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(socio, Roles.TenantAdmin);

        (string Code, string Title, OperationType Op, PropertyType Type, decimal Price, Currency Cur,
         string Street, string City, int Beds, int Baths, decimal Area)[] samples =
        [
            ("DEMO-001", "Casa 3 dormitorios con patio y parrilla", OperationType.Sale, PropertyType.House, 98000, Currency.USD, "San Martín 1200", "Firmat", 3, 2, 180),
            ("DEMO-002", "Departamento 2 ambientes céntrico", OperationType.Rent, PropertyType.Apartment, 320000, Currency.ARS, "Córdoba 850", "Rosario", 1, 1, 52),
            ("DEMO-003", "Lote 10x30 listo para construir", OperationType.Sale, PropertyType.Land, 24000, Currency.USD, "Ruta 90 km 4", "Venado Tuerto", 0, 0, 300),
            ("DEMO-004", "Local comercial sobre avenida", OperationType.Rent, PropertyType.Commercial, 550000, Currency.ARS, "Belgrano 440", "Firmat", 0, 1, 75),
            ("DEMO-005", "Departamento 3 ambientes con cochera", OperationType.Sale, PropertyType.Apartment, 87000, Currency.USD, "Pellegrini 2300", "Rosario", 2, 2, 78)
        ];

        foreach (var s in samples)
        {
            db.Properties.Add(new Property
            {
                TenantId = tenant.Id,
                Code = s.Code, Title = s.Title, Operation = s.Op, Type = s.Type,
                Status = PropertyStatus.Available, Price = s.Price, Currency = s.Cur,
                Street = s.Street, City = s.City, Province = "Santa Fe",
                Bedrooms = s.Beds, Bathrooms = s.Baths, AreaM2 = s.Area
            });
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Datos de demo creados (socio: {Email}).", seed.DemoEmail);
    }
}
