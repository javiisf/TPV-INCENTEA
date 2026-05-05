using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Serilog;

namespace ServidorImpresion
{
    /// <summary>
    /// Encapsula toda la lógica del NotifyIcon (system tray):
    /// creación del icono, menú contextual, tooltip y notificaciones.
    /// MainForm solo interactúa con métodos simples (SetStatus, ShowNotification).
    /// </summary>
    public sealed class TrayService : IDisposable
    {
        private NotifyIcon? _trayIcon;
        private ToolStripMenuItem? _autostartItem;
        private ToolStripMenuItem? _printerSubmenu;
        private Func<string>? _getCurrentDevice;
        private Func<IReadOnlyList<string>>? _getDevices;

        /// <summary>
        /// Se dispara cuando el usuario pulsa "Abrir Configuración" o hace doble-click en el tray.
        /// </summary>
        public event Action? OpenConfigRequested;

        /// <summary>
        /// Se dispara cuando el usuario pulsa "Abrir Health".
        /// </summary>
        public event Action? OpenHealthRequested;

        /// <summary>
        /// Se dispara cuando el usuario pulsa "Iniciar con Windows".
        /// </summary>
        public event Action? AutostartToggleRequested;

        /// <summary>
        /// Se dispara cuando el usuario selecciona una impresora del submenú.
        /// </summary>
        public event Action<string>? PrinterSelectedRequested;

        /// <summary>
        /// Se dispara cuando el usuario pulsa "Salir y Cerrar Servidor".
        /// </summary>
        public event Action? ExitRequested;

        public void Initialize()
        {
            Icon? iconToUse = null;

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "impresora.ico");
                if (File.Exists(iconPath))
                {
                    iconToUse = new Icon(iconPath);
                    Log.Information("TrayService: icono cargado desde {Path}", iconPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TrayService: no se pudo cargar impresora.ico");
            }

            iconToUse ??= SystemIcons.Application;

            _trayIcon = new NotifyIcon
            {
                Icon = iconToUse,
                Visible = true,
                Text = "Servidor TPV Corriendo"
            };

            _autostartItem = new ToolStripMenuItem("Iniciar con Windows")
            {
                CheckOnClick = false
            };
            _autostartItem.Click += (_, _) => AutostartToggleRequested?.Invoke();

            _printerSubmenu = new ToolStripMenuItem("Impresora");

            var menu = new ContextMenuStrip();
            menu.Items.Add("Configuración", null, (_, _) => OpenConfigRequested?.Invoke());
            menu.Items.Add(_printerSubmenu);
            menu.Items.Add("Estado", null, (_, _) => OpenHealthRequested?.Invoke());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir y Cerrar Servidor", null, (_, _) => ExitRequested?.Invoke());

            menu.Opening += (_, _) => RebuildPrinterSubmenu();

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => OpenConfigRequested?.Invoke();
        }

        /// <summary>
        /// Registra las fuentes de datos para el submenú de impresoras.
        /// Llamar después de Initialize().
        /// </summary>
        public void SetPrinterSource(Func<string> getCurrentDevice, Func<IReadOnlyList<string>> getDevices)
        {
            _getCurrentDevice = getCurrentDevice;
            _getDevices = getDevices;
        }

        private void RebuildPrinterSubmenu()
        {
            if (_printerSubmenu is null || _getCurrentDevice is null || _getDevices is null) return;

            _printerSubmenu.DropDownItems.Clear();
            string current = _getCurrentDevice();
            IReadOnlyList<string> devices = _getDevices();

            if (devices.Count == 0)
            {
                _printerSubmenu.DropDownItems.Add(new ToolStripMenuItem("(sin dispositivos)") { Enabled = false });
                return;
            }

            foreach (string device in devices)
            {
                string captured = device;
                var item = new ToolStripMenuItem(device) { Checked = device == current };
                item.Click += (_, _) => PrinterSelectedRequested?.Invoke(captured);
                _printerSubmenu.DropDownItems.Add(item);
            }
        }

        /// <summary>
        /// Actualiza el estado del checkmark de "Iniciar con Windows".
        /// </summary>
        public void SetAutostartChecked(bool enabled)
        {
            if (_autostartItem != null)
                _autostartItem.Checked = enabled;
        }

        /// <summary>
        /// Actualiza el tooltip del icono de bandeja.
        /// </summary>
        public void SetStatus(string message)
        {
            if (_trayIcon == null) return;
            // NotifyIcon.Text tiene límite (~63 chars)
            _trayIcon.Text = message.Length > 63 ? message[..63] : message;
        }

        /// <summary>
        /// Muestra una notificación de globo (balloon tip).
        /// </summary>
        public void ShowNotification(string title, string message)
        {
            if (_trayIcon == null) return;
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = message;
            _trayIcon.ShowBalloonTip(4000);
        }

        public void Dispose()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}
