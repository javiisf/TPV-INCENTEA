namespace ServidorImpresion
{
    /// <summary>
    /// Snapshot inmutable de la configuración relevante para impresión.
    /// Al ser readonly record struct, se aloja en la pila y evita allocations en heap
    /// en el hot path de impresión (PrintAsync).
    /// </summary>
    public readonly record struct ConfigSnapshot(
        string UltimoCOM,
        string UltimaUSB,
        int PuertoServidor,
        int BaudRate)
    {
        public static ConfigSnapshot From(ConfigData config)
        {
            return new ConfigSnapshot(
                config.UltimoCOM,
                config.UltimaUSB,
                config.PuertoServidor,
                config.BaudRate);
        }
    }
}
