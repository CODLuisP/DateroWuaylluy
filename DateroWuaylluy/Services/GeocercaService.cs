using System.Collections.Concurrent;
using DateroWuaylluy.Models;

namespace DateroWuaylluy.Services;

public class GeocercaService
{
    private readonly List<Geocerca> _geocercas;
    private readonly int[] _minutosDinamicos;
    private readonly ConcurrentDictionary<string, HashSet<int>> _geocercasVisitadas;
    private readonly ConcurrentDictionary<string, ModoRuta> _modoRutaPorPlaca;

    public GeocercaService(List<Geocerca>? geocercas = null, int[]? minutosDinamicos = null)
    {
        _geocercas = geocercas is { Count: > 0 } ? geocercas : InicializarGeocercas();
        _minutosDinamicos = minutosDinamicos is { Length: > 0 }
            ? minutosDinamicos
            : [8, 13, 17, 24, 44, 52, 79, 84, 87, 107, 136, 140, 149, 154, 162, 172];
        _geocercasVisitadas = new ConcurrentDictionary<string, HashSet<int>>();
        _modoRutaPorPlaca = new ConcurrentDictionary<string, ModoRuta>();
    }

    private List<Geocerca> InicializarGeocercas() =>
    [
        // IDA (1-8)
        new Geocerca { Id = 1,  Name = "SAENZ PEÑA",  CenterLat = -12.061528, CenterLon = -77.143081, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 2,  Name = "GALVEZ 1",    CenterLat = -12.067819, CenterLon = -77.132825, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 3,  Name = "LA PERLA",    CenterLat = -12.064903, CenterLon = -77.118961, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 4,  Name = "FAUCETT",     CenterLat = -12.062550, CenterLon = -77.097378, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 5,  Name = "UNIV",        CenterLat = -12.060486, CenterLon = -77.079589, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 6,  Name = "ARICA",       CenterLat = -12.055722, CenterLon = -77.056903, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 7,  Name = "BOL/UGA",     CenterLat = -12.056222, CenterLon = -77.042228, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 8,  Name = "WASHNT",      CenterLat = -12.054292, CenterLon = -77.039587, Active = true, Tipo = TipoGeocerca.Ida },

        // REGRESO (9-16)
        new Geocerca { Id = 9,  Name = "METRO V",    CenterLat = -12.054483, CenterLon = -77.042614, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 10, Name = "ARICA V",    CenterLat = -12.055722, CenterLon = -77.056903, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 11, Name = "UNIV V",     CenterLat = -12.060486, CenterLon = -77.079589, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 12, Name = "FAUCETT V",  CenterLat = -12.062550, CenterLon = -77.097378, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 13, Name = "PERLA V",    CenterLat = -12.064903, CenterLon = -77.118961, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 14, Name = "GALVEZ V",   CenterLat = -12.067819, CenterLon = -77.132825, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 15, Name = "S PEÑA V",   CenterLat = -12.061528, CenterLon = -77.143081, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 16, Name = "MORA V",     CenterLat = -12.050669, CenterLon = -77.134264, Active = true, Tipo = TipoGeocerca.Regreso },
    ];

    public void InicializarMonitoreoPlaca(string placa)
    {
        if (!_geocercasVisitadas.ContainsKey(placa))
            _geocercasVisitadas[placa] = new HashSet<int>();

        _modoRutaPorPlaca[placa] = ModoRuta.Ida;
        Console.WriteLine($"[GEOCERCA] 🔄 Modo inicial: IDA");
    }

    public void LimpiarMonitoreoPlaca(string placa)
    {
        _geocercasVisitadas.TryRemove(placa, out _);
        _modoRutaPorPlaca.TryRemove(placa, out _);
    }

