using System;
using System.Text;
using System.Windows.Forms;
using Serilog;

namespace ServidorImpresion
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Inicializar logging antes de cualquier cosa
            LogManager.Initialize();

            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Encoding encoding;
                try
                {
                    encoding = Encoding.GetEncoding(858);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Program: no se pudo obtener CP858, usando UTF-8 como fallback");
                    encoding = Encoding.UTF8;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Log.Information("Program: aplicación iniciada");

                Application.Run(new MainForm(encoding));
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Program: error no manejado en aplicación");
                MessageBox.Show("Error fatal: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Log.Information("Program: aplicación cerrada");
                LogManager.Close();
            }
        }
    }
}