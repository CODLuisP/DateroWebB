using Microsoft.AspNetCore.SignalR.Client;
using DateroWebB.API;
using DateroWebB.Models;
using DateroWebB.Services;

namespace DateroWebB;

public class Program
{
    private static AppSettings _settings;
    private static HubConnection _despachoConnection;
    private static readonly Dictionary<string, MonitoreoGPSService> _monitoreosActivos = new();
    private static readonly Dictionary<string, DatosDespacho> _datosDespachos = new();
    private static string _username;
    private static ServidorHTTP _servidor;
    private static GeocercaService _geocercaService;
    private static APIEnvioService _apiEnvioService;
    private static readonly Dictionary<string, ControlParadero15> _controlParadero15 = new();

    private class UltimaPosicion
    {
        public double Latitud { get; set; }
        public double Longitud { get; set; }
        public DateTime Timestamp { get; set; }
        public double Velocidad { get; set; }
    }

    private class ControlParadero15
    {
        public double LongitudReferenciaFija { get; set; }
        public double? LongitudReferenciaActual { get; set; } = null;
        public UltimaPosicion UltimaPosicionReferencia { get; set; } = null;
        public bool YaPaso { get; set; } = false;

        // Fallback: línea imaginaria en -76.920573
        public double LongitudLineaFallback { get; set; }
        public bool FallbackPasoAlEste { get; set; } = false;
    }

    private static ControlParadero15 CrearControlParadero15() => new()
    {
        LongitudReferenciaFija = _settings.Paradero15.LongitudReferenciaFija,
        LongitudLineaFallback = _settings.Paradero15.LongitudLineaFallback
    };

    private class DatosDespacho
    {
        public string Placa { get; set; }
        public string Codasig { get; set; }
        public DateTime HoraDespacho { get; set; }
        public int[] MinutosEsperados { get; set; }
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════╗");
        Console.WriteLine("║  Sistema de Control de Tiempos de Paraderos   ║");
        Console.WriteLine("╚════════════════════════════════════════════════╝");
        Console.WriteLine();

        _settings = AppSettings.Cargar();
        _username = _settings.ConfiguracionGeneral.Username;

        _geocercaService = new GeocercaService(_settings);
        _apiEnvioService = new APIEnvioService(_settings.ServiciosExternos);
        Console.WriteLine($"✓ Usuario configurado: {_username}");
        Console.WriteLine($"✓ Sistema de geocercas inicializado ({_geocercaService.ObtenerTotalGeocercas()} paraderos)\n");

        _servidor = new ServidorHTTP(_settings.ServidorHTTP.Puerto);
        _servidor.OnIniciarMonitoreo += IniciarMonitoreoAsync;
        _servidor.Iniciar();

        _despachoConnection = new HubConnectionBuilder()
            .WithUrl(_settings.ServiciosExternos.DespachoHubUrl)
            .WithAutomaticReconnect()
            .Build();

        _despachoConnection.On<RecentUrbano>("ActualizarDatosUrbano", async (datos) =>
        {
            await ManejarActualizacionDespacho(datos);
        });

        try
        {
            await _despachoConnection.StartAsync();
            Console.WriteLine($"[DESPACHO] ✓ Conectado al Hub de Despachos (opcional)\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Hub de Despachos no disponible: {ex.Message}");
            Console.WriteLine("   (No es crítico, se usará solo la API HTTP)\n");
        }

        await MenuPrincipal();
    }

