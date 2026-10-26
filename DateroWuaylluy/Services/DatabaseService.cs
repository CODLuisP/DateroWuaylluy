using MySqlConnector;
using DateroWuaylluy.Models;

namespace DateroWuaylluy.Services;

public class DatabaseService
{
    private readonly string _connectionString;
    private readonly string _accountId;
    private readonly string[] _rutasActivas;

    public DatabaseService(
        string connectionString,
        string accountId = "serfrymh",
        IEnumerable<string>? rutasActivas = null)
    {
        _connectionString = connectionString;
        _accountId = accountId;
        _rutasActivas = rutasActivas?.ToArray() ?? ["69", "70", "71", "72"];
    }

    public async Task<List<DeviceEspera>> GetDevicesEsperaAsync()
    {
        var lista = new List<DeviceEspera>();

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            string query = $@"
                SELECT deviceID, rutadefault, codconductoract
                FROM device
                WHERE accountID = '{MySqlHelper.EscapeString(_accountId)}'
                  AND rutadefault IS NOT NULL";

            using var command = new MySqlCommand(query, connection);
            using var reader  = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new DeviceEspera
                {
                    Placa           = reader.IsDBNull(0) ? null : reader.GetString(0).Trim(),
                    RutaDefault     = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    CodConductorAct = reader.IsDBNull(2) ? "0" : reader.GetString(2).Trim()
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] Error al obtener dispositivos en espera: {ex.Message}");
        }

        return lista;
    }

    /// <summary>
    /// Buses con un despacho ya abierto en BD al momento de arrancar el servicio: device.rutaact en
    /// 69/70/71/72 con su urbano_asigna correspondiente aún en isruta='1'. Permite, tras un reinicio
    /// (despliegue, caída, reinicio del servidor), seguir monitoreando como "activo" en vez de tratar
    /// al bus como si recién empezara el día — evita perder la IDA/VUELTA en curso y que EndRuta
    /// sobrescriba la fechafin de un viaje que en realidad sigue abierto.
    /// </summary>
    public async Task<List<DeviceActivo>> GetDevicesActivosAsync()
    {
        var lista = new List<DeviceActivo>();

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var rutasSql = string.Join(",", _rutasActivas.Select(r => $"'{MySqlHelper.EscapeString(r)}'"));
            string query = $@"
                SELECT d.deviceID, d.rutaact, d.codconductoract, a.codigo, a.fechaini, d.lastGPSTimestamp
                FROM device d
                JOIN urbano_asigna a ON a.deviceID = d.deviceID
                WHERE d.accountID = '{MySqlHelper.EscapeString(_accountId)}'
                  AND d.rutaact IN ({rutasSql})
                  AND a.isruta = '1'
                  AND a.fechaini >= UNIX_TIMESTAMP(CURDATE())
                  AND a.codigo = (SELECT MAX(codigo) FROM urbano_asigna WHERE deviceID = d.deviceID AND isruta = '1')";

            using var command = new MySqlCommand(query, connection);
            using var reader  = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new DeviceActivo
                {
                    Placa           = reader.IsDBNull(0) ? null : reader.GetString(0).Trim(),
                    Rutaact         = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    CodConductorAct = reader.IsDBNull(2) ? "0" : reader.GetString(2).Trim(),
                    Codasig         = reader.IsDBNull(3) ? "0" : reader.GetString(3).Trim(),
                    FechaIniUnix    = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                    LastGPSTimestamp = reader.IsDBNull(5) ? 0 : Convert.ToInt64(reader.GetValue(5))
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] Error al obtener dispositivos activos: {ex.Message}");
        }

        return lista;
    }
}
