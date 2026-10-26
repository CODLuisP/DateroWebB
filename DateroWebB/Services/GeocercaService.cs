using DateroWebB.Models;

namespace DateroWebB.Services;

public class GeocercaService
{
    private readonly List<Geocerca> _geocercas;
    private readonly TiemposDinamicosSettings _tiemposDinamicos;
    private readonly Dictionary<string, HashSet<int>> _geocercasVisitadas;
    private readonly Dictionary<string, ModoRuta> _modoRutaPorPlaca;

    public event Func<string, Task> OnCambioModoRegreso;

    public GeocercaService(AppSettings settings)
    {
        _geocercas = settings.Geocercas is { Count: > 0 }
            ? settings.Geocercas
            : InicializarGeocercas();
        _tiemposDinamicos = settings.TiemposDinamicos;
        _geocercasVisitadas = new Dictionary<string, HashSet<int>>();
        _modoRutaPorPlaca = new Dictionary<string, ModoRuta>();
    }

    public int ObtenerTotalGeocercas() => _geocercas.Count;

    private List<Geocerca> InicializarGeocercas() =>
    [
        // GEOCERCAS DE IDA (1-4)
        new Geocerca { Id = 1, Name = "PACÍFICO",         CenterLat = -12.199407, CenterLon = -77.028789, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 2, Name = "GALENA",            CenterLat = -12.166361, CenterLon = -76.976759, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 3, Name = "CVA. ESPERANZA I",  CenterLat = -12.180301, CenterLon = -76.943743, Active = true, Tipo = TipoGeocerca.Ida },
        new Geocerca { Id = 4, Name = "PARADERO 15",       CenterLat = -12.173399, CenterLon = -76.916099, Active = true, Tipo = TipoGeocerca.Ida },

        // GEOCERCAS DE REGRESO (5-8)
        new Geocerca { Id = 5, Name = "CVA. ESPERANZA",   CenterLat = -12.180073, CenterLon = -76.943156, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 6, Name = "CT",                CenterLat = -12.165106, CenterLon = -76.972775, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 7, Name = "FERRETERÍA",        CenterLat = -12.197397, CenterLon = -77.026855, Active = true, Tipo = TipoGeocerca.Regreso },
        new Geocerca { Id = 8, Name = "MIYASHIRO",         CenterLat = -12.203392, CenterLon = -77.032038, Active = true, Tipo = TipoGeocerca.Regreso },
    ];

    public void InicializarMonitoreoPlaca(string placa)
    {
        if (!_geocercasVisitadas.ContainsKey(placa))
            _geocercasVisitadas[placa] = new HashSet<int>();

        _modoRutaPorPlaca[placa] = ModoRuta.Ida;
        Console.WriteLine($"[GEOCERCA] 🔄 Modo inicial: IDA (detectando cruces izquierda→derecha)");
    }

    public void LimpiarMonitoreoPlaca(string placa)
    {
        _geocercasVisitadas.Remove(placa);
        _modoRutaPorPlaca.Remove(placa);
    }

    public Geocerca VerificarCruceGeocerca(string placa, double longitudActual, double latitudActual)
    {
        if (!_geocercasVisitadas.ContainsKey(placa))
            InicializarMonitoreoPlaca(placa);

        if (!_modoRutaPorPlaca.ContainsKey(placa))
            _modoRutaPorPlaca[placa] = ModoRuta.Ida;

        var modoActual = _modoRutaPorPlaca[placa];

        foreach (var geocerca in _geocercas.Where(g => g.Active))
        {
            if (geocerca.Id == 4) continue;
            if (geocerca.Id == 8) continue;
            if (_geocercasVisitadas[placa].Contains(geocerca.Id)) continue;
            if (modoActual == ModoRuta.Ida && geocerca.Tipo != TipoGeocerca.Ida) continue;
            if (modoActual == ModoRuta.Regreso && geocerca.Tipo != TipoGeocerca.Regreso) continue;

            bool cruzo = modoActual == ModoRuta.Ida
                ? longitudActual >= geocerca.CenterLon
                : longitudActual <= geocerca.CenterLon;

            if (cruzo)
            {
                _geocercasVisitadas[placa].Add(geocerca.Id);

                Console.WriteLine($"\n[GEOCERCA] 🎯 ════════════════════════════════════");
                Console.WriteLine($"[GEOCERCA]    ¡GEOCERCA DETECTADA!");
                Console.WriteLine($"[GEOCERCA]    Geocerca: {geocerca.Name}");
                Console.WriteLine($"[GEOCERCA]    Latitud actual: {latitudActual:F6}");
                Console.WriteLine($"[GEOCERCA]    Longitud actual: {longitudActual:F6}");
                Console.WriteLine($"[GEOCERCA]    Longitud geocerca: {geocerca.CenterLon:F6}");
                Console.WriteLine($"[GEOCERCA]    Modo: {modoActual}");
                Console.WriteLine($"[GEOCERCA] ════════════════════════════════════\n");

                return geocerca;
            }
        }

        return null;
    }

    public ModoRuta ObtenerModoActual(string placa) =>
        _modoRutaPorPlaca.TryGetValue(placa, out var modo) ? modo : ModoRuta.Ida;

    public HashSet<int> ObtenerGeocercasVisitadas(string placa) =>
        _geocercasVisitadas.TryGetValue(placa, out var visitadas) ? visitadas : new HashSet<int>();

    public int[] ObtenerMinutosDinamicos(DateTime horaDespacho)
    {
        int totalSeconds = horaDespacho.Hour * 3600 + horaDespacho.Minute * 60 + horaDespacho.Second;

        if (_tiemposDinamicos?.RangosHorarios is { Count: > 0 })
        {
            foreach (var range in _tiemposDinamicos.RangosHorarios)
            {
                int start = (int)TimeSpan.Parse(range.HoraInicio).TotalSeconds;
                int end = (int)TimeSpan.Parse(range.HoraFin).TotalSeconds;
                if (totalSeconds >= start && totalSeconds <= end)
                    return range.Minutos;
            }

            if (_tiemposDinamicos.MinutosPorDefecto is { Length: > 0 })
                return _tiemposDinamicos.MinutosPorDefecto;
        }

        return [5, 40, 70, 85, 100, 125, 165, 171];
    }

    public void MarcarGeocercaVisitada(string placa, int geocercaId)
    {
        if (!_geocercasVisitadas.ContainsKey(placa))
            _geocercasVisitadas[placa] = new HashSet<int>();

        _geocercasVisitadas[placa].Add(geocercaId);
    }

    public void CambiarModo(string placa, ModoRuta nuevoModo)
    {
        _modoRutaPorPlaca[placa] = nuevoModo;

        if (nuevoModo == ModoRuta.Regreso)
            _ = OnCambioModoRegreso?.Invoke(placa);
    }
}

public enum ModoRuta
{
    Ida,
    Regreso
}
