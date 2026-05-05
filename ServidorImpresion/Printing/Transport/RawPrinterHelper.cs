using System;
using System.Runtime.InteropServices;
using Serilog;

namespace ServidorImpresion
{
    public class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string pDocName = "TPV Label";
            [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile = null!;

            // "RAW" indica al Spooler de Windows que reenvíe los bytes tal cual al
            // driver de la impresora, sin ninguna transformación GDI/EMF. Cualquier
            // otro tipo de datos (p.ej. "TEXT") haría que el spooler reinterpretara
            // el flujo y corrompiera silenciosamente las secuencias de comandos ESC/POS.
            [MarshalAs(UnmanagedType.LPStr)] public string pDatatype = "RAW";
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, Int32 dwCount, out Int32 dwWritten);

        // MÉTODO: Envía un array de bytes directamente al driver para evitar corrupción de caracteres
        public static bool SendBytesToPrinter(string szPrinterName, byte[] pBytes)
        {
            IntPtr hPrinter = IntPtr.Zero;
            bool bSuccess = false;

            if (!OpenPrinter(szPrinterName, out hPrinter, IntPtr.Zero))
            {
                int winError = Marshal.GetLastWin32Error();
                Log.Error("SendBytesToPrinter: OpenPrinter falló. Impresora={Printer}, WinError={Error}",
                    szPrinterName, winError);
                return false;
            }

            try
            {
                DOCINFOA di = new DOCINFOA();
                if (!StartDocPrinter(hPrinter, 1, di))
                {
                    int winError = Marshal.GetLastWin32Error();
                    Log.Error("SendBytesToPrinter: StartDocPrinter falló. Impresora={Printer}, WinError={Error}",
                        szPrinterName, winError);
                    return false;
                }

                try
                {
                    IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(pBytes.Length);
                    try
                    {
                        Marshal.Copy(pBytes, 0, pUnmanagedBytes, pBytes.Length);

                        // WritePrinter no garantiza consumir todos los bytes en una sola
                        // llamada: algunos drivers (especialmente bridges USB-COM) devuelven
                        // un contador parcial. El bucle reintenta con el fragmento restante
                        // hasta que se acepta el payload completo o se produce un error.
                        int totalWritten = 0;
                        while (totalWritten < pBytes.Length)
                        {
                            int bytesEscritos = 0;
                            int remaining = pBytes.Length - totalWritten;
                            IntPtr offsetPtr = IntPtr.Add(pUnmanagedBytes, totalWritten);

                            if (!WritePrinter(hPrinter, offsetPtr, remaining, out bytesEscritos))
                            {
                                int winError = Marshal.GetLastWin32Error();
                                Log.Error("SendBytesToPrinter: WritePrinter falló. Impresora={Printer}, WinError={Error}, BytesEscritos={Written}/{Total}",
                                    szPrinterName, winError, totalWritten, pBytes.Length);
                                return false;
                            }

                            if (bytesEscritos == 0)
                            {
                                Log.Error("SendBytesToPrinter: WritePrinter escribió 0 bytes (impresora desconectada?). Impresora={Printer}, BytesEscritos={Written}/{Total}",
                                    szPrinterName, totalWritten, pBytes.Length);
                                return false;
                            }

                            totalWritten += bytesEscritos;
                        }

                        bSuccess = true;
                        Log.Information("SendBytesToPrinter: éxito. Impresora={Printer}, BytesEscritos={Bytes}",
                            szPrinterName, totalWritten);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pUnmanagedBytes);
                    }
                }
                finally
                {
                    EndDocPrinter(hPrinter);
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }

            return bSuccess;
        }
    }
}