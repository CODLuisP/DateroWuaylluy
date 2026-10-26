namespace DateroWuaylluy.Models;

public class IniciarMonitoreoRequest
{
    public string Placa { get; set; }
    public string Usuario { get; set; }
    public string FechaIni { get; set; }
    public string Codasig { get; set; }
    public string Codruta { get; set; }
    public bool DespachoDesdeLima { get; set; }
}