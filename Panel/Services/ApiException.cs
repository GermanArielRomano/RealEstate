using System.Text.Json;

namespace RealEstate.Panel.Services;

public sealed class ApiException(string message, int status = 0) : Exception(message)
{
    public int Status { get; } = status;

    /// <summary>Convierte la respuesta de error de la API (ProblemDetails / ValidationProblemDetails) en un mensaje legible.</summary>
    public static async Task<ApiException> FromAsync(HttpResponseMessage response)
    {
        var message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Forbidden => "No tienes permiso para realizar esta acción.",
            System.Net.HttpStatusCode.NotFound => "No se encontró el recurso solicitado.",
            System.Net.HttpStatusCode.TooManyRequests => "Demasiados intentos. Espera un momento e inténtalo de nuevo.",
            _ => $"Error {(int)response.StatusCode} del servidor."
        };

        try
        {
            var text = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.Array)
                    .SelectMany(p => p.Value.EnumerateArray().Select(e => e.GetString()))
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                var joined = string.Join(" ", messages);
                if (joined.Length > 0) message = joined;
            }
            else if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } d)
            {
                message = d;
            }
            else if (root.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } t)
            {
                message = t;
            }
        }
        catch (JsonException) { /* el cuerpo no era JSON: se queda el mensaje por defecto */ }

        return new ApiException(message, (int)response.StatusCode);
    }
}
