using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RealEstate.Client.Models;

namespace RealEstate.Client.Services;

public sealed class ApiException(string message) : Exception(message);

/// <summary>Cliente del catálogo público (sin login, solo lectura).</summary>
public sealed class PublicApi(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Unreachable =
        "No se pudo conectar con el servidor. Si estaba inactivo puede tardar hasta un minuto en despertar: reintenta en unos segundos.";

    public Task<PagedResult<PublicListItem>> SearchAsync(SearchFilter f, CancellationToken ct = default)
    {
        var qs = FilterParams(f);
        qs.Add($"Page={f.Page}");
        qs.Add($"PageSize={f.PageSize}");
        return GetAsync<PagedResult<PublicListItem>>($"api/public/properties?{string.Join('&', qs)}", ct);
    }

    /// <summary>Pines del mapa con los mismos filtros (y zona) que el listado.</summary>
    public Task<MapResult> MapAsync(SearchFilter f, CancellationToken ct = default) =>
        GetAsync<MapResult>($"api/public/map?{string.Join('&', FilterParams(f))}", ct);

    public Task<PagedResult<PublicListItem>> ByIdsAsync(IEnumerable<Guid> ids, int page, CancellationToken ct = default) =>
        GetAsync<PagedResult<PublicListItem>>(
            $"api/public/properties?Ids={string.Join(',', ids)}&Page={page}&PageSize=12", ct);

    public Task<PublicFilters> FiltersAsync(CancellationToken ct = default) =>
        GetAsync<PublicFilters>("api/public/filters", ct);

    /// <summary>null si la propiedad ya no está disponible.</summary>
    public async Task<PublicDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync($"api/public/properties/{id}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PublicDetail>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException(Unreachable);
        }
    }

    private static List<string> FilterParams(SearchFilter f)
    {
        var qs = new List<string> { $"Sort={f.Sort}" };
        var inv = CultureInfo.InvariantCulture;

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) qs.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
        }

        Add("Operation", f.Operation);
        Add("Type", f.Type);
        Add("City", f.City);
        Add("Search", f.Search);
        if (f.MinBedrooms is { } beds) qs.Add($"MinBedrooms={beds}");

        if (f.MinPrice is not null || f.MaxPrice is not null)
        {
            Add("Currency", f.Currency);   // el servidor solo filtra por precio si llega la moneda
            if (f.MinPrice is { } min) qs.Add($"MinPrice={min.ToString(inv)}");
            if (f.MaxPrice is { } max) qs.Add($"MaxPrice={max.ToString(inv)}");
        }

        if (f.HasZone)
        {
            qs.Add($"MinLat={f.MinLat!.Value.ToString(inv)}");
            qs.Add($"MaxLat={f.MaxLat!.Value.ToString(inv)}");
            qs.Add($"MinLon={f.MinLon!.Value.ToString(inv)}");
            qs.Add($"MaxLon={f.MaxLon!.Value.ToString(inv)}");
        }

        return qs;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync<T>(url, Json, ct) ?? throw new ApiException("Respuesta vacía del servidor.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ApiException(Unreachable);
        }
    }
}
