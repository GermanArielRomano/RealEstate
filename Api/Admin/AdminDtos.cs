using System.ComponentModel.DataAnnotations;
using RealEstate.Domain;

namespace RealEstate.Api.Admin;

public sealed class CreateTenantRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = default!;

    [StringLength(100), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$",
        ErrorMessage = "El identificador solo admite minúsculas, números y guiones.")]
    public string? Slug { get; init; }

    [StringLength(30)] public string? TaxId { get; init; }
    [EnumDataType(typeof(TenantPlan))] public TenantPlan Plan { get; init; } = TenantPlan.Free;

    // Primer usuario del socio (rol TenantAdmin)
    [Required, StringLength(200)] public string AdminFullName { get; init; } = default!;
    [Required, EmailAddress, StringLength(256)] public string AdminEmail { get; init; } = default!;
    [Required, StringLength(100, MinimumLength = 8)] public string AdminPassword { get; init; } = default!;
}

public sealed class UpdateTenantRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = default!;
    [StringLength(30)] public string? TaxId { get; init; }
    [EnumDataType(typeof(TenantPlan))] public TenantPlan Plan { get; init; }
    [StringLength(32)] public string? WhatsApp { get; init; }
    public bool IsActive { get; init; }
}

public sealed class ResetPasswordRequest
{
    [Required, StringLength(100, MinimumLength = 8)] public string NewPassword { get; init; } = default!;
}

public sealed class SetActiveRequest
{
    public bool IsActive { get; init; }
}

public sealed record TenantDto(
    Guid Id, string Name, string Slug, string? TaxId, TenantPlan Plan, bool IsActive,
    DateTimeOffset CreatedAt, int PropertyCount, int UserCount, string? WhatsApp);

public sealed record TenantCreatedDto(TenantDto Tenant, Guid AdminUserId);

public sealed record TenantUserDto(Guid Id, string Email, string FullName, bool IsActive, string? Role);