    private static async Task<bool> IniciarMonitoreoAsync(IniciarMonitoreoRequest datos)
    {
        try
        {
            var placa = datos.Placa.ToUpper();
            var usuario = string.IsNullOrEmpty(datos.Usuario) ? _username : datos.Usuario;
            var codasig = datos.Codasig;
            var fechaIni = datos.FechaIni;

            Console.WriteLine($"\n🚀 Iniciando monitoreo automático...");
            Console.WriteLine($"   Placa:        {placa}");
            Console.WriteLine($"   Usuario:      {usuario}");
            Console.WriteLine($"   Codasig:      {codasig}");
            Console.WriteLine($"   Hora Inicio:  {fechaIni}");

            DateTime horaDespacho;
            if (!string.IsNullOrEmpty(fechaIni))
            {
                var partes = fechaIni.Split(':');
                var hoy = DateTime.Today;
                horaDespacho = new DateTime(hoy.Year, hoy.Month, hoy.Day,
                    int.Parse(partes[0]), int.Parse(partes[1]), 0);
            }
            else
            {
                horaDespacho = DateTime.Now;
            }

            var minutosEsperados = _geocercaService.ObtenerMinutosDinamicos(horaDespacho);

            Console.WriteLine($"\n📊 Tiempos esperados calculados:");
            for (int i = 0; i < minutosEsperados.Length; i++)
            {
                var horaEsperada = horaDespacho.AddMinutes(minutosEsperados[i]);
                Console.WriteLine($"   Geocerca {i + 1}: +{minutosEsperados[i]} min → {horaEsperada:HH:mm:ss}");
            }
            Console.WriteLine();

            _datosDespachos[placa] = new DatosDespacho
            {
                Placa = placa,
                Codasig = codasig,
                HoraDespacho = horaDespacho,
                MinutosEsperados = minutosEsperados
            };

            _geocercaService.InicializarMonitoreoPlaca(placa);
            _controlParadero15[placa] = CrearControlParadero15();
            Console.WriteLine($"[PARADERO15] Control inicializado con referencia: {_settings.Paradero15.LongitudReferenciaFija:F6}\n");

            _geocercaService.OnCambioModoRegreso += async (placaCambio) =>
            {
                await _apiEnvioService.ActualizarRutaact(placaCambio);
            };

            if (_monitoreosActivos.ContainsKey(placa))
            {
                Console.WriteLine($"⚠️  Ya existe monitoreo para {placa}, reiniciando...");
                await _monitoreosActivos[placa].Desconectar();
                _monitoreosActivos.Remove(placa);
            }

            var monitoreo = new MonitoreoGPSService(usuario, _settings.ServiciosExternos);
            monitoreo.OnDatosGPSRecibidos += (datosGPS) => ManejarActualizacionGPS(placa, datosGPS);

            await monitoreo.ConectarYMonitorear(placa);
            _monitoreosActivos[placa] = monitoreo;

            Console.WriteLine($"✅ Monitoreo iniciado exitosamente para {placa}");
            Console.WriteLine($"🎯 Esperando detección de geocercas...\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error al iniciar monitoreo: {ex.Message}\n");
            return false;
        }
    }

    private static double CalcularDistanciaHaversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static DateTime AjustarHoraLlegada(
        double latPunto, double lonPunto, DateTime horaPunto,
        double latGeocerca, double lonGeocerca, ModoRuta modo)
    {
        double distanciaMetros = CalcularDistanciaHaversine(latPunto, lonPunto, latGeocerca, lonGeocerca);
        double velocidadFijaKmh = _settings.ConfiguracionGeneral.VelocidadFijaKmh;
        double velocidadMS = velocidadFijaKmh / 3.6;
        double tiempoSegundos = distanciaMetros / velocidadMS;
        DateTime horaAjustada = horaPunto.AddSeconds(-tiempoSegundos);

        Console.WriteLine($"[AJUSTE] ════════════════════════════════════");
        Console.WriteLine($"[AJUSTE] Modo: {modo}");
        Console.WriteLine($"[AJUSTE] Punto detectado: {lonPunto:F6}, {latPunto:F6}");
        Console.WriteLine($"[AJUSTE] Geocerca: {lonGeocerca:F6}, {latGeocerca:F6}");
        Console.WriteLine($"[AJUSTE] Distancia: {distanciaMetros:F2} metros");
        Console.WriteLine($"[AJUSTE] Velocidad: {velocidadFijaKmh:F2} km/h ({velocidadMS:F2} m/s)");
        Console.WriteLine($"[AJUSTE] Tiempo calculado: {tiempoSegundos:F2} segundos");
        Console.WriteLine($"[AJUSTE] Hora original: {horaPunto:HH:mm:ss}");
        Console.WriteLine($"[AJUSTE] Hora ajustada: {horaAjustada:HH:mm:ss}");
        Console.WriteLine($"[AJUSTE] ════════════════════════════════════");

        return horaAjustada;
    }

