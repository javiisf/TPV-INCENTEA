namespace ServidorImpresion
{
    public class ConfiguredDevice
    {
        public string Tipo { get; set; } = "no_configurado"; // "com" | "usb" | "no_configurado"
        public string Nombre { get; set; } = string.Empty;
        public int BaudRate { get; set; } = 0;
    }
}
