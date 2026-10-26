using System.Text;
using System.Text.Json;
using DateroWuaylluy.Models;

namespace DateroWuaylluy.Services;

public class APIEnvioService
{
    private readonly HttpClient _httpClient;
    private readonly string _endRutaUrlTemplate;
    private readonly string _asignarDespachoUrl;

    public APIEnvioService(
        string endRutaUrlTemplate = "https://villa.velsat.pe:8443/api/Datero/endruta/{deviceID}",
        string asignarDespachoUrl = "https://villa.velsat.pe:8443/api/Caja/asignarFR",
        int timeoutSegundos = 10)
    {
        _endRutaUrlTemplate = endRutaUrlTemplate;
        _asignarDespachoUrl = asignarDespachoUrl;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSegundos)
        };
        // ✅ Sin _apiUrl fija — viene dinámico desde _cuentas en Program.cs
    }

    public async Task<bool> EnviarRegistros(ControlGPS[] registros, string apiUrl)
    {
        try
        {
            var json = JsonSerializer.Serialize(registros);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            Console.WriteLine($"\n[API] Enviando {registros.Length} registro(s)...");
            Console.WriteLine($"[API] URL: {apiUrl}");

            var response = await _httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var resultado = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ✓ {resultado}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ❌ Error: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] ❌ Excepción: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> EnviarRegistro(ControlGPS registro, string apiUrl) =>
        await EnviarRegistros([registro], apiUrl);

    // ✅ urlBase viene desde _cuentas con {placa} y {rutaact} como placeholders
    public async Task<bool> ActualizarRutaact(string placa, string rutaact, string urlBase)
    {
        try
        {
            var url = urlBase
                .Replace("{placa}", placa)
                .Replace("{rutaact}", rutaact);

            Console.WriteLine($"\n[API] Llamando a ActualizarRutaact para {placa}...");
            Console.WriteLine($"[API] URL: {url}");

            var response = await _httpClient.PutAsync(url, null);

            if (response.IsSuccessStatusCode)
            {
                var resultado = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ✓ ActualizarRutaact exitoso: {resultado}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ❌ Error ActualizarRutaact: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] ❌ Excepción en ActualizarRutaact: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> EndRuta(string deviceID)
    {
        try
        {
            var url = _endRutaUrlTemplate
                .Replace("{deviceID}", deviceID)
                .Replace("{placa}", deviceID);
            Console.WriteLine($"\n[API] Llamando a endruta para {deviceID}...");
            Console.WriteLine($"[API] URL: {url}");

            var response = await _httpClient.PostAsync(url, null);

            if (response.IsSuccessStatusCode)
            {
                var resultado = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ✓ EndRuta exitoso: {resultado}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ❌ Error EndRuta: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] ❌ Excepción en EndRuta: {ex.Message}");
            return false;
        }
    }

    // ✅ Llama al mismo endpoint que usa el despacho manual (asignarFR),
    // para que el auto-despacho cree el registro real en BD (urbano_asigna / recor_control / device).
    public async Task<bool> AsignarDespachoAutomatico(string placa, string rutaDefault, string codConductor)
    {
        try
        {
            var url = _asignarDespachoUrl;

            var payload = new
            {
                carro = new { codunidad = placa },
                ruta = new { codigo = rutaDefault },
                conductor = new { codigo = string.IsNullOrEmpty(codConductor) ? "0" : codConductor },
                fecprog = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            Console.WriteLine($"\n[API] Llamando a asignarFR (auto-despacho) para {placa}...");
            Console.WriteLine($"[API] URL: {url}");

            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var resultado = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ✓ asignarFR exitoso: {resultado}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[API] ❌ Error asignarFR: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] ❌ Excepción en AsignarDespachoAutomatico: {ex.Message}");
            return false;
        }
    }
}
