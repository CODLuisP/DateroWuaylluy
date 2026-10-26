namespace DateroWuaylluy.Models;

/// <summary>Bus con un despacho ya abierto en BD (device.rutaact + urbano_asigna.isruta='1') al momento de arrancar el servicio.</summary>
public class DeviceActivo
{
    public string Placa { get; set; }
    public string Rutaact { get; set; }
    public string CodConductorAct { get; set; }
    public string Codasig { get; set; }
    public string FechaIniUnix { get; set; }
    public long LastGPSTimestamp { get; set; }
}
