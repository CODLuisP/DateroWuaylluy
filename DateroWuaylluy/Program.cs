using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using DateroWuaylluy.API;
using DateroWuaylluy.Models;
using DateroWuaylluy.Services;

namespace DateroWuaylluy;

public class Program
{
    private static readonly AppSettings _config = AppSettings.Cargar();
    private static HubConnection _despachoConnection;
    private static readonly ConcurrentDictionary<string, MonitoreoGPSService> _monitoreosActivos = new();
    private static readonly ConcurrentDictionary<string, DatosDespacho> _datosDespachos = new();
    private static string _username => _config.General.Username;
    private static ServidorHTTP _servidor;
    private static GeocercaService _geocercaService;
    private static APIEnvioService _apiEnvioService;
    private static readonly ConcurrentDictionary<string, ControlWashington> _controlWashington = new();
    private static readonly ConcurrentDictionary<string, Cuenta> _cuentaPorPlaca = new();
    private static readonly ConcurrentDictionary<string, bool> _cruzoPuntoRetorno = new();
    private static readonly ConcurrentDictionary<string, bool> _cruzoPuntoRetorno2 = new();

    // Líneas de respaldo IDA→REGRESO (Callao→Lima, no llegó a Washington y regresó)
    private static double LINEA_RETORNO_LON  => _config.DeteccionSentido.LineaRetornoLon;  // cerca de Faucett
    private static double LINEA_RETORNO2_LON => _config.DeteccionSentido.LineaRetorno2Lon; // cerca de Washington

    // Línea de respaldo espejada REGRESO→IDA (Lima→Callao, no llegó a Callao y regresó)
    private static readonly ConcurrentDictionary<string, bool> _cruzoPuntoRetornoLima = new();

    // Giro en Callao (REGRESO→IDA): pasó al oeste de la línea OESTE y volvió al este de la línea ESTE
    private static double LINEA_GIRO_CALLAO_OESTE => _config.DeteccionSentido.LineaGiroCallaoOeste;
    private static double LINEA_GIRO_CALLAO_ESTE  => _config.DeteccionSentido.LineaGiroCallaoEste;
    private static readonly ConcurrentDictionary<string, bool> _pasoGiroCallao = new();

    // Margen al volver a cruzar una línea de respaldo, para que el jitter del GPS de un bus detenido junto a la línea no dispare un cambio de sentido
    private static double HISTERESIS_LON => _config.DeteccionSentido.HisteresisLon;

    // Cada sentido es un despacho propio en urbano_asigna: ida → vuelta
    private static Dictionary<string, string> _rutaVueltaPorIda => _config.RutaVueltaPorIda;

    private static readonly HashSet<string> _cambioSentidoEnCurso = new();

    // ── VIGÍA de monitoreos activos ─────────────────────────────────────
    private static int INTERVALO_VIGIA_SEG => _config.Vigia.IntervaloSegundos;
    private static int TICKS_PARA_REPARAR => _config.Vigia.TicksParaReparar; // el problema debe repetirse en 2 revisiones seguidas antes de actuar
    private static readonly ConcurrentDictionary<string, DateTime> _ultimoPingPorPlaca = new();
    private static readonly ConcurrentDictionary<string, long> _gpsBdPrevioPorPlaca = new();
    private static readonly ConcurrentDictionary<string, int> _sospechasPorPlaca = new();
    private static readonly object _cambioMonitorLock = new();
    private static DateTime _ultimaRevisionVigia = DateTime.MinValue;

    // ── ZONAS DE DESPACHO AUTOMÁTICO (solo primer despacho, bus en espera) ──
    // ModoAlSalir null = el sentido depende de hacia dónde sale el bus (este → Ida, oeste → Regreso)
    private record ZonaDespacho(string Nombre, double Lat, double Lon, double RadioM, ModoRuta? ModoAlSalir);

    private static readonly List<ZonaDespacho> _zonasDespacho = _config.ZonasDespacho
        .Select(z => new ZonaDespacho(z.Nombre, z.Lat, z.Lon, z.RadioM, z.ModoAlSalir))
        .ToList();

    // ── MONITOREO EN ESPERA (despacho automático) ──────────────────────
    private static DatabaseService _dbService;
    private static readonly ConcurrentDictionary<string, MonitoreoGPSService> _monitoreosEspera = new();
    private static readonly ConcurrentDictionary<string, string> _rutaDefaultPorPlaca = new(); // placa → rutadefault
    private static readonly ConcurrentDictionary<string, string> _codConductorPorPlaca = new(); // placa → codconductoract
    private static readonly ConcurrentDictionary<string, string> _zonaActualPorPlaca = new();   // placa → nombre de zona (si está dentro)
    private static bool _monitoreoEsperaActivo = false;
    private static DateOnly? _diaIniciado;

    private static int HORA_PAUSA  => _config.MonitoreoEspera.HoraPausa;
    private static int HORA_REANUDA => _config.MonitoreoEspera.HoraReanuda;

    private class UltimaPosicion
    {
        public double Latitud { get; set; }
        public double Longitud { get; set; }
        public DateTime Timestamp { get; set; }
        public double Velocidad { get; set; }
    }

    private class ControlWashington
    {
        public bool YaPasoLatitud { get; set; } = false;
        public bool YaPasoLongitud { get; set; } = false;
        public UltimaPosicion UltimaPosicionDeteccion { get; set; } = null;
    }

    private class DatosDespacho
    {
        public string Placa { get; set; }
        public string Codasig { get; set; }
        public DateTime HoraDespacho { get; set; }
        public int[] MinutosEsperados { get; set; } // ⚠️ ya no se calcula (ver IniciarMonitoreoAsync), queda vacío
        public string Codruta { get; set; }
    }

    private class Cuenta
    {
        public string Usuario { get; set; }
        public string ApiActualizarRuta { get; set; }
        public string ApiEnvioControl { get; set; }
    }

    // ✅ Clave por Codruta — configurado desde appsettings.json
    private static readonly Dictionary<string, Cuenta> _cuentas = _config.Cuentas.ToDictionary(
        kv => kv.Key,
        kv => new Cuenta
        {
            Usuario           = kv.Value.Usuario,
            ApiActualizarRuta = kv.Value.ApiActualizarRuta,
            ApiEnvioControl   = kv.Value.ApiEnvioControl
        });

    private static bool EsRutaVuelta(string codruta) => _rutaVueltaPorIda.ContainsValue(codruta);

