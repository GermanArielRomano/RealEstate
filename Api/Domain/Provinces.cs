namespace RealEstate.Domain;

/// <summary>Las 23 provincias y la Ciudad Autónoma de Buenos Aires. El panel ofrece esta lista; la API la hace cumplir.</summary>
public static class Provinces
{
    public static readonly string[] All =
    [
        "Buenos Aires", "Catamarca", "Chaco", "Chubut", "Ciudad Autónoma de Buenos Aires", "Córdoba", "Corrientes",
        "Entre Ríos", "Formosa", "Jujuy", "La Pampa", "La Rioja", "Mendoza", "Misiones", "Neuquén", "Río Negro",
        "Salta", "San Juan", "San Luis", "Santa Cruz", "Santa Fe", "Santiago del Estero", "Tierra del Fuego", "Tucumán"
    ];

    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name) && All.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
}
