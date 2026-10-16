using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using RealEstate.Client;
using RealEstate.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// La URL de la API sale de wwwroot/appsettings.json (en GitHub Actions se reemplaza con la variable API_BASE_URL)
var apiBase = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7001/";
if (!apiBase.EndsWith('/')) apiBase += "/";

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase), Timeout = TimeSpan.FromSeconds(100) });
builder.Services.AddMudServices(c => c.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter);
builder.Services.AddScoped<PublicApi>();
builder.Services.AddScoped<FavoritesService>();

await builder.Build().RunAsync();
