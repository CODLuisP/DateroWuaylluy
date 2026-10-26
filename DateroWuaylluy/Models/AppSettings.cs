using System.Text.Json;
using System.Text.Json.Serialization;
using DateroWuaylluy.Services;

namespace DateroWuaylluy.Models;

public class AppSettings
{
    public ConnectionStringsConfig ConnectionStrings { get; set; } = new();
    public GeneralConfig General { get; set; } = new();
    public ServidorHttpConfig ServidorHttp { get; set; } = new();
    public ApisConfig Apis { get; set; } = new();

    public Dictionary<string, CuentaConfig> Cuentas { get; set; } = new()
    {
        { "69", new CuentaConfig {
            Usuario           = "serfrymh",
            ApiActualizarRuta = "https://villa.velsat.pe:8443/api/Datero/ActualizarRutaactFR/{placa}/{rutaact}",
            ApiEnvioControl   = "https://villa.velsat.pe:8443/api/Datero/EnvioControlB/serfrymh"
        }},
        { "71", new CuentaConfig {
            Usuario           = "serfrymh",
            ApiActualizarRuta = "https://villa.velsat.pe:8443/api/Datero/ActualizarRutaactFR/{placa}/{rutaact}",
            ApiEnvioControl   = "https://villa.velsat.pe:8443/api/Datero/EnvioControlB/serfrymh"
        }},
        { "70", new CuentaConfig {
            Usuario           = "serfrymh",
            ApiActualizarRuta = "https://villa.velsat.pe:8443/api/Datero/ActualizarRutaactFR/{placa}/{rutaact}",
            ApiEnvioControl   = "https://villa.velsat.pe:8443/api/Datero/EnvioControlB/serfrymh"
        }},
        { "72", new CuentaConfig {
            Usuario           = "serfrymh",
            ApiActualizarRuta = "https://villa.velsat.pe:8443/api/Datero/ActualizarRutaactFR/{placa}/{rutaact}",
            ApiEnvioControl   = "https://villa.velsat.pe:8443/api/Datero/EnvioControlB/serfrymh"
        }}
    };

    public Dictionary<string, string> RutaVueltaPorIda { get; set; } = new()
    {
        { "69", "70" },
        { "71", "72" }
    };

    public DeteccionSentidoConfig DeteccionSentido { get; set; } = new();

    public List<ZonaDespachoConfig> ZonasDespacho { get; set; } = new()
    {
        new ZonaDespachoConfig { Nombre = "CALLAO",          Lat = -12.047575, Lon = -77.134993, RadioM = 200.0, ModoAlSalir = ModoRuta.Ida },
        new ZonaDespachoConfig { Nombre = "LIMA_WASHINGTON", Lat = -12.054023, Lon = -77.040955, RadioM = 200.0, ModoAlSalir = ModoRuta.Regreso },
        new ZonaDespachoConfig { Nombre = "FAUCETT",         Lat = -12.062550, Lon = -77.097378, RadioM = 150.0, ModoAlSalir = null }
    };

    public VigiaConfig Vigia { get; set; } = new();
    public MonitoreoEsperaConfig MonitoreoEspera { get; set; } = new();
    public LogsConfig Logs { get; set; } = new();

    public List<Geocerca> Geocercas { get; set; } =
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

    public int[] MinutosDinamicosGeocercas { get; set; } =
        [8, 13, 17, 24, 44, 52, 79, 84, 87, 107, 136, 140, 149, 154, 162, 172];

    public static AppSettings Cargar()
    {
        var opciones = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var rutaBase = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var rutaActual = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        var ruta = File.Exists(rutaBase) ? rutaBase : (File.Exists(rutaActual) ? rutaActual : null);

        if (ruta == null)
            return new AppSettings();

        var json = File.ReadAllText(ruta);
        return JsonSerializer.Deserialize<AppSettings>(json, opciones) ?? new AppSettings();
    }
}

public class ConnectionStringsConfig
{
    public string DefaultConnection { get; set; } = string.Empty;
}

public class GeneralConfig
{
    public string Username { get; set; } = "serfrymh";
    public int ZonaHorariaOffsetHoras { get; set; } = -5;
}

public class ServidorHttpConfig
{
    public int Puerto { get; set; } = 8080;
}

public class ApisConfig
{
    public string HubDespachoUrl { get; set; } = "https://villa.velsat.pe:8443/urbanoHub";
    public string HubGpsUrlTemplate { get; set; } = "https://villa.velsat.pe:8443/dataHubVehicle/{username}/{placa}";
    public string EndRutaUrlTemplate { get; set; } = "https://villa.velsat.pe:8443/api/Datero/endruta/{deviceID}";
    public string AsignarDespachoUrl { get; set; } = "https://villa.velsat.pe:8443/api/Caja/asignarFR";
    public int HttpTimeoutSegundos { get; set; } = 10;
    public int GpsReconexionDelayMs { get; set; } = 5000;
}

public class CuentaConfig
{
    public string Usuario { get; set; } = "serfrymh";
    public string ApiActualizarRuta { get; set; } = "https://villa.velsat.pe:8443/api/Datero/ActualizarRutaactFR/{placa}/{rutaact}";
    public string ApiEnvioControl { get; set; } = "https://villa.velsat.pe:8443/api/Datero/EnvioControlB/serfrymh";
}

public class DeteccionSentidoConfig
{
    public double LineaRetornoLon { get; set; } = -77.098966;
    public double LineaRetorno2Lon { get; set; } = -77.043779;
    public double LineaGiroCallaoOeste { get; set; } = -77.137;
    public double LineaGiroCallaoEste { get; set; } = -77.135;
    public double HisteresisLon { get; set; } = 0.0005;
    public double VelocidadFijaKmh { get; set; } = 40.0;
}

public class ZonaDespachoConfig
{
    public string Nombre { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double RadioM { get; set; }
    public ModoRuta? ModoAlSalir { get; set; }
}

public class VigiaConfig
{
    public int IntervaloSegundos { get; set; } = 60;
    public int TicksParaReparar { get; set; } = 2;
}

public class MonitoreoEsperaConfig
{
    public int HoraPausa { get; set; } = 23;
    public int HoraReanuda { get; set; } = 4;
    public int IntervaloRevisionNocturnaMinutos { get; set; } = 10;
    public int IntervaloRefrescoMinutos { get; set; } = 30;
}

public class LogsConfig
{
    public string Carpeta { get; set; } = "logs";
    public int DiasRetencion { get; set; } = 5;
}