    /// <summary>Código de ruta del sentido pedido a partir de cualquier código de la misma línea (69/70 o 71/72). Null si no se conoce.</summary>
    private static string RutaSegunSentido(string codruta, ModoRuta modo)
    {
        var rutaIda = _rutaVueltaPorIda.ContainsKey(codruta)
            ? codruta
            : _rutaVueltaPorIda.FirstOrDefault(kv => kv.Value == codruta).Key;

        if (rutaIda == null) return null;
        return modo == ModoRuta.Ida ? rutaIda : _rutaVueltaPorIda[rutaIda];
    }

    private static bool EsHoraNocturna()
    {
        var hora = DateTime.Now.Hour;
        return hora >= HORA_PAUSA || hora < HORA_REANUDA;
    }

    static async Task Main(string[] args)
    {
        ArchivoLog.Instalar(_config.Logs.Carpeta, _config.Logs.DiasRetencion);

        Console.WriteLine("╔════════════════════════════════════════════════╗");
        Console.WriteLine("║  Sistema de Control de Tiempos de Paraderos   ║");
        Console.WriteLine("╚════════════════════════════════════════════════╝");
        Console.WriteLine();

        // ── Red de seguridad: nada debe morir en silencio ──────────────────
        // Cualquier excepción no controlada en un hilo de fondo, o en una
        // Task "fire-and-forget" que nadie awaitea, se pierde sin dejar
        // rastro en .NET moderno. Estos dos handlers garantizan que quede
        // impreso en consola (y por lo tanto en el log de NSSM).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.WriteLine($"\n💥 [FATAL] Excepción no controlada: {e.ExceptionObject}\n");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine($"\n💥 [UNOBSERVED] Excepción en tarea no observada: {e.Exception}\n");
            e.SetObserved();
        };

        _geocercaService = new GeocercaService(_config.Geocercas, _config.MinutosDinamicosGeocercas);
        _apiEnvioService = new APIEnvioService(
            _config.Apis.EndRutaUrlTemplate,
            _config.Apis.AsignarDespachoUrl,
            _config.Apis.HttpTimeoutSegundos);

        Console.WriteLine($"✓ Usuario: {_username}");
        Console.WriteLine($"✓ Rutas configuradas: {string.Join(", ", _cuentas.Keys)}");
        Console.WriteLine($"✓ Zonas de despacho automático: {string.Join(", ", _zonasDespacho.Select(z => z.Nombre))}\n");

        _servidor = new ServidorHTTP(_config.ServidorHttp.Puerto);
        _servidor.OnIniciarMonitoreo += IniciarMonitoreoAsync;
        _servidor.OnConsultarEstado += ConsultarEstadoPlaca;
        _servidor.Iniciar();

        _despachoConnection = new HubConnectionBuilder()
            .WithUrl(_config.Apis.HubDespachoUrl)
            .WithAutomaticReconnect()
            .Build();

