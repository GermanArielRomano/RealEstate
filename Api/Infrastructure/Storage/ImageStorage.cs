using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace RealEstate.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>"Supabase" (demo / producción gratis) o "Local" (solo desarrollo: en Render el disco es efímero).</summary>
    public string Provider { get; set; } = "Supabase";

    // ---- Supabase Storage ----
    public string SupabaseUrl { get; set; } = "";       // https://<ref>.supabase.co
    public string SupabaseKey { get; set; } = "";       // secret key (sb_secret_...). NUNCA la publishable/anon
    public string SupabaseBucket { get; set; } = "property-images";   // bucket PÚBLICO

    // ---- Local ----
    public string LocalRootPath { get; set; } = "uploads";
    public string PublicBaseUrl { get; set; } = "";      // Local: https://localhost:7001/media
}

public interface IImageStorage
{
    Task SaveAsync(string key, byte[] content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    string GetUrl(string key);
}

/// <summary>
/// Las claves llevan el tenant como primer segmento: aísla los archivos por inmobiliaria
/// y permite borrar o exportar todo un tenant por prefijo.
/// </summary>
public static class ImageKeys
{
    public const string ContentType = "image/webp";

    public static string Main(Guid tenantId, Guid propertyId, Guid imageId) =>
        $"{tenantId:N}/properties/{propertyId:N}/{imageId:N}.webp";

    public static string Thumbnail(Guid tenantId, Guid propertyId, Guid imageId) =>
        $"{tenantId:N}/properties/{propertyId:N}/{imageId:N}_thumb.webp";
}

// ===========================================================================
//  Supabase Storage (REST). Plan gratis: 1 GB, archivos de hasta 50 MB.
//  Los encabezados (apikey / Authorization) se configuran en AddImageHandling.
// ===========================================================================
public sealed class SupabaseImageStorage : IImageStorage
{
    private readonly HttpClient _http;
    private readonly string _objectBase;   // .../storage/v1/object/{bucket}
    private readonly string _publicBase;   // .../storage/v1/object/public/{bucket}

    public SupabaseImageStorage(HttpClient http, IOptions<StorageOptions> options)
    {
        var o = options.Value;
        var baseUrl = o.SupabaseUrl.TrimEnd('/');

        _http = http;
        _objectBase = $"{baseUrl}/storage/v1/object/{o.SupabaseBucket}";
        _publicBase = $"{baseUrl}/storage/v1/object/public/{o.SupabaseBucket}";
    }

    public async Task SaveAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_objectBase}/{key}") { Content = body };
        // La clave contiene un GUID único: el archivo nunca cambia, se puede cachear "para siempre"
        request.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromDays(365) };

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new IOException(
                $"Supabase Storage rechazó la subida ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(ct)}");
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"{_objectBase}/{key}", ct);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new IOException($"Supabase Storage rechazó el borrado ({(int)response.StatusCode}).");
    }

    public string GetUrl(string key) => $"{_publicBase}/{key}";
}

// ===========================================================================
//  Carpeta local (solo desarrollo)
// ===========================================================================
public sealed class LocalImageStorage : IImageStorage
{
    private readonly string _baseUrl;

    public string RootPath { get; }

    public LocalImageStorage(IOptions<StorageOptions> options, IHostEnvironment env)
    {
        var o = options.Value;
        RootPath = Path.GetFullPath(Path.IsPathRooted(o.LocalRootPath)
            ? o.LocalRootPath
            : Path.Combine(env.ContentRootPath, o.LocalRootPath));
        _baseUrl = o.PublicBaseUrl.TrimEnd('/');
        Directory.CreateDirectory(RootPath);
    }

    public async Task SaveAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public string GetUrl(string key) => $"{_baseUrl}/{key}";

    /// <summary>Defensa contra path traversal: la ruta final debe quedar dentro de RootPath.</summary>
    private string Resolve(string key)
    {
        var full = Path.GetFullPath(Path.Combine(RootPath, key));
        if (!full.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Ruta de almacenamiento inválida.");
        return full;
    }
}
