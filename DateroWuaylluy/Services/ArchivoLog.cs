using System.Text;

namespace DateroWuaylluy.Services;

/// <summary>
/// Duplica todo lo que se escribe en consola a logs/datero-AAAA-MM-DD.log (un archivo por día),
/// anteponiendo la hora a cada línea. Conserva solo los últimos <see cref="DiasRetencion"/> días (incluido hoy).
/// </summary>
public sealed class ArchivoLog : TextWriter
{
    private const int DiasRetencion = 5;
    private readonly int _diasRetencion;

    private readonly TextWriter _consola;
    private readonly string _carpeta;
    private readonly object _lock = new();
    private StreamWriter? _archivo;
    private DateOnly _diaArchivo;
    private bool _inicioDeLinea = true;

    private ArchivoLog(TextWriter consola, string carpeta, int diasRetencion)
    {
        _consola = consola;
        _carpeta = carpeta;
        _diasRetencion = diasRetencion;
        Directory.CreateDirectory(_carpeta);
    }

    public override Encoding Encoding => Encoding.UTF8;

    public static void Instalar(string nombreCarpeta = "logs", int diasRetencion = DiasRetencion)
    {
        var carpeta = Path.IsPathRooted(nombreCarpeta)
            ? nombreCarpeta
            : Path.Combine(AppContext.BaseDirectory, nombreCarpeta);
        Console.SetOut(new ArchivoLog(Console.Out, carpeta, diasRetencion));
    }

    public override void Write(char value)
    {
        lock (_lock)
        {
            _consola.Write(value);
            EscribirEnArchivo(value.ToString());
        }
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        lock (_lock)
        {
            _consola.Write(value);
            EscribirEnArchivo(value);
        }
    }

    public override void Write(char[] buffer, int index, int count) =>
        Write(new string(buffer, index, count));

    public override void WriteLine(string? value)
    {
        lock (_lock)
        {
            _consola.WriteLine(value);
            EscribirEnArchivo((value ?? string.Empty) + Environment.NewLine);
        }
    }

    public override void Flush()
    {
        lock (_lock)
        {
            _consola.Flush();
            _archivo?.Flush();
        }
    }

    // Un fallo del disco nunca debe tumbar el servicio: se pierde la línea del archivo, la consola sigue.
    private void EscribirEnArchivo(string texto)
    {
        try
        {
            var ahora = DateTime.Now;
            AsegurarArchivoDelDia(DateOnly.FromDateTime(ahora));

            var sb = new StringBuilder(texto.Length + 16);
            foreach (var c in texto)
            {
                if (_inicioDeLinea && c != '\r' && c != '\n')
                {
                    sb.Append(ahora.ToString("HH:mm:ss.fff")).Append(' ');
                    _inicioDeLinea = false;
                }
                sb.Append(c);
                if (c == '\n') _inicioDeLinea = true;
            }

            _archivo!.Write(sb.ToString());
        }
        catch
        {
        }
    }

    private void AsegurarArchivoDelDia(DateOnly hoy)
    {
        if (_archivo != null && _diaArchivo == hoy) return;

        _archivo?.Dispose();
        var ruta = Path.Combine(_carpeta, $"datero-{hoy:yyyy-MM-dd}.log");
        _archivo = new StreamWriter(new FileStream(ruta, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        _diaArchivo = hoy;
        _inicioDeLinea = true;

        BorrarArchivosAntiguos(hoy);
    }

    private void BorrarArchivosAntiguos(DateOnly hoy)
    {
        var limite = hoy.AddDays(-_diasRetencion);
        foreach (var ruta in Directory.GetFiles(_carpeta, "datero-*.log"))
        {
            var nombre = Path.GetFileNameWithoutExtension(ruta);
            if (DateOnly.TryParseExact(nombre["datero-".Length..], "yyyy-MM-dd", out var dia) && dia <= limite)
            {
                try { File.Delete(ruta); } catch { }
            }
        }
    }
}
