using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealEstate.Mobile.Models;
using RealEstate.Mobile.Services;

namespace RealEstate.Mobile.ViewModels;

public partial class PropertiesListViewModel : ObservableObject
{
    private const int PageSize = 20;

    private readonly IPropertiesService _service;
    private CancellationTokenSource? _cts;
    private int _page;
    private bool _hasMore = true;

    public PropertiesListViewModel(IPropertiesService service)
    {
        _service = service;
        Properties.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<PropertyCardItem> Properties { get; } = [];

    /// <summary>Enlazado (TwoWay) al RefreshView: también muestra el spinner en la carga inicial.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsRefreshing { get; set; }

    /// <summary>Spinner del pie de la lista mientras se carga la página siguiente.</summary>
    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool IsEmpty => Properties.Count == 0 && !IsRefreshing && !HasError;

    // ------------------------------------------------------------------
    //  Comandos (el generador crea InitializeCommand, RefreshCommand, ...)
    // ------------------------------------------------------------------

    /// <summary>Se llama desde OnAppearing; solo carga si todavía no hay datos.</summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (Properties.Count == 0 && !IsRefreshing)
            await LoadPageAsync(1, reset: true);
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadPageAsync(1, reset: true);

    /// <summary>RemainingItemsThresholdReachedCommand del CollectionView.</summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!_hasMore || IsRefreshing || IsLoadingMore || Properties.Count == 0)
            return;

        await LoadPageAsync(_page + 1, reset: false);
    }

    [RelayCommand]
    private Task ViewDetailsAsync(PropertyCardItem? item) =>
        item is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{AppRoutes.PropertyDetail}?id={item.Id}");

    // ------------------------------------------------------------------
    //  Carga
    // ------------------------------------------------------------------
    private async Task LoadPageAsync(int page, bool reset)
    {
        // Una carga nueva invalida la anterior (p. ej. pull-to-refresh mientras se paginaba)
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;

        ErrorMessage = null;
        if (reset) IsRefreshing = true; else IsLoadingMore = true;

        try
        {
            var result = await _service.GetAvailableAsync(page, PageSize, ct);

            // Se limpia DESPUÉS de recibir datos: evita el parpadeo de lista vacía al refrescar
            if (reset) Properties.Clear();

            foreach (var dto in result.Items)
                Properties.Add(new PropertyCardItem(dto));

            _page = result.Page;
            _hasMore = result.Items.Count > 0 && Properties.Count < result.TotalCount;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Reemplazada por una carga más nueva: ella se encarga del estado
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            // OperationCanceledException sin cancelación nuestra = timeout del HttpClient
            ErrorMessage = ex switch
            {
                HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }
                    => "Tu sesión expiró. Vuelve a iniciar sesión.",
                OperationCanceledException
                    => "El servidor está despertando (la demo gratuita se apaga al estar inactiva). Reintenta en unos segundos.",
                _ => "No pudimos cargar las propiedades. Revisa tu conexión e inténtalo de nuevo."
            };
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsRefreshing = false;
                IsLoadingMore = false;
            }
        }
    }
}
