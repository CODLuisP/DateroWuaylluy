using System.Net;
using System.Text;
using System.Text.Json;
using DateroWuaylluy.Models;

namespace DateroWuaylluy.API;

public class ServidorHTTP
{
    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private readonly int _puerto;

    public event Func<IniciarMonitoreoRequest, Task<bool>> OnIniciarMonitoreo;
    public event Func<string, EstadoPlacaResponse> OnConsultarEstado;

    public ServidorHTTP(int puerto = 8080)
    {
        _puerto = puerto;
    }

    public void Iniciar()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_puerto}/");  // ✅ Acepta cualquier IP

        _listener.Start();
        _cts = new CancellationTokenSource();

        Console.WriteLine($"✓ Servidor HTTP iniciado en http://+:{_puerto}");
        Console.WriteLine($"✓ Endpoint disponible: POST http://+:{_puerto}/api/iniciar-monitoreo");
        Console.WriteLine();

        Task.Run(() => EscucharPeticiones(_cts.Token));
    }

    private async Task EscucharPeticiones(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => ProcesarPeticion(context), ct);
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    Console.WriteLine($"❌ Error en servidor HTTP: {ex.Message}");
            }
        }
    }

    private async Task ProcesarPeticion(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
        response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        try
        {
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/api/iniciar-monitoreo")
            {
                await ProcesarIniciarMonitoreo(request, response);
                return;
            }

            if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/health")
            {
                await ResponderJSON(response, 200, new { status = "running", timestamp = DateTime.Now });
                return;
            }

            if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/api/estado")
            {
                await ProcesarConsultarEstado(request, response);
                return;
            }

            await ResponderJSON(response, 404, new { error = "Endpoint no encontrado" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error procesando petición: {ex.Message}");
            await ResponderJSON(response, 500, new { error = ex.Message });
        }
    }

    private async Task ProcesarIniciarMonitoreo(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync();
            }

            var datos = JsonSerializer.Deserialize<IniciarMonitoreoRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (datos == null || string.IsNullOrEmpty(datos.Placa))
            {
                await ResponderJSON(response, 400, new { error = "Placa es requerida" });
                return;
            }

            Console.WriteLine($"\n📨 Petición recibida: Iniciar monitoreo de placa {datos.Placa}");

            if (OnIniciarMonitoreo != null)
            {
                bool exito = await OnIniciarMonitoreo.Invoke(datos);

                if (exito)
                {
                    await ResponderJSON(response, 200, new
                    {
                        success = true,
                        message = $"Monitoreo iniciado para placa {datos.Placa}",
                        placa = datos.Placa
                    });
                }
                else
                {
                    await ResponderJSON(response, 500, new
                    {
                        success = false,
                        message = "Error al iniciar monitoreo"
                    });
                }
            }
            else
            {
                await ResponderJSON(response, 500, new { error = "Handler no configurado" });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error en ProcesarIniciarMonitoreo: {ex.Message}");
            await ResponderJSON(response, 500, new { error = ex.Message });
        }
    }

    private async Task ProcesarConsultarEstado(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var placa = request.QueryString["placa"];

            if (string.IsNullOrWhiteSpace(placa))
            {
                await ResponderJSON(response, 400, new { error = "Query param 'placa' es requerido" });
                return;
            }

            if (OnConsultarEstado == null)
            {
                await ResponderJSON(response, 500, new { error = "Handler no configurado" });
                return;
            }

            var estado = OnConsultarEstado.Invoke(placa.ToUpper());
            await ResponderJSON(response, 200, estado);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error en ProcesarConsultarEstado: {ex.Message}");
            await ResponderJSON(response, 500, new { error = ex.Message });
        }
    }

    private async Task ResponderJSON(HttpListenerResponse response, int statusCode, object data)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";

        var json = JsonSerializer.Serialize(data);
        var buffer = Encoding.UTF8.GetBytes(json);

        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    public void Detener()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        Console.WriteLine("✓ Servidor HTTP detenido");
    }
}