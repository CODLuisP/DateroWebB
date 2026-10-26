using Microsoft.Extensions.Configuration;

namespace DateroWebB.Models;

public class AppSettings
{
    public ConfiguracionGeneralSettings ConfiguracionGeneral { get; set; } = new();
    public ServidorHttpSettings ServidorHTTP { get; set; } = new();
    public ServiciosExternosSettings ServiciosExternos { get; set; } = new();
    public Paradero15Settings Paradero15 { get; set; } = new();
    public MiyashiroSettings Miyashiro { get; set; } = new();
    public List<Geocerca> Geocercas { get; set; } = [];
    public TiemposDinamicosSettings TiemposDinamicos { get; set; } = new();

    public static AppSettings Cargar()
    {
        var basePath = File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            ? AppContext.BaseDirectory
            : Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        return configuration.Get<AppSettings>() ?? new AppSettings();
    }
}

public class ConfiguracionGeneralSettings
{
    public string Username { get; set; } = "etudvrb";
    public double VelocidadFijaKmh { get; set; } = 40.0;
    public int FinalizarMonitoreoDelayMs { get; set; } = 30000;
}

public class ServidorHttpSettings
{
    public int Puerto { get; set; } = 5003;
}

public class ServiciosExternosSettings
{
    public string BaseUrl { get; set; } = "https://villa.velsat.pe:8443";
    public string DespachoHubUrl { get; set; } = "https://villa.velsat.pe:8443/urbanoHub";
    public string GpsHubUrl { get; set; } = "https://villa.velsat.pe:8443/dataHubVehicle";
    public string ApiEnvioControlUrl { get; set; } = "https://villa.velsat.pe:8443/api/Datero/EnvioControlB/etudvrb";
    public string ApiActualizarRutaactUrl { get; set; } = "https://villa.velsat.pe:8443/api/Datero/ActualizarRutaact";
    public string ApiEndRutaUrl { get; set; } = "https://villa.velsat.pe:8443/api/Datero/endruta";
    public int HttpTimeoutSegundos { get; set; } = 10;
    public bool IgnorarErroresSsl { get; set; } = true;
    public int GpsReconexionDelayMs { get; set; } = 5000;
}

public class Paradero15Settings
{
    public int Id { get; set; } = 4;
    public string Name { get; set; } = "PARADERO 15";
    public double CenterLat { get; set; } = -12.173399;
    public double CenterLon { get; set; } = -76.916099;
    public double LongitudReferenciaFija { get; set; } = -76.922273;
    public double LongitudLineaFallback { get; set; } = -76.920573;
}

public class MiyashiroSettings
{
    public int Id { get; set; } = 8;
    public string Name { get; set; } = "MIYASHIRO";
    public double CenterLat { get; set; } = -12.203392;
    public double CenterLon { get; set; } = -77.032038;
    public double RadioMetros { get; set; } = 200.0;
}

public class TiemposDinamicosSettings
{
    public int[] MinutosPorDefecto { get; set; } = [5, 40, 70, 85, 100, 125, 165, 171];
    public List<RangoHorarioSettings> RangosHorarios { get; set; } = [];
}

public class RangoHorarioSettings
{
    public string HoraInicio { get; set; } = string.Empty;
    public string HoraFin { get; set; } = string.Empty;
    public int[] Minutos { get; set; } = [];
}
