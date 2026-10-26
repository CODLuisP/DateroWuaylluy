namespace DateroWuaylluy.Models;

public class EstadoPlacaResponse
{
    public string Placa { get; set; }
    public bool EnMonitoreoEspera { get; set; }
    public bool EnMonitoreoActivo { get; set; }
    public bool TieneRutaDefault { get; set; }
    public string RutaDefault { get; set; }
    public string CodConductorAct { get; set; }
    public string ZonaActual { get; set; }
    public string ModoRutaActual { get; set; }
    public bool MonitoreoEsperaGlobalActivo { get; set; }
    public DateTime HoraServidor { get; set; }
}