    private static async Task MenuPrincipal()
    {
        while (true)
        {
            Console.WriteLine("\n╔════════════════════════════════════════╗");
            Console.WriteLine("║              MENÚ PRINCIPAL            ║");
            Console.WriteLine("╠════════════════════════════════════════╣");
            Console.WriteLine("║ 1. Ver monitoreos activos              ║");
            Console.WriteLine("║ 2. Detener monitoreo de una placa      ║");
            Console.WriteLine("║ 3. Detener todos los monitoreos        ║");
            Console.WriteLine("║ 4. Estado del servidor                 ║");
            Console.WriteLine("║ 5. Salir                               ║");
            Console.WriteLine("╚════════════════════════════════════════╝");
            Console.Write("Seleccione una opción: ");

            switch (Console.ReadLine())
            {
                case "1": MostrarMonitoreosActivos(); break;
                case "2": await DetenerMonitoreoEspecifico(); break;
                case "3": await DetenerTodosLosMonitoreos(); break;
                case "4": MostrarEstadoServidor(); break;
                case "5": await Salir(); return;
                default: Console.WriteLine("❌ Opción inválida"); break;
            }
        }
    }

    private static void MostrarMonitoreosActivos()
    {
        Console.WriteLine("\n╔════════════════════════════════════════╗");
        Console.WriteLine("║        MONITOREOS ACTIVOS              ║");
        Console.WriteLine("╚════════════════════════════════════════╝");

        if (_monitoreosActivos.Count == 0)
        {
            Console.WriteLine("⚠️  No hay monitoreos activos");
            return;
        }

        foreach (var (placa, monitoreo) in _monitoreosActivos)
        {
            Console.WriteLine($"   🚌 {placa}: {(monitoreo.EstaConectado() ? "✅ Activo" : "❌ Desconectado")}");
            if (_datosDespachos.TryGetValue(placa, out var datos))
            {
                Console.WriteLine($"      Codasig: {datos.Codasig}");
                Console.WriteLine($"      Despacho: {datos.HoraDespacho:HH:mm:ss}");
            }
        }

        Console.WriteLine();
    }

    private static async Task DetenerMonitoreoEspecifico()
    {
        Console.Write("\n📋 Ingrese la placa a detener: ");
        var placa = Console.ReadLine()?.Trim().ToUpper();

        if (string.IsNullOrEmpty(placa))
        {
            Console.WriteLine("❌ Placa inválida");
            return;
        }

        if (!_monitoreosActivos.ContainsKey(placa))
        {
            Console.WriteLine($"⚠️  No hay monitoreo activo para {placa}");
            return;
        }

        await _monitoreosActivos[placa].Desconectar();
        _monitoreosActivos.Remove(placa);
        _datosDespachos.Remove(placa);
        _controlParadero15.Remove(placa);
        _geocercaService.LimpiarMonitoreoPlaca(placa);

        Console.WriteLine($"✓ Monitoreo detenido para {placa}");
    }

    private static async Task DetenerTodosLosMonitoreos()
    {
        Console.WriteLine("\n🛑 Deteniendo todos los monitoreos...");

        foreach (var (placa, monitoreo) in _monitoreosActivos)
        {
            await monitoreo.Desconectar();
            _geocercaService.LimpiarMonitoreoPlaca(placa);
        }

        _monitoreosActivos.Clear();
        _datosDespachos.Clear();
        _controlParadero15.Clear();
        Console.WriteLine("✓ Todos los monitoreos detenidos");
    }

