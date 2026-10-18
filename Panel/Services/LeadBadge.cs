namespace RealEstate.Panel.Services;

/// <summary>Cantidad de consultas nuevas para el numerito de la barra. Estático: no necesita registro en Program.cs.</summary>
public static class LeadBadge
{
    public static int NewCount { get; private set; }
    public static event Action? Changed;

    public static async Task RefreshAsync(ApiClient api, AuthService auth)
    {
        var roles = auth.User?.Roles;
        if (roles is null || !(roles.Contains("TenantAdmin") || roles.Contains("Agent")))
        {
            Set(0);
            return;
        }

        try { Set((await api.LeadSummaryAsync()).NewCount); }
        catch (ApiException) { /* el aviso no es crítico: se reintenta en la próxima navegación */ }
    }

    private static void Set(int count)
    {
        if (count == NewCount) return;
        NewCount = count;
        Changed?.Invoke();
    }
}
