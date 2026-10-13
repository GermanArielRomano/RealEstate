using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using RealEstate.Panel.Models;

namespace RealEstate.Panel.Services;

/// <summary>
/// Sesión + AuthenticationStateProvider. El JWT se guarda en localStorage (aceptable para una demo;
/// en producción conviene un esquema con cookies HttpOnly / BFF).
/// </summary>
public sealed class AuthService(HttpClient http, IJSRuntime js) : AuthenticationStateProvider
{
    private const string StorageKey = "re_session";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private Session? _session;
    private bool _loaded;

    public string? Token => _session?.Token;
    public UserInfo? User => _session?.User;
    public bool IsSuperAdmin => User?.Roles.Contains("SuperAdmin") == true;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await EnsureLoadedAsync();
        if (_session is null) return Anonymous;

        var u = _session.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.FullName),
            new(ClaimTypes.Email, u.Email)
        };
        claims.AddRange(u.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt")));
    }

    public async Task LoginAsync(string email, string password)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("api/auth/login", new { email, password }, Json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException("No se pudo conectar con el servidor. Si estaba inactivo puede tardar hasta un minuto en despertar: inténtalo de nuevo.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw await ApiException.FromAsync(response);

            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json)
                        ?? throw new ApiException("Respuesta inválida del servidor.");

            _session = new Session(login.AccessToken, login.ExpiresAt, login.User);
            _loaded = true;
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(_session, Json));
        }

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task LogoutAsync()
    {
        _session = null;
        _loaded = true;
        await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(raw)) return;

            var session = JsonSerializer.Deserialize<Session>(raw, Json);
            if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
                return;
            }

            _session = session;
        }
        catch (JsonException)
        {
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
    }
}