    private static void MostrarEstadoServidor()
    {
        Console.WriteLine("\n╔════════════════════════════════════════╗");
        Console.WriteLine("║         ESTADO DEL SERVIDOR            ║");
        Console.WriteLine("╚════════════════════════════════════════╝");
        Console.WriteLine($"Usuario:              {_username}");
        Console.WriteLine($"Puerto HTTP:          {_settings.ServidorHTTP.Puerto}");
        Console.WriteLine($"Monitoreos activos:   {_monitoreosActivos.Count}");
        Console.WriteLine($"Hub Despacho:         {(_despachoConnection?.State == HubConnectionState.Connected ? "Conectado" : "Desconectado")}");
        Console.WriteLine($"Endpoint API:         http://localhost:{_settings.ServidorHTTP.Puerto}/api/iniciar-monitoreo");
        Console.WriteLine($"API Envío:            {_settings.ServiciosExternos.ApiEnvioControlUrl}");
        Console.WriteLine("════════════════════════════════════════\n");
    }

    private static async Task Salir()
    {
        Console.WriteLine("\n🛑 Cerrando aplicación...");

        _servidor?.Detener();

        foreach (var monitoreo in _monitoreosActivos.Values)
            await monitoreo.Desconectar();

        if (_despachoConnection != null)
            await _despachoConnection.StopAsync();

        Console.WriteLine("✓ Aplicación cerrada correctamente");
        Console.WriteLine("Presione una tecla para salir...");
        Console.ReadKey();
    }

    private static async Task ManejarActualizacionDespacho(RecentUrbano datos)
    {
        if (datos.Isruta == "1" && !string.IsNullOrEmpty(datos.DeviceID))
        {
            await IniciarMonitoreoAsync(new IniciarMonitoreoRequest
            {
                Placa = datos.DeviceID,
                Usuario = _username,
                FechaIni = datos.Fechaini,
                Codasig = "0"
            });
        }
    }

