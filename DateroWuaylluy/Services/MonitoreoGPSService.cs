using Microsoft.AspNetCore.SignalR.Client;
using DateroWuaylluy.Models;

namespace DateroWuaylluy.Services;

public class MonitoreoGPSService
{
    private HubConnection _gpsConnection;
    private readonly string _username;
    private readonly string _hubGpsUrlTemplate;
    private readonly int _reconexionDelayMs;
    private string _placaActual;

    public event Action<DatosVehiculo> OnDatosGPSRecibidos;

    public MonitoreoGPSService(
        string username,
        string hubGpsUrlTemplate = "https://villa.velsat.pe:8443/dataHubVehicle/{username}/{placa}",
        int reconexionDelayMs = 5000)
    {
        _username = username;
        _hubGpsUrlTemplate = hubGpsUrlTemplate;
        _reconexionDelayMs = reconexionDelayMs;
    }

    public async Task ConectarYMonitorear(string placa)
    {
        if (_gpsConnection != null && _gpsConnection.State == HubConnectionState.Connected)
            await Desconectar();

        _placaActual = placa;

        var url = _hubGpsUrlTemplate
            .Replace("{username}", _username)
            .Replace("{placa}", placa);

        _gpsConnection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect()
            .Build();

        _gpsConnection.On<DatosVehiculo>("ActualizarDatosVehiculo", (datos) =>
        {
            if (datos?.Vehiculo != null)
                Console.WriteLine($"[HUB] Recibido: {datos.FechaActual:HH:mm:ss} | Lon: {datos.Vehiculo.lastValidLongitude:F6}");

            OnDatosGPSRecibidos?.Invoke(datos);
        });

        _gpsConnection.Closed += async (error) =>
        {
            if (_gpsConnection != null)
            {
                Console.WriteLine($"[GPS] Conexión cerrada. Reconectando...");
                await Task.Delay(_reconexionDelayMs);
                try
                {
                    if (_gpsConnection != null && _gpsConnection.State == HubConnectionState.Disconnected)
                        await _gpsConnection.StartAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GPS] Error al reconectar: {ex.Message}");
                }
            }
        };

        _gpsConnection.Reconnecting += _ =>
        {
            Console.WriteLine($"[GPS] Reconectando al monitoreo GPS...");
            return Task.CompletedTask;
        };

        _gpsConnection.Reconnected += _ =>
        {
            Console.WriteLine($"[GPS] Reconectado exitosamente al monitoreo GPS");
            return Task.CompletedTask;
        };

        try
        {
            await _gpsConnection.StartAsync();
            Console.WriteLine($"[GPS] ✓ Conectado al monitoreo GPS de {placa}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GPS] Error al conectar: {ex.Message}");
            throw;
        }
    }

    public async Task Desconectar()
    {
        if (_gpsConnection == null) return;

        try
        {
            if (_gpsConnection.State is HubConnectionState.Connected
                or HubConnectionState.Connecting
                or HubConnectionState.Reconnecting)
            {
                await _gpsConnection.StopAsync();
            }

            await _gpsConnection.DisposeAsync();
            _gpsConnection = null;
            Console.WriteLine($"[GPS] Desconectado del monitoreo GPS");
        }
        catch (ObjectDisposedException)
        {
            _gpsConnection = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GPS] Error al desconectar (no crítico): {ex.Message}");
            _gpsConnection = null;
        }
    }

    public bool EstaConectado() =>
        _gpsConnection != null && _gpsConnection.State == HubConnectionState.Connected;

    public string GetPlacaActual() => _placaActual;
}