        _despachoConnection.On<RecentUrbano>("ActualizarDatosUrbano", async (datos) =>
        {
            await EjecutarSeguro(() => ManejarActualizacionDespacho(datos), "ManejarActualizacionDespacho");
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

        _dbService = new DatabaseService(
            _config.ConnectionStrings.DefaultConnection,
            _username,
            _cuentas.Keys);
        await RecuperarMonitoreosActivosAsync();
        _ = Task.Run(GestionarMonitoreoEsperaAsync);
        _ = Task.Run(VigilarMonitoreosActivosAsync);

        // ✅ Corriendo como servicio (NSSM/SCM): no hay consola real ni stdin.
        // Console.ReadLine() en ese caso NO se bloquea — devuelve null al instante,
        // así que MenuPrincipal() (que depende de leer líneas) se convertía en un
        // bucle cerrado imprimiendo el menú sin parar, al 100% de un núcleo de CPU.
        // Eso es lo que dejaba al proceso sordo a la señal de STOP del SCM (se quedaba
        // "Stopping" para siempre hasta matarlo a mano por PID).
        // Environment.UserInteractive es la forma estándar de distinguir "estoy en
        // una consola real" (true) de "estoy corriendo como servicio" (false).
        if (Environment.UserInteractive && !Console.IsInputRedirected)
        {
            await MenuPrincipal();
        }
        else
        {
            Console.WriteLine("✓ Corriendo como servicio (sin consola interactiva) — menú deshabilitado.\n");

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                Console.WriteLine("\n[SHUTDOWN] ProcessExit recibido — cerrando.\n");

            // Mantiene vivo el proceso sin tocar stdin. El trabajo real ya corre en
            // el HTTP server, el hub de despacho y GestionarMonitoreoEsperaAsync.
            await Task.Delay(Timeout.Infinite);
        }
    }

    /// <summary>
    /// Envuelve una operación async disparada "fire-and-forget" (callbacks de eventos
    /// SignalR, que no se pueden awaitear) para que cualquier excepción quede logueada
    /// en vez de perderse en silencio. Usar siempre en vez de "_ = MetodoAsync(...)".
    /// </summary>
    private static async Task EjecutarSeguro(Func<Task> accion, string contexto)
    {
        try
        {
            await accion();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ [ERROR] {contexto}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static EstadoPlacaResponse ConsultarEstadoPlaca(string placa)
    {
        _zonaActualPorPlaca.TryGetValue(placa, out var zonaActual);
        _rutaDefaultPorPlaca.TryGetValue(placa, out var rutaDefault);
        _codConductorPorPlaca.TryGetValue(placa, out var codConductor);

        string modoActual = null;
        if (_monitoreosActivos.ContainsKey(placa))
            modoActual = _geocercaService.ObtenerModoActual(placa).ToString();

        return new EstadoPlacaResponse
        {
            Placa = placa,
            EnMonitoreoEspera = _monitoreosEspera.ContainsKey(placa),
            EnMonitoreoActivo = _monitoreosActivos.ContainsKey(placa),
            TieneRutaDefault = rutaDefault != null,
            RutaDefault = rutaDefault,
            CodConductorAct = codConductor,
            ZonaActual = zonaActual,
            ModoRutaActual = modoActual,
            MonitoreoEsperaGlobalActivo = _monitoreoEsperaActivo,
            HoraServidor = DateTime.Now
        };
    }

    private static async Task<bool> IniciarMonitoreoAsync(IniciarMonitoreoRequest datos)
    {
        try
        {
            var placa = datos.Placa.ToUpper();
            var codasig = datos.Codasig;
            var fechaIni = datos.FechaIni;
            var codruta = datos.Codruta;

            // ✅ Validar por codruta
            if (!_cuentas.TryGetValue(codruta, out var cuenta))
            {
                Console.WriteLine($"❌ Codruta desconocida: {codruta}. Válidas: {string.Join(", ", _cuentas.Keys)}");
                return false;
            }

            Console.WriteLine($"\n🚀 Iniciando monitoreo automático...");
            Console.WriteLine($"   Placa:        {placa}");
            Console.WriteLine($"   Usuario:      {_username}");
            Console.WriteLine($"   Codasig:      {codasig}");
            Console.WriteLine($"   Hora Inicio:  {fechaIni}");
            Console.WriteLine($"   Codruta:      {codruta}");

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

            // 📌 DESHABILITADO — cálculo de tiempos esperados por paradero (16 geocercas).
            // Ya no se hace seguimiento de ETAs por paradero, solo despacho / cambio de sentido / redespacho.
            // var minutosEsperados = _geocercaService.ObtenerMinutosDinamicos(horaDespacho);
            // Console.WriteLine($"\n📊 Tiempos esperados calculados:");
            // for (int i = 0; i < minutosEsperados.Length; i++)
            // {
            //     var horaEsperada = horaDespacho.AddMinutes(minutosEsperados[i]);
            //     Console.WriteLine($"   Geocerca {i + 1}: +{minutosEsperados[i]} min → {horaEsperada:HH:mm:ss}");
            // }
            // Console.WriteLine();

            _datosDespachos[placa] = new DatosDespacho
            {
                Placa = placa,
                Codasig = codasig,
                HoraDespacho = horaDespacho,
                MinutosEsperados = Array.Empty<int>(),
                Codruta = codruta
            };

            // ✅ Mapear placa → cuenta por codruta
            _cuentaPorPlaca[placa] = cuenta;

            _geocercaService.InicializarMonitoreoPlaca(placa);
            ReiniciarDeteccionSentido(placa);

            if (EsRutaVuelta(codruta))
            {
                _geocercaService.CambiarModo(placa, ModoRuta.Regreso);
                Console.WriteLine($"[DESPACHO] 🔄 Despacho de VUELTA (codruta {codruta}) → modo REGRESO\n");
            }
            else if (datos.DespachoDesdeLima)
            {
                // Despacho manual desde Lima con código de ida: se pasa a la ruta de vuelta
                var rutaVuelta = RutaSegunSentido(codruta, ModoRuta.Regreso);
                _geocercaService.CambiarModo(placa, ModoRuta.Regreso);
                _datosDespachos[placa].Codruta = rutaVuelta;
                await _apiEnvioService.ActualizarRutaact(placa, rutaVuelta, cuenta.ApiActualizarRuta);
                Console.WriteLine($"[DESPACHO] 🔄 Despacho desde Lima con codruta {codruta} → rutaact {rutaVuelta}, modo REGRESO\n");
            }
            else
            {
                Console.WriteLine($"[DESPACHO] ➡️  Despacho de IDA (codruta {codruta}) → modo IDA\n");
            }

            await ConectarMonitoreoActivoAsync(placa);

            // El bus arranca fuera de cualquier zona de despacho (recién salió de una)
            _zonaActualPorPlaca.TryRemove(placa, out _);

            Console.WriteLine($"✅ Monitoreo iniciado para {placa} (codruta: {codruta})");
            Console.WriteLine($"🎯 Esperando cambio de sentido para cerrar el viaje y despachar el siguiente...\n");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error al iniciar monitoreo de {datos?.Placa}: {ex.Message} (si ya tenía monitor, lo conserva; el vigía reintentará)\n");
            return false;
        }
    }

    /// <summary>
    /// Conecta un monitor GPS nuevo para la placa y solo después reemplaza y cierra el anterior.
    /// Si la conexión falla, lanza la excepción y la placa conserva el monitor que tenía.
    /// </summary>
    private static async Task ConectarMonitoreoActivoAsync(string placa)
    {
        var nuevo = new MonitoreoGPSService(
            _username,
            _config.Apis.HubGpsUrlTemplate,
            _config.Apis.GpsReconexionDelayMs);
        nuevo.OnDatosGPSRecibidos += (datosGPS) =>
            _ = EjecutarSeguro(() => ManejarActualizacionGPS(placa, datosGPS), $"ManejarActualizacionGPS({placa})");

        try
        {
            await nuevo.ConectarYMonitorear(placa);
        }
        catch
        {
            await nuevo.Desconectar();
            throw;
        }

        MonitoreoGPSService anterior;
        lock (_cambioMonitorLock)
        {
            _monitoreosActivos.TryGetValue(placa, out anterior);
            _monitoreosActivos[placa] = nuevo;
        }
        _ultimoPingPorPlaca[placa] = DateTime.UtcNow;

        if (anterior != null && !ReferenceEquals(anterior, nuevo))
            await anterior.Desconectar();
    }

    // ════════════════════════════════════════════════════════════════════
    // RECUPERACIÓN AL ARRANCAR — buses con despacho ya abierto en BD
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Se ejecuta una sola vez, al arrancar el servicio, antes de que arranque el ciclo de espera.
    /// Sin esto, tras un reinicio a mitad del día la app trataría a TODOS los buses como recién
    /// empezando el día: el viaje en curso quedaría sin EndRuta hasta el próximo cambio de sentido
    /// (perdiendo su fechafin real) y, peor, DespacharAutomaticoAsync lo marcaría con isruta='0' sin
    /// fechafin al crear el "primer" despacho — duplicando el viaje que en realidad seguía abierto.
    /// </summary>
    private static async Task RecuperarMonitoreosActivosAsync()
    {
        var devices = await _dbService.GetDevicesActivosAsync();
        if (devices.Count == 0) return;

        Console.WriteLine($"\n[RECUPERACIÓN] 🔁 {devices.Count} bus(es) con despacho abierto en BD — recuperando como monitoreo activo:");

        foreach (var device in devices)
            await RecuperarPlacaActivaAsync(device, "RECUPERACIÓN");

        Console.WriteLine();
    }

    /// <summary>
    /// Reconstruye el estado de una placa con despacho abierto a partir de la BD (fuente de verdad)
    /// y le conecta un monitor GPS activo. La usan el arranque y el vigía.
    /// </summary>
    private static async Task RecuperarPlacaActivaAsync(DeviceActivo device, string origen)
    {
        if (string.IsNullOrEmpty(device.Placa) || !_cuentas.TryGetValue(device.Rutaact, out var cuenta))
        {
            Console.WriteLine($"[{origen}]   ⊘ {device.Placa} con rutaact {device.Rutaact} inválido, omitido");
            return;
        }

        var placa = device.Placa.ToUpper();
        DateTime horaDespacho;
        try
        {
            horaDespacho = string.IsNullOrEmpty(device.FechaIniUnix)
                ? DateTime.Now
                : DateTimeOffset.FromUnixTimeSeconds(long.Parse(device.FechaIniUnix)).ToOffset(TimeSpan.FromHours(_config.General.ZonaHorariaOffsetHoras)).DateTime;
        }
        catch { horaDespacho = DateTime.Now; }

        _datosDespachos[placa] = new DatosDespacho
        {
            Placa = placa,
            Codasig = device.Codasig,
            HoraDespacho = horaDespacho,
            MinutosEsperados = Array.Empty<int>(),
            Codruta = device.Rutaact
        };
        _cuentaPorPlaca[placa] = cuenta;
        _codConductorPorPlaca[placa] = device.CodConductorAct;

        _geocercaService.InicializarMonitoreoPlaca(placa);
        _geocercaService.CambiarModo(placa, EsRutaVuelta(device.Rutaact) ? ModoRuta.Regreso : ModoRuta.Ida);
        ReiniciarDeteccionSentido(placa);

        try
        {
            await ConectarMonitoreoActivoAsync(placa);
            Console.WriteLine($"[{origen}]   ✓ {placa} monitoreado (rutaact {device.Rutaact}, despachado {horaDespacho:HH:mm:ss})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{origen}]   ❌ {placa} error al conectar GPS: {ex.Message}");
            if (!_monitoreosActivos.ContainsKey(placa))
                LimpiarEstadoPlacaActiva(placa);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // VIGÍA — repara unidades con despacho abierto que dejaron de vigilarse
    // ════════════════════════════════════════════════════════════════════

    private static async Task VigilarMonitoreosActivosAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(INTERVALO_VIGIA_SEG));

            // De noche los viajes se cierran por horario y a medianoche por el proceso externo: no hay nada que reparar
            if (EsHoraNocturna())
            {
                _sospechasPorPlaca.Clear();
                _gpsBdPrevioPorPlaca.Clear();
                _ultimaRevisionVigia = DateTime.MinValue;
                continue;
            }

            try
            {
                await RevisarMonitoreosActivosAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VIGÍA] ❌ Error en la revisión: {ex.Message}");
            }
        }
    }

    private static async Task RevisarMonitoreosActivosAsync()
    {
        var revisionAnterior = _ultimaRevisionVigia;
        _ultimaRevisionVigia = DateTime.UtcNow;

        var abiertos = await _dbService.GetDevicesActivosAsync();
        var vistas = new HashSet<string>();

        foreach (var device in abiertos)
        {
            if (string.IsNullOrEmpty(device.Placa) || device.Rutaact == null || !_cuentas.ContainsKey(device.Rutaact))
                continue;

            var placa = device.Placa.ToUpper();
            vistas.Add(placa);

            _gpsBdPrevioPorPlaca.TryGetValue(placa, out var gpsBdPrevio);
            _gpsBdPrevioPorPlaca[placa] = device.LastGPSTimestamp;

            if (CambioSentidoEnCurso(placa)) continue;

            var problema = DiagnosticarPlaca(placa, device, gpsBdPrevio, revisionAnterior);
            if (problema == null)
            {
                _sospechasPorPlaca.TryRemove(placa, out _);
                continue;
            }

            var veces = _sospechasPorPlaca.AddOrUpdate(placa, 1, (_, n) => n + 1);
            if (veces < TICKS_PARA_REPARAR) continue;

            _sospechasPorPlaca.TryRemove(placa, out _);
            Console.WriteLine($"\n[VIGÍA] ⚠️  {placa}: {problema} — reparando desde la BD");
            await RecuperarPlacaActivaAsync(device, "VIGÍA");
        }

        foreach (var placa in _sospechasPorPlaca.Keys.Where(p => !vistas.Contains(p)))
            _sospechasPorPlaca.TryRemove(placa, out _);
        foreach (var placa in _gpsBdPrevioPorPlaca.Keys.Where(p => !vistas.Contains(p)))
            _gpsBdPrevioPorPlaca.TryRemove(placa, out _);
    }

    /// <summary>Null si la placa está sana; si no, la descripción del problema.</summary>
    private static string DiagnosticarPlaca(string placa, DeviceActivo device, long gpsBdPrevio, DateTime revisionAnterior)
    {
        if (!_monitoreosActivos.TryGetValue(placa, out var monitoreo))
            return "tiene despacho abierto en BD pero la app no la estaba vigilando";

        if (!monitoreo.EstaConectado())
            return "conexión GPS caída";

        if (!_datosDespachos.TryGetValue(placa, out var despacho) || !_cuentaPorPlaca.ContainsKey(placa))
            return "se perdió en memoria el estado de su despacho";

        if (despacho.Codruta != device.Rutaact)
            return $"ruta en memoria {despacho.Codruta} distinta a la de BD {device.Rutaact}";

        // El GPS siguió reportando a la BD desde la revisión anterior, pero a la app no le llegó ningún punto
        _ultimoPingPorPlaca.TryGetValue(placa, out var ultimoPing);
        if (revisionAnterior != DateTime.MinValue && gpsBdPrevio > 0
            && device.LastGPSTimestamp > gpsBdPrevio && ultimoPing < revisionAnterior)
            return "el GPS reporta en BD pero a la app no le llegan puntos";

        return null;
    }

    // ════════════════════════════════════════════════════════════════════
    // MONITOREO EN ESPERA — DESPACHO AUTOMÁTICO
    // ════════════════════════════════════════════════════════════════════

    private static async Task GestionarMonitoreoEsperaAsync()
    {
        while (true)
        {
            var hora = DateTime.Now.Hour;
            bool esHoraNocturna = hora >= HORA_PAUSA || hora < HORA_REANUDA;

            if (esHoraNocturna)
            {
                if (_monitoreoEsperaActivo)
                {
                    Console.WriteLine($"\n[ESPERA] 🌙 Son las {DateTime.Now:HH:mm} — pausando monitoreo en espera...");
                    await PausarMonitoreoEsperaAsync();
                }
                await Task.Delay(TimeSpan.FromMinutes(_config.MonitoreoEspera.IntervaloRevisionNocturnaMinutos));
                continue;
            }

            // ✅ Se recarga cada ciclo (no solo la primera vez) para detectar placas a las
            // que se les asignó rutadefault durante el día — CargarYArrancarMonitoreoEsperaAsync
            // ya es idempotente: omite las que ya están en espera o en monitoreo activo.
            var hoy = DateOnly.FromDateTime(DateTime.Now);
            if (_diaIniciado != hoy)
            {
                _diaIniciado = hoy;
                await SoltarMonitoreosDelDiaAnteriorAsync();
            }

            if (!_monitoreoEsperaActivo)
                Console.WriteLine($"\n[ESPERA] ☀️  Son las {DateTime.Now:HH:mm} — iniciando monitoreo en espera...");
            else
                Console.WriteLine($"\n[ESPERA] 🔄 Son las {DateTime.Now:HH:mm} — refrescando lista de buses en espera...");

            await CargarYArrancarMonitoreoEsperaAsync();

            await Task.Delay(TimeSpan.FromMinutes(_config.MonitoreoEspera.IntervaloRefrescoMinutos));
        }
    }

    /// <summary>
    /// Los despachos abiertos se cierran en BD con un proceso externo a medianoche, sin que esta app se entere.
    /// Al iniciar el día se sueltan en memoria (sin EndRuta, que sobrescribiría la fechafin del último viaje)
    /// para que los buses vuelvan a espera y su primer despacho se cree al salir de una zona.
    /// </summary>
    private static async Task SoltarMonitoreosDelDiaAnteriorAsync()
    {
        if (_monitoreosActivos.Count == 0) return;

        Console.WriteLine($"\n[ESPERA] 🌅 Nuevo día — soltando {_monitoreosActivos.Count} monitoreo(s) activo(s) del día anterior (ya cerrados en BD)");

        foreach (var (placa, monitoreo) in _monitoreosActivos.ToList())
        {
            await monitoreo.Desconectar();
            _monitoreosActivos.TryRemove(placa, out _);
            LimpiarEstadoPlacaActiva(placa);
            Console.WriteLine($"[ESPERA]   🔌 {placa} liberado");
        }
    }

    private static async Task CargarYArrancarMonitoreoEsperaAsync()
    {
        var devices = await _dbService.GetDevicesEsperaAsync();

        if (devices.Count == 0)
        {
            Console.WriteLine("[ESPERA] ⚠️  No se encontraron buses con rutadefault en la DB.");
            return;
        }

        Console.WriteLine($"[ESPERA] {devices.Count} bus(es) cargados para monitoreo en espera:");

        foreach (var device in devices)
        {
            if (string.IsNullOrEmpty(device.Placa) || string.IsNullOrEmpty(device.RutaDefault))
                continue;

            var placa = device.Placa.ToUpper();

            // Se refresca siempre (también para buses activos): el conductor se reutiliza en cada nuevo despacho por cambio de sentido
            _rutaDefaultPorPlaca[placa] = device.RutaDefault;
            _codConductorPorPlaca[placa] = device.CodConductorAct;

            // Si ya está en monitoreo activo, no agregar en espera
            if (_monitoreosActivos.ContainsKey(placa))
            {
                Console.WriteLine($"[ESPERA]   ⊘ {placa} ya está en monitoreo activo, omitido");
                continue;
            }

            // Si ya tiene monitoreo en espera con el GPS conectado, omitir; si se cayó, se reconecta abajo
            if (_monitoreosEspera.TryGetValue(placa, out var esperaExistente))
            {
                if (esperaExistente.EstaConectado())
                {
                    Console.WriteLine($"[ESPERA]   ⊘ {placa} ya está en espera");
                    continue;
                }

                Console.WriteLine($"[ESPERA]   ⚠️  {placa} en espera con GPS desconectado — reconectando");
                await esperaExistente.Desconectar();
                _monitoreosEspera.TryRemove(placa, out _);
            }

            _zonaActualPorPlaca.TryRemove(placa, out _);

            try
            {
                var monitoreo = new MonitoreoGPSService(
                    _username,
                    _config.Apis.HubGpsUrlTemplate,
                    _config.Apis.GpsReconexionDelayMs);
                monitoreo.OnDatosGPSRecibidos += (datos) =>
                    _ = EjecutarSeguro(() => ManejarGPSEsperaAsync(placa, datos), $"ManejarGPSEsperaAsync({placa})");
                await monitoreo.ConectarYMonitorear(placa);
                _monitoreosEspera[placa] = monitoreo;
                Console.WriteLine($"[ESPERA]   ✓ {placa} en espera (rutadefault: {device.RutaDefault})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ESPERA]   ❌ {placa} error al conectar: {ex.Message}");
            }
        }

        _monitoreoEsperaActivo = true;
    }

    private static async Task PausarMonitoreoEsperaAsync()
    {
        foreach (var (placa, monitoreo) in _monitoreosEspera)
        {
            await monitoreo.Desconectar();
            Console.WriteLine($"[ESPERA] 🔌 {placa} desconectado");
        }

        _monitoreosEspera.Clear();
        _zonaActualPorPlaca.Clear();
        _monitoreoEsperaActivo = false;
    }

    private static async Task ManejarGPSEsperaAsync(string placa, DatosVehiculo datos)
    {
        if (datos?.Vehiculo == null) return;

        // Si el bus fue despachado mientras estaba en espera, detener su monitoreo en espera
        if (_monitoreosActivos.ContainsKey(placa))
        {
            if (_monitoreosEspera.TryGetValue(placa, out var m))
            {
                await m.Desconectar();
                _monitoreosEspera.TryRemove(placa, out _);
            }
            return;
        }

        var v = datos.Vehiculo;
        await VerificarZonaEsperaAsync(placa, v.lastValidLatitude, v.lastValidLongitude);
    }

    // ════════════════════════════════════════════════════════════════════
    // ZONAS DE DESPACHO — primer despacho de un bus en espera
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bus en espera (sin despacho): al salir de una zona de despacho se crea su primer despacho.
    /// Los despachos siguientes los crea CambiarSentidoAsync en cada cambio de sentido.
    /// </summary>
    private static async Task VerificarZonaEsperaAsync(string placa, double lat, double lon)
    {
        var zonaDetectada = _zonasDespacho.FirstOrDefault(z =>
            CalcularDistanciaHaversine(lat, lon, z.Lat, z.Lon) <= z.RadioM);

        _zonaActualPorPlaca.TryGetValue(placa, out var zonaAnteriorNombre);
        bool estabaDentro = zonaAnteriorNombre != null;
        bool ahoraDentro = zonaDetectada != null;

        if (!estabaDentro && ahoraDentro)
        {
            _zonaActualPorPlaca[placa] = zonaDetectada.Nombre;
            Console.WriteLine($"[ZONA] 🏁 {placa} entró a {zonaDetectada.Nombre}");
            return;
        }

        if (estabaDentro && !ahoraDentro)
        {
            _zonaActualPorPlaca.TryRemove(placa, out _);
            var zonaSalida = _zonasDespacho.First(z => z.Nombre == zonaAnteriorNombre);

            var modo = zonaSalida.ModoAlSalir ?? (lon >= zonaSalida.Lon ? ModoRuta.Ida : ModoRuta.Regreso);

            Console.WriteLine($"\n[ESPERA] 🚀 {placa} salió de {zonaSalida.Nombre} → auto-despacho ({modo})\n");
            await DespacharAutomaticoAsync(placa, modo);
        }
    }

    private static async Task DespacharAutomaticoAsync(string placa, ModoRuta modo)
    {
        try
        {
            if (!_rutaDefaultPorPlaca.TryGetValue(placa, out var rutaDefault))
            {
                Console.WriteLine($"[ESPERA] ❌ {placa} sin rutadefault — no se puede auto-despachar");
                return;
            }

            var ruta = RutaSegunSentido(rutaDefault, modo);
            if (ruta == null)
            {
                Console.WriteLine($"[ESPERA] ❌ {placa} rutadefault {rutaDefault} no es 69/70/71/72 — no se puede auto-despachar");
                return;
            }

            _codConductorPorPlaca.TryGetValue(placa, out var codConductor);

            // Detener monitoreo en espera de este bus
            if (_monitoreosEspera.TryGetValue(placa, out var monitoreoEspera))
            {
                await monitoreoEspera.Desconectar();
                _monitoreosEspera.TryRemove(placa, out _);
            }

            // ✅ Mismo circuito que el despacho manual: asignarFR crea el registro real
            // en BD (urbano_asigna / recor_control / device) y luego notifica de vuelta
            // a esta consola en /api/iniciar-monitoreo, que dispara IniciarMonitoreoAsync.
            bool asignado = await _apiEnvioService.AsignarDespachoAutomatico(placa, ruta, codConductor);

            if (!asignado)
                Console.WriteLine($"[ESPERA] ❌ {placa} — asignarFR no pudo crear el despacho automático (ruta {ruta})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ESPERA] ❌ Error en auto-despacho de {placa}: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // CAMBIO DE SENTIDO — cada IDA y cada VUELTA es un despacho propio
    // ════════════════════════════════════════════════════════════════════

    private static void ReiniciarDeteccionSentido(string placa)
    {
        _controlWashington[placa] = new ControlWashington();
        _cruzoPuntoRetorno[placa] = false;
        _cruzoPuntoRetorno2[placa] = false;
        _cruzoPuntoRetornoLima[placa] = false;
        _pasoGiroCallao[placa] = false;
    }

    private static bool CambioSentidoEnCurso(string placa)
    {
        lock (_cambioSentidoEnCurso) return _cambioSentidoEnCurso.Contains(placa);
    }

    /// <summary>
    /// Cierra el despacho actual (endruta → fechafin en urbano_asigna) y crea el despacho del nuevo
    /// sentido con asignarFR (nuevo registro en urbano_asigna / recor_control con su propia fechaini).
    /// asignarFR notifica luego a /api/iniciar-monitoreo, que reinicia el monitoreo con el nuevo codruta.
    /// De noche solo cierra el despacho y deja al bus fuera de monitoreo hasta el ciclo de espera de las 04:00.
    /// </summary>
    private static async Task CambiarSentidoAsync(string placa, ModoRuta nuevoModo, string motivo, double lat, double lon)
    {
        lock (_cambioSentidoEnCurso)
        {
            if (!_cambioSentidoEnCurso.Add(placa)) return;
        }

        try
        {
            if (!_datosDespachos.TryGetValue(placa, out var despacho) || !_cuentaPorPlaca.TryGetValue(placa, out var cuenta))
            {
                Console.WriteLine($"⚠️  {placa}: sin despacho/cuenta en memoria, no se puede cambiar de sentido");
                return;
            }

            var nuevaRuta = RutaSegunSentido(despacho.Codruta, nuevoModo);
            if (nuevaRuta == null)
            {
                Console.WriteLine($"⚠️  {placa}: codruta {despacho.Codruta} sin equivalente de {nuevoModo}");
                return;
            }

            Console.WriteLine($"\n[SENTIDO] 🔄 ════════════════════════════════════");
            Console.WriteLine($"[SENTIDO]    {placa}: {motivo} → {(nuevoModo == ModoRuta.Ida ? "IDA" : "VUELTA")} (ruta {despacho.Codruta} → {nuevaRuta})");
            Console.WriteLine($"[SENTIDO]    Lat/Lon: {lat:F6}, {lon:F6}");
            Console.WriteLine($"[SENTIDO] ════════════════════════════════════\n");

            // Estado local al nuevo sentido de inmediato, por si la notificación de asignarFR no llega
            _geocercaService.CambiarModo(placa, nuevoModo);
            ReiniciarDeteccionSentido(placa);
            despacho.Codruta = nuevaRuta;

            if (EsHoraNocturna())
            {
                if (DateTime.Now.Hour >= HORA_PAUSA)
                {
                    Console.WriteLine($"[SENTIDO] 🌙 {placa}: horario nocturno — se cierra el viaje sin nuevo despacho");
                    await FinalizarMonitoreoActivoAsync(placa);
                }
                else
                {
                    // Pasada la medianoche el viaje ya fue cerrado en BD por el proceso externo
                    Console.WriteLine($"[SENTIDO] 🌙 {placa}: madrugada — viaje ya cerrado en BD, solo se detiene el monitoreo");
                    if (_monitoreosActivos.TryGetValue(placa, out var monitoreo))
                    {
                        await monitoreo.Desconectar();
                        _monitoreosActivos.TryRemove(placa, out _);
                    }
                    LimpiarEstadoPlacaActiva(placa);
                }
                return;
            }

            bool finalizado = await _apiEnvioService.EndRuta(placa);
            Console.WriteLine(finalizado ? "✅ Viaje anterior finalizado en BD" : "❌ No se pudo finalizar el viaje anterior en BD");

            _codConductorPorPlaca.TryGetValue(placa, out var codConductor);
            bool asignado = await _apiEnvioService.AsignarDespachoAutomatico(placa, nuevaRuta, codConductor);

            if (!asignado)
            {
                // Respaldo: al menos dejar device.rutaact/feciniruta en el nuevo sentido
                Console.WriteLine($"❌ asignarFR falló para {placa} — respaldo con ActualizarRutaact({nuevaRuta})");
                await _apiEnvioService.ActualizarRutaact(placa, nuevaRuta, cuenta.ApiActualizarRuta);
            }
        }
        finally
        {
            lock (_cambioSentidoEnCurso) _cambioSentidoEnCurso.Remove(placa);
        }
    }

    private static async Task FinalizarMonitoreoActivoAsync(string placa)
    {
        try
        {
            bool finalizadoEnBD = await _apiEnvioService.EndRuta(placa);
            Console.WriteLine(finalizadoEnBD ? "✅ Ruta finalizada en base de datos" : "❌ No se pudo finalizar la ruta en base de datos");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error al finalizar ruta de {placa}: {ex.Message}");
        }

        if (_monitoreosActivos.TryGetValue(placa, out var monitoreo))
        {
            await monitoreo.Desconectar();
            _monitoreosActivos.TryRemove(placa, out _);
        }

        LimpiarEstadoPlacaActiva(placa);
        Console.WriteLine($"✓ Monitoreo de {placa} finalizado\n");
    }

    private static void LimpiarEstadoPlacaActiva(string placa)
    {
        _datosDespachos.TryRemove(placa, out _);
        _controlWashington.TryRemove(placa, out _);
        _cuentaPorPlaca.TryRemove(placa, out _);
        _cruzoPuntoRetorno.TryRemove(placa, out _);
        _cruzoPuntoRetorno2.TryRemove(placa, out _);
        _cruzoPuntoRetornoLima.TryRemove(placa, out _);
        _pasoGiroCallao.TryRemove(placa, out _);
        _zonaActualPorPlaca.TryRemove(placa, out _);
        _ultimoPingPorPlaca.TryRemove(placa, out _);
        _geocercaService.LimpiarMonitoreoPlaca(placa);
    }

    // ════════════════════════════════════════════════════════════════════

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

    // 📌 Ya no se usa (dependía del reporte de horas de llegada por paradero, deshabilitado).
    // Se deja definida por si se reactiva esa lógica más adelante.
    private static DateTime AjustarHoraLlegada(
        double latPunto, double lonPunto, DateTime horaPunto,
        double latGeocerca, double lonGeocerca, ModoRuta modo)
    {
        double distanciaMetros = CalcularDistanciaHaversine(latPunto, lonPunto, latGeocerca, lonGeocerca);
        double velocidadFijaKmh = _config.DeteccionSentido.VelocidadFijaKmh;
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

            var opcion = Console.ReadLine();
            if (opcion == null)
            {
                await Task.Delay(Timeout.Infinite);
                return;
            }

            switch (opcion)
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
                Console.WriteLine($"      Codasig:  {datos.Codasig}");
                Console.WriteLine($"      Codruta:  {datos.Codruta}");
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
        _monitoreosActivos.TryRemove(placa, out _);
        LimpiarEstadoPlacaActiva(placa);

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
        _controlWashington.Clear();
        _cuentaPorPlaca.Clear();
        _cruzoPuntoRetorno.Clear();
        _cruzoPuntoRetorno2.Clear();
        _cruzoPuntoRetornoLima.Clear();
        _pasoGiroCallao.Clear();
        Console.WriteLine("✓ Todos los monitoreos detenidos");
    }

    private static void MostrarEstadoServidor()
    {
        var apiEnvio = _cuentas.Values.FirstOrDefault()?.ApiEnvioControl ?? string.Empty;
        Console.WriteLine("\n╔════════════════════════════════════════╗");
        Console.WriteLine("║         ESTADO DEL SERVIDOR            ║");
        Console.WriteLine("╚════════════════════════════════════════╝");
        Console.WriteLine($"Usuario:              {_username}");
        Console.WriteLine($"Puerto HTTP:          {_config.ServidorHttp.Puerto}");
        Console.WriteLine($"Monitoreos activos:   {_monitoreosActivos.Count}");
        Console.WriteLine($"Zonas de despacho:    {string.Join(", ", _zonasDespacho.Select(z => z.Nombre))}");
        Console.WriteLine($"Hub Despacho:         {(_despachoConnection?.State == HubConnectionState.Connected ? "Conectado" : "Desconectado")}");
        Console.WriteLine($"Endpoint API:         http://localhost:{_config.ServidorHttp.Puerto}/api/iniciar-monitoreo");
        Console.WriteLine($"API Envío:            {apiEnvio}");
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
        if (datos.Isruta == "1"
            && !string.IsNullOrEmpty(datos.DeviceID)
            && datos.Codruta != null
            && _cuentas.ContainsKey(datos.Codruta))
        {
            await IniciarMonitoreoAsync(new IniciarMonitoreoRequest
            {
                Placa = datos.DeviceID,
                Usuario = _username,
                FechaIni = datos.Fechaini,
                Codasig = "0",
                Codruta = datos.Codruta
            });
        }
    }

    private static async Task ManejarActualizacionGPS(string placa, DatosVehiculo datos)
    {
        if (datos?.Vehiculo == null) return;

        var v = datos.Vehiculo;
        _ultimoPingPorPlaca[placa] = DateTime.UtcNow;

        Console.WriteLine($"📍 GPS [{placa}] {v.lastValidLatitude:F6},{v.lastValidLongitude:F6} {v.lastValidSpeed:F0} km/h " +
                          $"modo={_geocercaService.ObtenerModoActual(placa)} hora GPS {datos.FechaActual:HH:mm:ss}");

        // Mientras se cierra el viaje y se crea el siguiente, se ignoran los pings de esta placa
        if (CambioSentidoEnCurso(placa)) return;

        if (!_controlWashington.ContainsKey(placa))
            ReiniciarDeteccionSentido(placa);

        var lat = v.lastValidLatitude;
        var lon = v.lastValidLongitude;
        var modoActual = _geocercaService.ObtenerModoActual(placa);

        ModoRuta? nuevoModo = null;
        string motivo = null;

        if (modoActual == ModoRuta.Ida)
        {
            // ═══════════════════════════════════════════════════════════════
            // IDA → VUELTA
            // 1) Giro en Washington (escenario ideal)
            // 2) Línea cerca de Washington: cruzó al este y regresó
            // 3) Línea de Faucett: cruzó al este y regresó (no llegó a Washington)
            // ═══════════════════════════════════════════════════════════════
            var geocercaWashington = _geocercaService.ObtenerGeocercaPorId(8);
            var ctrlW = _controlWashington[placa];

            if (lat <= geocercaWashington.CenterLat) ctrlW.YaPasoLatitud = true;
            if (lon >= geocercaWashington.CenterLon) ctrlW.YaPasoLongitud = true;

            if (lon >= LINEA_RETORNO2_LON) _cruzoPuntoRetorno2[placa] = true;
            if (lon >= LINEA_RETORNO_LON)  _cruzoPuntoRetorno[placa] = true;

            if (ctrlW.YaPasoLatitud && ctrlW.YaPasoLongitud)
            {
                (nuevoModo, motivo) = (ModoRuta.Regreso, "giro en Washington");
            }
            else if (_cruzoPuntoRetorno2[placa] && lon < LINEA_RETORNO2_LON - HISTERESIS_LON)
            {
                (nuevoModo, motivo) = (ModoRuta.Regreso, "regresó al oeste por la línea cerca de Washington");
            }
            else if (_cruzoPuntoRetorno[placa] && lon < LINEA_RETORNO_LON - HISTERESIS_LON)
            {
                (nuevoModo, motivo) = (ModoRuta.Regreso, "regresó al oeste por la línea de Faucett");
            }
        }
        else
        {
            // ═══════════════════════════════════════════════════════════════
            // VUELTA → IDA
            // 1) Giro en Callao: pasó al oeste de la línea de giro y volvió al este
            // 2) Línea de Faucett: cruzó al oeste y regresó (no llegó a Callao)
            // ═══════════════════════════════════════════════════════════════
            if (lon <= LINEA_GIRO_CALLAO_OESTE) _pasoGiroCallao[placa] = true;
            if (lon <= LINEA_RETORNO_LON)       _cruzoPuntoRetornoLima[placa] = true;

            if (_pasoGiroCallao[placa] && lon > LINEA_GIRO_CALLAO_ESTE)
            {
                (nuevoModo, motivo) = (ModoRuta.Ida, "giro en Callao");
            }
            else if (_cruzoPuntoRetornoLima[placa] && lon > LINEA_RETORNO_LON + HISTERESIS_LON)
            {
                (nuevoModo, motivo) = (ModoRuta.Ida, "regresó al este por la línea de Faucett");
            }
        }

        if (nuevoModo.HasValue)
            await CambiarSentidoAsync(placa, nuevoModo.Value, motivo, lat, lon);

        // 📌 DESHABILITADO — detección y reporte de las 16 geocercas/paraderos con ETAs.
        // Solo se conserva: primer despacho por zona y cambio de sentido (con nuevo despacho).
        //
        // bool washingtonDisparoEstePing =
        //     _geocercaService.ObtenerGeocercasVisitadas(placa).Contains(8) &&
        //     _geocercaService.ObtenerModoActual(placa) == ModoRuta.Regreso &&
        //     modoActual == ModoRuta.Ida;
        //
        // Geocerca geocercaCruzada = null;
        // if (!washingtonDisparoEstePing)
        // {
        //     geocercaCruzada = _geocercaService.VerificarCruceGeocerca(
        //         placa, v.lastValidLongitude, v.lastValidLatitude);
        // }
        //
        // if (geocercaCruzada != null)
        // {
        //     DateTime horaAjustada = AjustarHoraLlegada(
        //         v.lastValidLatitude, v.lastValidLongitude, datos.FechaActual,
        //         geocercaCruzada.CenterLat, geocercaCruzada.CenterLon, modoActual);
        //
        //     _ = ProcesarLlegadaGeocerca(placa, geocercaCruzada,
        //         v.lastValidLatitude, v.lastValidLongitude, horaAjustada);
        // }
    }

    // 📌 DESHABILITADO — reporte de llegada por paradero (control de tiempos / EnvioControlB).
    // Se deja definida por si se reactiva esa lógica más adelante; ya no se invoca.
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

            if (!_cuentaPorPlaca.TryGetValue(placa, out var cuenta))
            {
                Console.WriteLine($"⚠️  No hay cuenta asociada para {placa}");
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
            var diferenciaMin = (int)diferencia.TotalMinutes;
            string volado = FormatearVolado(diferencia);

            Console.WriteLine($"📊 CÁLCULO DE TIEMPOS:");
            Console.WriteLine($"   Hora Despacho:  {datosDespacho.HoraDespacho:HH:mm:ss}");
            Console.WriteLine($"   Hora Esperada:  {horaEsperada:HH:mm:ss} (+{minutosEsperados} min)");
            Console.WriteLine($"   Hora Llegada:   {horaLlegada:HH:mm:ss}");
            Console.WriteLine($"   Diferencia:     {volado} {(diferenciaMin > 0 ? "(RETRASO)" : diferenciaMin < 0 ? "(ADELANTO)" : "(A TIEMPO)")}");
            Console.WriteLine($"   Coordenadas:    {latitud:F6}, {longitud:F6}");
            Console.WriteLine($"   Codruta:        {datosDespacho.Codruta}");

            var registro = new ControlGPS
            {
                Codasig = datosDespacho.Codasig,
                DeviceID = placa,
                Nom_control = geocerca.Name,
                Hora_inicio = datosDespacho.HoraDespacho.ToString("HH:mm:ss"),
                Hora_estimada = horaEsperada.ToString("HH:mm:ss"),
                Hora_llegada = horaLlegada.ToString("HH:mm:ss"),
                Volado = volado,
                Fecha = horaLlegada.ToString("yyyy-MM-dd")
            };

            Console.WriteLine($"\n📤 Enviando datos a la API...");
            bool enviado = await _apiEnvioService.EnviarRegistro(registro, cuenta.ApiEnvioControl);
            Console.WriteLine(enviado ? "✅ Datos enviados correctamente" : "❌ Error al enviar datos");

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