    private static void ManejarActualizacionGPS(string placa, DatosVehiculo datos)
    {
        if (datos?.Vehiculo == null) return;

        var v = datos.Vehiculo;

        Console.WriteLine($"\n📍 ══════ GPS [{placa}] ══════");
        Console.WriteLine($"📍 Lat/Lon:    {v.lastValidLatitude:F6}, {v.lastValidLongitude:F6}");
        Console.WriteLine($"⚡ Velocidad:  {v.lastValidSpeed:F1} km/h | 🧭 Rumbo: {v.lastValidHeading:F0}°");
        Console.WriteLine($"📍 Dirección:  {v.Direccion ?? "N/A"}");
        Console.WriteLine($"🕐 {datos.FechaActual:HH:mm:ss}");

        var modoActual = _geocercaService.ObtenerModoActual(placa);
        var geocercasVisitadas = _geocercaService.ObtenerGeocercasVisitadas(placa);

        if (modoActual == ModoRuta.Ida && !geocercasVisitadas.Contains(_settings.Paradero15.Id))
        {
            var paradero15 = new Geocerca
            {
                Id = _settings.Paradero15.Id,
                Name = _settings.Paradero15.Name,
                CenterLat = _settings.Paradero15.CenterLat,
                CenterLon = _settings.Paradero15.CenterLon
            };

            var geocercaDetectada = ProcesarParadero15(
                placa, v.lastValidLatitude, v.lastValidLongitude,
                datos.FechaActual, v.lastValidSpeed, paradero15);

            if (geocercaDetectada != null)
            {
                var posicionEnviar = _controlParadero15[placa].UltimaPosicionReferencia;

                DateTime horaAjustada = AjustarHoraLlegada(
                    posicionEnviar.Latitud, posicionEnviar.Longitud, posicionEnviar.Timestamp,
                    paradero15.CenterLat, paradero15.CenterLon, ModoRuta.Ida);

                _geocercaService.MarcarGeocercaVisitada(placa, _settings.Paradero15.Id);
                _geocercaService.CambiarModo(placa, ModoRuta.Regreso);

                Console.WriteLine($"\n[GEOCERCA] 🔄 ════════════════════════════════════");
                Console.WriteLine($"[GEOCERCA]    CAMBIO DE MODO: IDA → REGRESO");
                Console.WriteLine($"[GEOCERCA]    Llegó a {paradero15.Name} (punto de vuelta)");
                Console.WriteLine($"[GEOCERCA] ════════════════════════════════════\n");

                _ = ProcesarLlegadaGeocerca(placa, geocercaDetectada,
                    posicionEnviar.Latitud, posicionEnviar.Longitud, horaAjustada);
            }
        }

        // Fallback: línea imaginaria en lon -76.920573
        if (modoActual == ModoRuta.Ida &&
            !geocercasVisitadas.Contains(_settings.Paradero15.Id) &&
            _controlParadero15.TryGetValue(placa, out var ctrlFallback))
        {
            var lonActual = v.lastValidLongitude;

            if (!ctrlFallback.FallbackPasoAlEste && lonActual >= ctrlFallback.LongitudLineaFallback)
            {
                ctrlFallback.FallbackPasoAlEste = true;
                Console.WriteLine($"[FALLBACK] ➡️ Bus cruzó línea imaginaria hacia el este ({lonActual:F6})");
            }
            else if (ctrlFallback.FallbackPasoAlEste && lonActual < ctrlFallback.LongitudLineaFallback)
            {
                Console.WriteLine($"\n[FALLBACK] 🔄 ════════════════════════════════════");
                Console.WriteLine($"[FALLBACK]    ¡CAMBIO DE SENTIDO POR FALLBACK!");
                Console.WriteLine($"[FALLBACK]    Bus regresó al oeste de la línea imaginaria ({lonActual:F6})");
                Console.WriteLine($"[FALLBACK] ════════════════════════════════════\n");

                var geocercaP15 = new Geocerca
                {
                    Id = _settings.Paradero15.Id,
                    Name = _settings.Paradero15.Name,
                    CenterLat = _settings.Paradero15.CenterLat,
                    CenterLon = _settings.Paradero15.CenterLon
                };

                // Usar UltimaPosicionReferencia si está disponible, si no la posición actual
                var posRef = ctrlFallback.UltimaPosicionReferencia;
                double latEnvio   = posRef?.Latitud    ?? v.lastValidLatitude;
                double lonEnvio   = posRef?.Longitud   ?? v.lastValidLongitude;
                DateTime tsEnvio  = posRef?.Timestamp  ?? datos.FechaActual;

                DateTime horaAjustada = AjustarHoraLlegada(
                    latEnvio, lonEnvio, tsEnvio,
                    geocercaP15.CenterLat, geocercaP15.CenterLon, ModoRuta.Ida);

                _geocercaService.MarcarGeocercaVisitada(placa, _settings.Paradero15.Id);
                _geocercaService.CambiarModo(placa, ModoRuta.Regreso);

                _ = ProcesarLlegadaGeocerca(placa, geocercaP15, latEnvio, lonEnvio, horaAjustada);
            }
        }

        var geocercaCruzada = _geocercaService.VerificarCruceGeocerca(
            placa, v.lastValidLongitude, v.lastValidLatitude);

        if (geocercaCruzada != null)
        {
            DateTime horaAjustada = AjustarHoraLlegada(
                v.lastValidLatitude, v.lastValidLongitude, datos.FechaActual,
                geocercaCruzada.CenterLat, geocercaCruzada.CenterLon, modoActual);

            _ = ProcesarLlegadaGeocerca(placa, geocercaCruzada,
                v.lastValidLatitude, v.lastValidLongitude, horaAjustada);
        }

        // Geocerca circular MIYASHIRO (Id=8) — solo en modo Regreso
        if (modoActual == ModoRuta.Regreso && !geocercasVisitadas.Contains(_settings.Miyashiro.Id))
        {
            double latMiyashiro   = _settings.Miyashiro.CenterLat;
            double lonMiyashiro   = _settings.Miyashiro.CenterLon;
            double radioMiyashiro = _settings.Miyashiro.RadioMetros;

            double distMiyashiro = CalcularDistanciaHaversine(
                v.lastValidLatitude, v.lastValidLongitude,
                latMiyashiro, lonMiyashiro);

            Console.WriteLine($"[MIYASHIRO] Distancia al centro: {distMiyashiro:F1} m");

            if (distMiyashiro <= radioMiyashiro)
            {
                Console.WriteLine($"\n[MIYASHIRO] 🏁 ════════════════════════════════════");
                Console.WriteLine($"[MIYASHIRO]    ¡DENTRO DE GEOCERCA CIRCULAR!");
                Console.WriteLine($"[MIYASHIRO]    Distancia: {distMiyashiro:F1} m / Radio: {radioMiyashiro} m");
                Console.WriteLine($"[MIYASHIRO] ════════════════════════════════════\n");

                var geocercaMiyashiro = new Geocerca
                {
                    Id = _settings.Miyashiro.Id,
                    Name = _settings.Miyashiro.Name,
                    CenterLat = latMiyashiro,
                    CenterLon = lonMiyashiro
                };

                _geocercaService.MarcarGeocercaVisitada(placa, _settings.Miyashiro.Id);

                DateTime horaAjustada = AjustarHoraLlegada(
                    v.lastValidLatitude, v.lastValidLongitude, datos.FechaActual,
                    latMiyashiro, lonMiyashiro, ModoRuta.Regreso);

                _ = ProcesarLlegadaGeocerca(placa, geocercaMiyashiro,
                    v.lastValidLatitude, v.lastValidLongitude, horaAjustada);
            }
        }

        Console.WriteLine("═══════════════════════════════\n");
    }

