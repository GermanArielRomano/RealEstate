using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RealEstate.Mobile.Models;

namespace RealEstate.Mobile.Services;

public interface IPropertiesService
{
    Task<PagedResult<PropertyListItemDto>> GetAvailableAsync(int page, int pageSize, CancellationToken ct = default);
}

/// <summary>
/// El tenant NO se envía: el backend lo toma del claim "tenant_id" del JWT,
/// así cada inmobiliaria solo recibe sus propias propiedades.
/// </summary>
public sealed class PropertiesService(HttpClient http) : IPropertiesService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<PropertyListItemDto>> GetAvailableAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var url = $"api/properties?Status=Available&Sort=Newest&Page={page}&PageSize={pageSize}";

        // Lanza HttpRequestException (con StatusCode) si la respuesta no es 2xx
        var result = await http.GetFromJsonAsync<PagedResult<PropertyListItemDto>>(url, Json, ct);

        return result ?? new PagedResult<PropertyListItemDto>([], page, pageSize, 0);
    }
}

/// <summary>Adjunta el JWT guardado en el almacenamiento seguro del dispositivo a cada request.</summary>
public sealed class AuthHeaderHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await SecureStorage.Default.GetAsync("access_token");
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, ct);
    }
}

/*
 ===========================================================================
  Paquetes NuGet
 ===========================================================================
  dotnet add package CommunityToolkit.Mvvm      (8.4.0 o superior: usa propiedades parciales)
  dotnet add package Microsoft.Extensions.Http

 ===========================================================================
  MauiProgram.cs
 ===========================================================================
  // Render ya sirve HTTPS con certificado válido: no hace falta configurar certificados en Android/iOS.
  var apiBaseUrl = "https://TU-SERVICIO.onrender.com/";

  builder.Services.AddTransient<AuthHeaderHandler>();
  builder.Services.AddHttpClient<IPropertiesService, PropertiesService>(c =>
      {
          c.BaseAddress = new Uri(apiBaseUrl);
          c.Timeout = TimeSpan.FromSeconds(90); // la instancia gratuita de Render tarda ~1 min en despertar
      })
      .AddHttpMessageHandler<AuthHeaderHandler>();

  builder.Services.AddTransient<PropertiesListViewModel>();
  builder.Services.AddTransient<PropertiesListPage>();

 ===========================================================================
  AppShell.xaml
 ===========================================================================
  <ShellContent Title="Propiedades" Route="properties"
                ContentTemplate="{DataTemplate views:PropertiesListPage}" />

 ===========================================================================
  AppShell.xaml.cs  (ruta de la pantalla de detalle, que crearás después)
 ===========================================================================
  Routing.RegisterRoute(AppRoutes.PropertyDetail, typeof(PropertyDetailPage));
  // En PropertyDetailPage/ViewModel recibe el parámetro con [QueryProperty(nameof(Id), "id")]
*/
