using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using RealEstate.Panel.Models;

namespace RealEstate.Panel.Services;

public sealed class ApiClient(HttpClient http, AuthService auth)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // =====================================================================
    //  Propiedades (socio: TenantAdmin / Agent). El tenant lo decide el JWT.
    // =====================================================================
    public async Task<PagedResult<PropertyListItem>> ListPropertiesAsync(PropertyFilter f, CancellationToken ct = default)
    {
        var query = new List<string> { $"Page={f.Page}", $"PageSize={f.PageSize}", "Sort=Newest" };
        if (!string.IsNullOrWhiteSpace(f.Search)) query.Add($"Search={Uri.EscapeDataString(f.Search.Trim())}");
        if (!string.IsNullOrWhiteSpace(f.Status)) query.Add($"Status={f.Status}");
        if (!string.IsNullOrWhiteSpace(f.Operation)) query.Add($"Operation={f.Operation}");

        return await GetAsync<PagedResult<PropertyListItem>>($"api/properties?{string.Join('&', query)}", ct);
    }

    public Task<PropertyDetail> GetPropertyAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<PropertyDetail>($"api/properties/{id}", ct);

    public Task<PropertyDetail> CreatePropertyAsync(PropertyForm form) =>
        SendJsonAsync<PropertyDetail>(HttpMethod.Post, "api/properties", form);

    public Task<PropertyDetail> UpdatePropertyAsync(Guid id, PropertyForm form) =>
        SendJsonAsync<PropertyDetail>(HttpMethod.Put, $"api/properties/{id}", form);

    public async Task DeletePropertyAsync(Guid id) =>
        (await SendAsync(HttpMethod.Delete, $"api/properties/{id}")).Dispose();

    public async Task<UploadResult> UploadImagesAsync(Guid propertyId, IReadOnlyList<IBrowserFile> files, long maxFileSize)
    {
        using var content = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var stream = new StreamContent(file.OpenReadStream(maxFileSize));
            stream.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            content.Add(stream, "files", file.Name);   // el campo debe llamarse "files"
        }

        using var response = await SendAsync(HttpMethod.Post, $"api/properties/{propertyId}/images", content, allowBadRequest: true);

        // 400 también trae el detalle por archivo (ninguno fue válido)
        return await response.Content.ReadFromJsonAsync<UploadResult>(Json)
               ?? throw new ApiException("Respuesta inválida del servidor.");
    }

    public async Task SetCoverAsync(Guid propertyId, Guid imageId) =>
        (await SendAsync(HttpMethod.Put, $"api/properties/{propertyId}/images/{imageId}/cover")).Dispose();

    public async Task DeleteImageAsync(Guid propertyId, Guid imageId) =>
        (await SendAsync(HttpMethod.Delete, $"api/properties/{propertyId}/images/{imageId}")).Dispose();

    // =====================================================================
    //  Administración (SuperAdmin)
    // =====================================================================
    public Task<List<TenantItem>> ListTenantsAsync(CancellationToken ct = default) =>
        GetAsync<List<TenantItem>>("api/admin/tenants", ct);

    public Task<TenantItem> GetTenantAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<TenantItem>($"api/admin/tenants/{id}", ct);

    public Task<TenantCreated> CreateTenantAsync(NewTenantForm form) =>
        SendJsonAsync<TenantCreated>(HttpMethod.Post, "api/admin/tenants", form);

    public Task<TenantItem> UpdateTenantAsync(Guid id, TenantEditForm form) =>
        SendJsonAsync<TenantItem>(HttpMethod.Put, $"api/admin/tenants/{id}", form);

    public Task<List<TenantUser>> ListTenantUsersAsync(Guid tenantId, CancellationToken ct = default) =>
        GetAsync<List<TenantUser>>($"api/admin/tenants/{tenantId}/users", ct);

    public async Task ResetPasswordAsync(Guid userId, string newPassword) =>
        (await SendAsync(HttpMethod.Post, $"api/admin/users/{userId}/reset-password",
            JsonContent.Create(new { newPassword }, options: Json))).Dispose();

    public async Task SetUserActiveAsync(Guid userId, bool isActive) =>
        (await SendAsync(HttpMethod.Put, $"api/admin/users/{userId}/active",
            JsonContent.Create(new { isActive }, options: Json))).Dispose();

    // =====================================================================
    //  Consultas de compradores
    // =====================================================================
    public Task<PagedResult<LeadItem>> ListLeadsAsync(string? status, int page, CancellationToken ct = default)
    {
        var url = $"api/leads?Page={page}&PageSize=20";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={status}";
        return GetAsync<PagedResult<LeadItem>>(url, ct);
    }

    public Task<LeadSummary> LeadSummaryAsync(CancellationToken ct = default) =>
        GetAsync<LeadSummary>("api/leads/summary", ct);

    public async Task SetLeadStatusAsync(Guid id, string status) =>
        (await SendAsync(HttpMethod.Put, $"api/leads/{id}/status", JsonContent.Create(new { status }, options: Json))).Dispose();

    public async Task DeleteLeadAsync(Guid id) =>
        (await SendAsync(HttpMethod.Delete, $"api/leads/{id}")).Dispose();

    // =====================================================================
    //  Cuenta
    // =====================================================================
    public async Task ChangePasswordAsync(ChangePasswordForm form) =>
        (await SendAsync(HttpMethod.Post, "api/auth/change-password",
            JsonContent.Create(new { form.CurrentPassword, form.NewPassword }, options: Json))).Dispose();

    // =====================================================================
    //  Infraestructura HTTP
    // =====================================================================
    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, url, ct: ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
               ?? throw new ApiException("Respuesta inválida del servidor.");
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string url, object body)
    {
        using var response = await SendAsync(method, url, JsonContent.Create(body, options: Json));
        return await response.Content.ReadFromJsonAsync<T>(Json)
               ?? throw new ApiException("Respuesta inválida del servidor.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, HttpContent? content = null,
        bool allowBadRequest = false, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (auth.Token is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException("No se pudo conectar con el servidor. Si estaba inactivo puede tardar hasta un minuto en despertar: reintenta en unos segundos.");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized && auth.Token is not null)
        {
            response.Dispose();
            await auth.LogoutAsync();   // el AuthorizeRouteView redirige al login
            throw new ApiException("Tu sesión expiró. Inicia sesión nuevamente.", 401);
        }

        if (response.IsSuccessStatusCode || (allowBadRequest && response.StatusCode == HttpStatusCode.BadRequest))
            return response;

        var error = await ApiException.FromAsync(response);
        response.Dispose();
        throw error;
    }
}