    public Geocerca VerificarCruceGeocerca(string placa, double longitudActual, double latitudActual)
    {
        if (!_geocercasVisitadas.ContainsKey(placa))
            InicializarMonitoreoPlaca(placa);

        if (!_modoRutaPorPlaca.ContainsKey(placa))
            _modoRutaPorPlaca[placa] = ModoRuta.Ida;

        var modoActual = _modoRutaPorPlaca[placa];
        var visitadas = _geocercasVisitadas[placa];

        // ── CORRECCIÓN: solo evaluar la SIGUIENTE geocerca esperada en secuencia ──
        // Determinar el rango de IDs según el modo actual
        // Ida: 1-8 (Washington=8 se maneja aparte, así que 1-7 aquí)
        // Regreso: 9-16
        int idMin = modoActual == ModoRuta.Ida ? 1 : 9;
        int idMax = modoActual == ModoRuta.Ida ? 7 : 16; // Washington (8) se excluye aquí

        // Encontrar el siguiente ID no visitado dentro del rango del modo actual
        int siguienteId = idMin;
        for (int id = idMin; id <= idMax; id++)
        {
            if (!visitadas.Contains(id))
            {
                siguienteId = id;
                break;
            }
            // Si todos fueron visitados, no hay siguiente
            if (id == idMax)
                return null;
        }

        // Obtener solo esa geocerca
        var geocerca = _geocercas.FirstOrDefault(g => g.Id == siguienteId && g.Active);
        if (geocerca == null) return null;

        // Evaluar si la cruzó
        bool cruzo = false;

        if (modoActual == ModoRuta.Ida)
        {
            // SAENZ PEÑA IDA (Id=1): cruce arriba→abajo (latitud)
            // Resto IDA: cruce izquierda→derecha (longitud)
            cruzo = geocerca.Id == 1
                ? latitudActual <= geocerca.CenterLat
                : longitudActual >= geocerca.CenterLon;
        }
        else
        {
            // S PEÑA V (15) y MORA V (16): cruce abajo→arriba (latitud)
            // Resto REGRESO: cruce derecha→izquierda (longitud)
            cruzo = geocerca.Id is 15 or 16
                ? latitudActual >= geocerca.CenterLat
                : longitudActual <= geocerca.CenterLon;
        }

        if (!cruzo) return null;

        visitadas.Add(geocerca.Id);

        Console.WriteLine($"\n[GEOCERCA] 🎯 ════════════════════════════════════");
        Console.WriteLine($"[GEOCERCA]    ¡GEOCERCA DETECTADA!");
        Console.WriteLine($"[GEOCERCA]    Geocerca: {geocerca.Name} (Id {geocerca.Id})");
        Console.WriteLine($"[GEOCERCA]    Lat: {latitudActual:F6} | Lon: {longitudActual:F6}");
        Console.WriteLine($"[GEOCERCA]    Centro: {geocerca.CenterLat:F6} | {geocerca.CenterLon:F6}");
        Console.WriteLine($"[GEOCERCA]    Modo: {modoActual}");
        Console.WriteLine($"[GEOCERCA] ════════════════════════════════════\n");

        return geocerca;
    }

    public ModoRuta ObtenerModoActual(string placa) =>
        _modoRutaPorPlaca.TryGetValue(placa, out var modo) ? modo : ModoRuta.Ida;

    public HashSet<int> ObtenerGeocercasVisitadas(string placa) =>
        _geocercasVisitadas.TryGetValue(placa, out var visitadas) ? visitadas : new HashSet<int>();

    public void MarcarGeocercaVisitada(string placa, int geocercaId)
    {
        if (!_geocercasVisitadas.ContainsKey(placa))
            _geocercasVisitadas[placa] = new HashSet<int>();

        _geocercasVisitadas[placa].Add(geocercaId);
    }

    public void CambiarModo(string placa, ModoRuta nuevoModo) =>
        _modoRutaPorPlaca[placa] = nuevoModo;

    public int[] ObtenerMinutosDinamicos(DateTime horaDespacho) =>
        _minutosDinamicos;

    public Geocerca ObtenerGeocercaPorId(int id) =>
        _geocercas.FirstOrDefault(g => g.Id == id);
}

public enum ModoRuta { Ida, Regreso }
