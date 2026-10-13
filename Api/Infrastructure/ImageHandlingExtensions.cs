using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using RealEstate.Infrastructure.Imaging;
using RealEstate.Infrastructure.Storage;
using SixLabors.ImageSharp.Memory;

namespace RealEstate.Infrastructure;

public static class ImageHandlingExtensions
{
    public static IServiceCollection AddImageHandling(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ImageOptions>(config.GetSection(ImageOptions.Section));
        services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));

        // Render gratis = 512 MB de RAM: limita el pool de memoria de ImageSharp y el tamaño de cada reserva
        SixLabors.ImageSharp.Configuration.Default.MemoryAllocator = MemoryAllocator.Create(
            new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = 16, AllocationLimitMegabytes = 256 });

        services.AddSingleton<IImageOptimizer, ImageSharpOptimizer>();

        var provider = config[$"{StorageOptions.Section}:Provider"] ?? "Supabase";
        if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IImageStorage, LocalImageStorage>();
        }
        else
        {
            services.AddHttpClient<IImageStorage, SupabaseImageStorage>((sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
                if (string.IsNullOrWhiteSpace(o.SupabaseUrl) || string.IsNullOrWhiteSpace(o.SupabaseKey))
                    throw new InvalidOperationException("Faltan Storage__SupabaseUrl y/o Storage__SupabaseKey.");

                http.Timeout = TimeSpan.FromSeconds(30);

                // Las claves nuevas (sb_secret_...) viajan SOLO en 'apikey'.
                // Las legacy (service_role, un JWT) también se aceptan en Authorization.
                http.DefaultRequestHeaders.Add("apikey", o.SupabaseKey);
                if (!o.SupabaseKey.StartsWith("sb_", StringComparison.Ordinal))
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.SupabaseKey);
            });
        }

        return services;
    }

    /// <summary>Sirve /media/** desde la carpeta local. Solo aplica con Storage:Provider=Local.</summary>
    public static WebApplication UseLocalMedia(this WebApplication app)
    {
        if (!string.Equals(app.Configuration["Storage:Provider"], "Local", StringComparison.OrdinalIgnoreCase))
            return app;

        var local = (LocalImageStorage)app.Services.GetRequiredService<IImageStorage>();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(local.RootPath),
            RequestPath = "/media",
            OnPrepareResponse = ctx =>
                ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable"
        });

        return app;
    }
}