    private static Geocerca ProcesarParadero15(
        string placa, double latitudActual, double longitudActual,
        DateTime timestampActual, double velocidadActual, Geocerca geocercaParadero15)
    {
        if (!_controlParadero15.ContainsKey(placa))
            _controlParadero15[placa] = CrearControlParadero15();

        var control = _controlParadero15[placa];

        if (_geocercaService.ObtenerGeocercasVisitadas(placa).Contains(_settings.Paradero15.Id))
            return null;

        Console.WriteLine($"[PARADERO15] Evaluando posición: {longitudActual:F6}");

        if (!control.YaPaso)
        {
            if (longitudActual >= control.LongitudReferenciaFija)
            {
                control.YaPaso = true;
                control.LongitudReferenciaActual = longitudActual;
                control.UltimaPosicionReferencia = new UltimaPosicion
                {
                    Latitud = latitudActual,
                    Longitud = longitudActual,
                    Timestamp = timestampActual,
                    Velocidad = velocidadActual
                };
                Console.WriteLine($"[PARADERO15] ✓ Pasó punto fijo inicial");
            }
            else
            {
                Console.WriteLine($"[PARADERO15] Aún a la izquierda del punto fijo ({control.LongitudReferenciaFija:F6})");
            }
            return null;
        }

        if (longitudActual > control.LongitudReferenciaActual.Value)
        {
            control.LongitudReferenciaActual = longitudActual;
            control.UltimaPosicionReferencia = new UltimaPosicion
            {
                Latitud = latitudActual,
                Longitud = longitudActual,
                Timestamp = timestampActual
            };
            Console.WriteLine($"[PARADERO15] ➡️ Avanzando al este. Referencia: {longitudActual:F6}");
            return null;
        }
        else if (longitudActual < control.LongitudReferenciaActual.Value)
        {
            Console.WriteLine($"\n[PARADERO15] 🎯 ¡PUNTO DE RETORNO DETECTADO!");
            Console.WriteLine($"[PARADERO15]    Última posición al este: {control.LongitudReferenciaActual.Value:F6}");
            Console.WriteLine($"[PARADERO15]    Posición actual (regresando): {longitudActual:F6}");
            return geocercaParadero15;
        }

        return null;
    }

