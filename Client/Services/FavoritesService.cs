using System.Text.Json;
using Microsoft.JSInterop;

namespace RealEstate.Client.Services;

/// <summary>Favoritos del comprador, guardados en su propio navegador (no requiere cuenta).</summary>
public sealed class FavoritesService(IJSRuntime js)
{
    private const string Key = "re_favs";
    private HashSet<Guid>? _ids;

    public event Action? Changed;

    public int Count => _ids?.Count ?? 0;
    public IReadOnlyCollection<Guid> All => _ids ?? [];

    public bool IsFavorite(Guid id) => _ids?.Contains(id) == true;

    public async Task EnsureLoadedAsync()
    {
        if (_ids is not null) return;

        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            _ids = string.IsNullOrEmpty(raw) ? [] : JsonSerializer.Deserialize<HashSet<Guid>>(raw) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            _ids = [];
        }

        Changed?.Invoke();
    }

    public async Task ToggleAsync(Guid id)
    {
        await EnsureLoadedAsync();
        if (!_ids!.Remove(id)) _ids.Add(id);

        await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(_ids));
        Changed?.Invoke();
    }
}