    private static async Task ProcesarLlegadaGeocerca(
        string placa, Geocerca geocerca,
        double latitud, double longitud, DateTime horaLlegada)
    {
        try
        {
            if (!_datosDespachos.TryGetValue(placa, out var datosDespacho))
            {
                Console.WriteLine($"⚠️  No hay datos de despacho para {placa}");
                return;
            }

            int indice = geocerca.Id - 1;
            if (indice < 0 || indice >= datosDespacho.MinutosEsperados.Length)
            {
                Console.WriteLine($"⚠️  Índice de geocerca inválido: {indice}");
                return;
            }

            var minutosEsperados = datosDespacho.MinutosEsperados[indice];
            var horaEsperada = datosDespacho.HoraDespacho.AddMinutes(minutosEsperados);
            var diferencia = horaLlegada - horaEsperada;
            var diferenciaMinutos = (int)diferencia.TotalMinutes;
            string volado = FormatearVolado(diferencia);

            Console.WriteLine($"📊 CÁLCULO DE TIEMPOS:");
            Console.WriteLine($"   Hora Despacho:  {datosDespacho.HoraDespacho:HH:mm:ss}");
            Console.WriteLine($"   Hora Esperada:  {horaEsperada:HH:mm:ss} (+{minutosEsperados} min)");
            Console.WriteLine($"   Hora Llegada:   {horaLlegada:HH:mm:ss}");
            Console.WriteLine($"   Diferencia:     {volado} {(diferenciaMinutos > 0 ? "(RETRASO)" : diferenciaMinutos < 0 ? "(ADELANTO)" : "(A TIEMPO)")}");
            Console.WriteLine($"   Coordenadas:    {latitud:F6}, {longitud:F6}");

            if (geocerca.Id == _settings.Miyashiro.Id)
            {
                Console.WriteLine($"\n🏁 ¡RUTA COMPLETADA! Bus {placa} llegó a la última geocerca");

                bool finalizadoEnBD = await _apiEnvioService.EndRuta(placa);
                if (finalizadoEnBD)
                    Console.WriteLine($"✅ Ruta finalizada en base de datos");

                Console.WriteLine($"   Deteniendo monitoreo en {_settings.ConfiguracionGeneral.FinalizarMonitoreoDelayMs / 1000} segundos...\n");

                _ = Task.Run(async () =>
                {
                    await Task.Delay(_settings.ConfiguracionGeneral.FinalizarMonitoreoDelayMs);
                    if (_monitoreosActivos.ContainsKey(placa))
                    {
                        await _monitoreosActivos[placa].Desconectar();
                        _monitoreosActivos.Remove(placa);
                        _datosDespachos.Remove(placa);
                        _controlParadero15.Remove(placa);
                        _geocercaService.LimpiarMonitoreoPlaca(placa);
                        Console.WriteLine($"\n✓ Monitoreo de {placa} finalizado\n");
                    }
                });
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error procesando llegada: {ex.Message}\n");
        }
    }

    private static string FormatearVolado(TimeSpan diferencia)
    {
        bool esNegativo = diferencia.TotalSeconds < 0;
        var abs = diferencia.Duration();
        int horas = (int)abs.TotalHours;
        string signo = esNegativo ? "-" : "";

        return horas > 0
            ? $"{signo}{horas}h {abs.Minutes}min {abs.Seconds}seg"
            : abs.Minutes > 0
                ? $"{signo}{abs.Minutes}min {abs.Seconds}seg"
                : $"{signo}{abs.Seconds}seg";
    }
}
