using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Serilog;

namespace ServidorImpresion
{
    public partial class MainForm : Form
    {
        private readonly AppController _controller = null!; // asignado en constructor o Close() antes de uso
        private readonly TrayService _tray = new();
        private readonly Encoding _encoding;
        private readonly System.Windows.Forms.Timer _statusUpdateTimer = new() { Interval = 2_000 };

        private bool permitirCierreReal = false;
        private bool lastPrinterHealthy = true;
        private bool notifiedServerError = false;
        private bool startHidden = false;
        private bool configValida = false;
        private bool _cerrando = false;

        private Label lblNoPrinters;
        private readonly System.Windows.Forms.Timer _deviceRefreshTimer = new() { Interval = 3_000 };
        private readonly System.Windows.Forms.Timer _savedTimer = new() { Interval = 650 };

        public MainForm(Encoding encoding)
        {
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));

            InitializeComponent();

            lblNoPrinters = new Label
            {
                Text = "No hay impresoras disponibles. Instale una e intente de nuevo.",
                ForeColor = Color.Red,
                AutoSize = true,
                Visible = false,
                Location = new Point(12, 95)
            };
            tabPrincipal.Controls.Add(lblNoPrinters);

            _savedTimer.Tick += (_, _) => { _savedTimer.Stop(); lblGuardado.Visible = false; this.Hide(); };

            this.StartPosition = FormStartPosition.CenterScreen;

            var configStore = new ConfigStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ServidorImpresion",
                "config_tpv.json"));

            try
            {
                var config = configStore.Load();
                bool configFileExists = configStore.Exists();

                configValida = config.EsValida();

                _controller = new AppController(encoding, configStore, config);
                _controller.StatusChanged += Controller_StatusChanged;
                _controller.ServerError += Controller_ServerError;

                if (configFileExists && configValida)
                {
                    startHidden = true;
                    this.ShowInTaskbar = false;
                    this.Opacity = 0;
                }

                this.Shown += MainForm_Shown;

                ConfigurarTrayIcon();
                ConfigurarValidacionUI();

                this.Load += MainForm_Load;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "MainForm: error durante la inicialización");
                MessageBox.Show("Error fatal durante la inicialización: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }

        private async void MainForm_Load(object? sender, EventArgs e)
        {
            try
            {
                // Establecer el puerto ANTES de llenar dispositivos para que ActualizarValidacionUI
                // no sobreescriba configValida con false cuando SelectedIndexChanged se dispare.
                txtPort.Text = _controller.Config.PuertoServidor.ToString();

                await LlenarDispositivosAsync();

                _deviceRefreshTimer.Tick += async (_, _) => await RefrescarDispositivosAsync();

                // CONFIGURACIÓN DEL NUEVO TIMER DE ESTADOS
                _statusUpdateTimer.Tick += async (_, _) => await RefrescarSoloEstadosAsync();

                this.VisibleChanged += (_, _) =>
                {
                    if (this.Visible)
                    {
                        _deviceRefreshTimer.Start();
                        _statusUpdateTimer.Start(); // Arranca al mostrar ventana
                    }
                    else
                    {
                        _deviceRefreshTimer.Stop();
                        _statusUpdateTimer.Stop();  // Para al ocultar (ahorro de CPU)
                    }
                };

                // Aplica la selección guardada (dispositivo + puerto) y recalcula la validación.
                AplicarConfiguracionEnUI();
                ActualizarValidacionUI();

                if (!configValida)
                {
                    Log.Warning("MainForm_Load: configuración inicial no válida. Esperando selección del usuario.");
                    return;
                }

                await _controller.StartAsync(CancellationToken.None);
                ActualizarEstadoTray(healthy: true, message: $"Servidor OK (puerto {_controller.Config.PuertoServidor})");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "MainForm_Load: error durante la inicialización de servicios");
                ActualizarEstadoTray(healthy: false, message: $"Servidor ERROR (puerto {_controller.Config.PuertoServidor})");
                MostrarNotificacionTray("Servidor TPV", $"No se pudo iniciar el servidor HTTP en el puerto {_controller.Config.PuertoServidor}.\n\nDetalle: {ex.Message}");
                MessageBox.Show("Error fatal durante la inicialización de servicios: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                permitirCierreReal = true;
                this.Close();
            }
        }

        private void MainForm_Shown(object? sender, EventArgs e)
        {
            if (!startHidden)
                return;
            try { this.Hide(); } catch { }
        }

        // ── Tray ────────────────────────────────────────────────────────────────────

        private void ConfigurarTrayIcon()
        {
            _tray.Initialize();
            _tray.OpenConfigRequested += MostrarVentana;
            _tray.OpenHealthRequested += AbrirHealth;
            _tray.AutostartToggleRequested += ToggleAutostart;
            _tray.PrinterSelectedRequested += OnTrayPrinterSelected;
            _tray.ExitRequested += () =>
            {
                permitirCierreReal = true;
                Application.Exit();
            };

            _tray.SetPrinterSource(
                getCurrentDevice: () =>
                {
                    string com = _controller.Config.UltimoCOM;
                    string usb = _controller.Config.UltimaUSB;
                    return !string.IsNullOrEmpty(com) ? com : usb;
                },
                getDevices: () => DeviceDiscoveryService.GetDevices()
            );

            // Reflejar estado actual del registro al arrancar
            _tray.SetAutostartChecked(WindowsStartupManager.IsEnabled());
        }

        private void OnTrayPrinterSelected(string device)
        {
            try
            {
                var config = _controller.Config.Clone();
                config.AplicarDispositivo(device);
                _controller.SaveAndApplyConfig(config);

                // Reflejar en el combo si la ventana está abierta
                int idx = FindPrinterItemIndex(device);
                if (idx >= 0) cbPrinters.SelectedIndex = idx;

                Log.Information("OnTrayPrinterSelected: impresora cambiada a '{Device}'", device);
                _tray.ShowNotification("Servidor TPV", $"Impresora: {device}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "OnTrayPrinterSelected: error cambiando impresora a '{Device}'", device);
            }
        }

        private void ToggleAutostart()
        {
            try
            {
                if (WindowsStartupManager.IsEnabled())
                {
                    WindowsStartupManager.Disable();
                    _tray.SetAutostartChecked(false);
                }
                else
                {
                    WindowsStartupManager.Enable();
                    _tray.SetAutostartChecked(true);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ToggleAutostart: error modificando el registro");
                MostrarNotificacionTray("Servidor TPV", $"No se pudo cambiar el inicio automático: {ex.Message}");
            }
        }

        private void ActualizarEstadoTray(bool healthy, string message)
            => _tray.SetStatus(message);

        private void MostrarNotificacionTray(string title, string message)
            => _tray.ShowNotification(title, message);

        private void MostrarVentana()
        {
            if (startHidden)
            {
                this.Opacity = 1;
                this.ShowInTaskbar = true;
            }
            notifiedServerError = false; // permitir una nueva notificación si vuelve a cerrar sin configurar
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        private void AbrirHealth()
        {
            try
            {
                string url = $"http://localhost:{_controller.Config.PuertoServidor}/health";

                string apiKey = _controller.Config.ApiKey;
                if (!string.IsNullOrEmpty(apiKey))
                    url += $"?key={Uri.EscapeDataString(apiKey)}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AbrirHealth: no se pudo abrir /health");
                MostrarNotificacionTray("Servidor TPV", "No se pudo abrir /health en el navegador.");
            }
        }

        // ── Eventos del controlador ─────────────────────────────────────────────────

        private void Controller_StatusChanged(object? sender, AppStatusSnapshot snap)
        {
            try
            {
                if (!IsHandleCreated)
                    return;

                BeginInvoke(new Action(() =>
                {
                    btnTestImpresion.Enabled = snap.IsServerRunning;
                    if (snap.IsServerRunning)
                    {
                        ActualizarEstadoTray(healthy: true, message: $"Servidor OK (puerto {snap.Port})");
                        notifiedServerError = false;
                    }
                    else
                    {
                        ActualizarEstadoTray(healthy: false, message: $"Servidor ERROR (puerto {snap.Port})");
                        if (!notifiedServerError)
                        {
                            MostrarNotificacionTray("Servidor TPV", "El servidor HTTP no está ejecutándose.");
                            notifiedServerError = true;
                        }
                    }

                    if (snap.PrinterHealthy != lastPrinterHealthy)
                    {
                        if (!snap.PrinterHealthy)
                            MostrarNotificacionTray("Servidor TPV", "Se detectaron fallos de impresión. Revise la impresora/puerto.");
                        lastPrinterHealthy = snap.PrinterHealthy;
                    }
                }));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Controller_StatusChanged: no se pudo procesar snapshot");
            }
        }

        private void Controller_ServerError(object? sender, HttpServerErrorEventArgs e)
        {
            if (!this.IsHandleCreated)
                return;

            this.Invoke(new Action(() =>
            {
                ActualizarEstadoTray(healthy: false, message: $"Servidor ERROR (puerto {_controller.Config.PuertoServidor})");
                MostrarNotificacionTray("Servidor TPV", $"Error del servidor HTTP: {e.Message}");
                MessageBox.Show($"Error del servidor HTTP: {e.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }

        // ── Cierre ───────────────────────────────────────────────────────────────────

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!permitirCierreReal && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();

                // Notificar una sola vez que la configuración está pendiente
                if (!configValida && !notifiedServerError)
                {
                    MostrarNotificacionTray("Servidor TPV", "La configuración está incompleta. Abre la ventana desde el icono del tray para completarla.");
                    notifiedServerError = true;
                }
            }
            else if (!_cerrando)
            {
                // Cancelar el cierre para poder hacer el apagado async limpio,
                // luego cerrar de verdad cuando termine.
                e.Cancel = true;
                _cerrando = true;
                _ = CerrarLimpioAsync();
            }
            base.OnFormClosing(e);
        }

        private async System.Threading.Tasks.Task CerrarLimpioAsync()
        {
            await FinalizarTodoAsync();
            permitirCierreReal = true;
            // Volver al hilo UI para el cierre final
            if (IsHandleCreated)
                this.Invoke(new Action(this.Close));
        }

        private async System.Threading.Tasks.Task FinalizarTodoAsync()
        {
            _deviceRefreshTimer.Stop();
            _deviceRefreshTimer.Dispose();
            _statusUpdateTimer.Stop();
            _statusUpdateTimer.Dispose();
            _savedTimer.Stop();
            _savedTimer.Dispose();

            await _controller.StopAsync();

            try { _tray.Dispose(); }
            catch (Exception ex) { Log.Warning(ex, "FinalizarTodo: error desechando TrayService"); }
        }

        // ── Dispositivos ─────────────────────────────────────────────────────────────

        internal Task LlenarDispositivosAsync()
        {
            // Ejecutado en el hilo UI (STA) — necesario para PrinterSettings.InstalledPrinters
            try
            {
                cbPrinters.Items.Clear();
                var devices = DeviceDiscoveryService.GetDevices();
                var items = devices.Select(d => new PrinterItem(d)).ToList();
                foreach (var item in items)
                    cbPrinters.Items.Add(item);

                if (cbPrinters.Items.Count > 0) { cbPrinters.SelectedIndex = 0; lblNoPrinters.Visible = false; }
                else { lblNoPrinters.Visible = true; }

                Log.Information("LlenarDispositivos: {Count} dispositivos encontrados", cbPrinters.Items.Count);
                _ = ActualizarEstadosAsync(items);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LlenarDispositivos: error listando dispositivos");
            }
            return Task.CompletedTask;
        }

        private Task RefrescarDispositivosAsync()
        {
            // No interrumpir al usuario si tiene el desplegable abierto
            if (cbPrinters.DroppedDown) return Task.CompletedTask;

            string? seleccionActual = (cbPrinters.SelectedItem as PrinterItem)?.DeviceName
                                      ?? cbPrinters.SelectedItem?.ToString();

            // Ejecutado en el hilo UI (STA) — necesario para PrinterSettings.InstalledPrinters
            var nuevos = DeviceDiscoveryService.GetDevices();

            // Comparar con la lista actual para no tocar nada si no cambió (comparar por DeviceName)
            bool igual = nuevos.Count == cbPrinters.Items.Count;
            if (igual)
            {
                for (int i = 0; i < nuevos.Count; i++)
                {
                    string nombre = (cbPrinters.Items[i] as PrinterItem)?.DeviceName
                                    ?? cbPrinters.Items[i]?.ToString() ?? "";
                    if (nombre != nuevos[i]) { igual = false; break; }
                }
            }
            if (igual) return Task.CompletedTask;

            Log.Information("RefrescarDispositivos: lista cambió ({Old} → {New})", cbPrinters.Items.Count, nuevos.Count);

            var items = nuevos.Select(d => new PrinterItem(d)).ToList();
            cbPrinters.Items.Clear();
            foreach (var item in items) cbPrinters.Items.Add(item);

            if (cbPrinters.Items.Count > 0)
            {
                lblNoPrinters.Visible = false;
                // Restaurar selección anterior si sigue disponible
                int idx = seleccionActual != null ? FindPrinterItemIndex(seleccionActual) : -1;
                cbPrinters.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                lblNoPrinters.Visible = true;
            }

            ActualizarValidacionUI();
            _ = ActualizarEstadosAsync(items);
            return Task.CompletedTask;
        }

        // ── Configuración UI ─────────────────────────────────────────────────────────

        private void AplicarConfiguracionEnUI()
        {
            txtPort.Text = _controller.Config.PuertoServidor.ToString();

            string guardado = !string.IsNullOrEmpty(_controller.Config.UltimoCOM)
                ? _controller.Config.UltimoCOM
                : _controller.Config.UltimaUSB;

            if (!string.IsNullOrEmpty(guardado))
            {
                int index = FindPrinterItemIndex(guardado);
                if (index != -1)
                    cbPrinters.SelectedIndex = index;
            }

            // Advanced tab
            int baudIdx = cbBaudRate.Items.IndexOf(_controller.Config.BaudRate);
            cbBaudRate.SelectedIndex = baudIdx >= 0 ? baudIdx : 0;

            nudMaxTicketKB.Value = Math.Max(1, (int)Math.Ceiling(_controller.Config.MaxTicketBytes / 1024.0));

            ActualizarBaudRateEnabled();
        }

        private void ActualizarBaudRateEnabled()
        {
            var selected = (cbPrinters.SelectedItem as PrinterItem)?.DeviceName ?? "";
            cbBaudRate.Enabled = EsPuertoCOM(selected);
        }

        private void ConfigurarValidacionUI()
        {
            txtPort.TextChanged += (s, e) => ActualizarValidacionUI();
            cbPrinters.SelectedIndexChanged += (s, e) => ActualizarValidacionUI();
            cbPrinters.SelectedIndexChanged += (s, e) => ActualizarBaudRateEnabled();
        }

        private void ActualizarValidacionUI()
        {
            bool puertoOk = int.TryParse(txtPort.Text.Trim(), out int puerto) && puerto > 0 && puerto <= 65535;
            txtPort.BackColor = puertoOk ? SystemColors.Window : Color.MistyRose;

            bool dispositivoOk = cbPrinters.SelectedItem != null && !string.IsNullOrWhiteSpace(cbPrinters.SelectedItem.ToString());
            cbPrinters.BackColor = dispositivoOk ? SystemColors.Window : Color.MistyRose;

            configValida = puertoOk && dispositivoOk;
            btnGuardar.Enabled = configValida;
        }

        // ── API key ──────────────────────────────────────────────────────────────────

        private static string GenerarApiKey()
        {
            byte[] bytes = new byte[18];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes); // 24 caracteres URL-safe base64
        }

        private void MostrarDialogoGestionApiKey()
        {
            string claveActual = _controller.Config.ApiKey;
            bool tieneKey = !string.IsNullOrEmpty(claveActual);

            if (!tieneKey)
            {
                // Sin clave: ofrecer activar
                var r = MessageBox.Show(
                    "La API Key está DESACTIVADA.\r\n" +
                    "El servidor acepta peticiones sin autenticación.\r\n\r\n" +
                    "¿Activar protección por API Key?",
                    "API Key — desactivada",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (r != DialogResult.Yes) return;

                string nuevaClave = GenerarApiKey();
                var cfg = _controller.Config.Clone();
                cfg.ApiKey = nuevaClave;
                _controller.SaveAndApplyConfig(cfg);

                try { Clipboard.SetText(nuevaClave); } catch { }
                MessageBox.Show(
                    $"API Key activada:\r\n\r\n{nuevaClave}\r\n\r\n" +
                    "Copiada al portapapeles. Guárdela — no se volverá a mostrar.\r\n\r\n" +
                    "Inclúyala como encabezado HTTP:\r\n  X-Api-Key: <su clave>",
                    "API Key activada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Log.Information("MostrarDialogoGestionApiKey: API key activada por el usuario");
            }
            else
            {
                // Con clave: regenerar (Yes) · desactivar (No) · cancelar (Cancel)
                var r = MessageBox.Show(
                    $"La API Key está ACTIVA.\r\n\r\n" +
                    $"Clave actual:\r\n{claveActual}\r\n\r\n" +
                    "Sí       → Regenerar nueva clave\r\n" +
                    "No       → Desactivar (sin autenticación)\r\n" +
                    "Cancelar → Cerrar sin cambios",
                    "API Key — activa",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Information);

                if (r == DialogResult.Yes)
                {
                    string nuevaClave = GenerarApiKey();
                    var cfg = _controller.Config.Clone();
                    cfg.ApiKey = nuevaClave;
                    _controller.SaveAndApplyConfig(cfg);

                    try { Clipboard.SetText(nuevaClave); } catch { }
                    MessageBox.Show(
                        $"Nueva API Key generada:\r\n\r\n{nuevaClave}\r\n\r\n" +
                        "Copiada al portapapeles. Guárdela — no se volverá a mostrar.\r\n\r\n" +
                        "Inclúyala como encabezado HTTP:\r\n  X-Api-Key: <su clave>",
                        "API Key regenerada", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Log.Information("MostrarDialogoGestionApiKey: API key regenerada por el usuario");
                }
                else if (r == DialogResult.No)
                {
                    var confirmar = MessageBox.Show(
                        "El servidor quedará sin protección y aceptará peticiones de cualquier cliente local.\r\n\r\n" +
                        "¿Confirmar desactivación?",
                        "Desactivar API Key",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning);

                    if (confirmar != DialogResult.OK) return;

                    var cfg = _controller.Config.Clone();
                    cfg.ApiKey = string.Empty;
                    _controller.SaveAndApplyConfig(cfg);
                    MessageBox.Show("API Key desactivada. El servidor ya no requiere autenticación.",
                        "API Key", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Log.Information("MostrarDialogoGestionApiKey: API key desactivada por el usuario");
                }
            }
        }

        // ── Helpers combo impresoras ─────────────────────────────────────────────────

        private int FindPrinterItemIndex(string deviceName)
        {
            for (int i = 0; i < cbPrinters.Items.Count; i++)
                if ((cbPrinters.Items[i] as PrinterItem)?.DeviceName == deviceName)
                    return i;
            return -1;
        }

        // Devuelve true si el nombre corresponde a un puerto COM real (COM1, COM12, etc.)
        private static bool EsPuertoCOM(string deviceName)
            => deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
               && deviceName.Length > 3
               && deviceName[3..].All(char.IsDigit);

        // Aplica un nuevo estado a un PrinterItem y fuerza el repintado del ComboBox.
        // Debe llamarse desde el hilo UI.
        private void AplicarEstadoItemEnUI(PrinterItem item, string status)
        {
            item.UpdateStatus(status);
            int index = cbPrinters.Items.IndexOf(item);
            if (index != -1)
                cbPrinters.Items[index] = cbPrinters.Items[index];
        }

        private async Task RefrescarSoloEstadosAsync()
        {
            if (cbPrinters.DroppedDown || cbPrinters.Items.Count == 0 || !IsHandleCreated)
                return;

            var items = cbPrinters.Items.Cast<PrinterItem>().ToList();

            foreach (var item in items)
            {
                if (EsPuertoCOM(item.DeviceName))
                    continue; // los COM no necesitan consulta WMI periódica

                try
                {
                    var (ready, _) = await PrinterStatusChecker.TryGetPrinterReadyAsync(item.DeviceName);
                    string nuevoStatus = ready ? "Conectada" : "No detectada";

                    if (IsHandleCreated)
                        BeginInvoke(() => AplicarEstadoItemEnUI(item, nuevoStatus));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "RefrescarSoloEstadosAsync: error actualizando {Device}", item.DeviceName);
                }
            }
        }

        private async Task ActualizarEstadosAsync(IReadOnlyList<PrinterItem> items)
        {
            foreach (var item in items)
            {
                if (!IsHandleCreated) return;

                try
                {
                    string status;
                    if (EsPuertoCOM(item.DeviceName))
                    {
                        status = "Disponible";
                    }
                    else
                    {
                        var (ready, _) = await PrinterStatusChecker.TryGetPrinterReadyAsync(item.DeviceName);
                        status = ready ? "Conectada" : "No detectada";
                    }

                    if (!IsHandleCreated) return;
                    BeginInvoke(() => AplicarEstadoItemEnUI(item, status));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ActualizarEstadosAsync: error actualizando {Device}", item.DeviceName);
                }
            }
        }

        private sealed class PrinterItem(string deviceName)
        {
            public string DeviceName { get; } = deviceName;
            private string _status = "…";

            public void UpdateStatus(string status) => _status = status;
            public override string ToString() => $"{DeviceName} ({_status})";
        }

        // ── Test de impresión ────────────────────────────────────────────────────────

        private async void BtnTestImpresion_Click(object? sender, EventArgs e)
        {
            string tipo = cbTestTipo.SelectedItem?.ToString() ?? "";
            byte[] payload;

            if (tipo.StartsWith("ESC/POS"))
            {
                int cols = tipo.Contains("32") ? 32 : tipo.Contains("42") ? 42 : 48;
                payload = BuildTestEscPos(cols);
            }
            else
            {
                payload = Encoding.ASCII.GetBytes(BuildTestZpl());
            }

            try
            {
                btnTestImpresion.Enabled = false;

                var (isSuccess, statusCode, body) = await _controller.PostTestPayloadAsync(payload);

                if (isSuccess)
                    _tray.ShowNotification("Test de impresión", "Test enviado correctamente.");
                else
                    MessageBox.Show($"Error HTTP {statusCode}: {body}", "Test de impresión",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BtnTestImpresion_Click: error enviando test");
                MessageBox.Show($"No se pudo enviar: {ex.Message}", "Test de impresión",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTestImpresion.Enabled = _controller.IsRunning;
            }
        }

        private byte[] BuildTestEscPos(int cols)
        {
            return new EscPosBuilder(_encoding, cols)
                .Initialize()
                .AlignCenter()
                .BoldOn()
                .TextLine("TEST DE IMPRESION")
                .BoldOff()
                .TextLine($"Servidor TPV  ({cols} col)")
                .TextLine(System.DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"))
                .Separator()
                .AlignLeft()
                .LeftRight("Puerto:", _controller.Config.PuertoServidor.ToString())
                .LeftRight("Columnas:", cols.ToString())
                .Separator()
                .LineFeed(3)
                .Cut()
                .Build();
        }

        private static string BuildTestZpl()
        {
            string fecha = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            return $"^XA^FO50,50^A0N,40,40^FDTest Impresion^FS^FO50,110^A0N,30,30^FD{fecha}^FS^FO50,160^A0N,25,25^FDServidor TPV^FS^XZ";
        }

        private void BtnCancelar_Click(object sender, EventArgs e)
        {
            this.Hide();
        }

        private async void btnGuardar_Click(object sender, EventArgs e)
        {
            try
            {
                ActualizarValidacionUI();

                if (!int.TryParse(txtPort.Text.Trim(), out int puerto) || puerto <= 0 || puerto > 65535)
                {
                    MessageBox.Show("Puerto inválido. Debe ser un número entre 1 y 65535.", "Validación",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string seleccion = (cbPrinters.SelectedItem as PrinterItem)?.DeviceName
                                   ?? cbPrinters.SelectedItem?.ToString() ?? "";
                if (string.IsNullOrEmpty(seleccion))
                {
                    MessageBox.Show("Debe seleccionar un dispositivo.", "Validación",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var config = _controller.Config.Clone();
                int puertoPrevio = config.PuertoServidor; // capturar antes de modificar
                config.PuertoServidor = puerto;
                config.AplicarDispositivo(seleccion);

                // Advanced tab fields
                if (cbBaudRate.SelectedItem is int baud) config.BaudRate = baud;
                config.MaxTicketBytes = (int)nudMaxTicketKB.Value * 1024;

                Log.Information("btnGuardar_Click: dispositivo seleccionado. Tipo={Tipo}, Nombre={Nombre}",
                    string.IsNullOrEmpty(config.UltimoCOM) ? "USB" : "COM", seleccion);

                _controller.SaveAndApplyConfig(config);

                configValida = config.EsValida();

                // Servidor aún no arrancado (primera configuración válida): arrancarlo ahora.
                if (!_controller.IsRunning && configValida)
                {
                    try
                    {
                        await _controller.StartAsync(CancellationToken.None);
                        ActualizarEstadoTray(healthy: true, message: $"Servidor OK (puerto {puerto})");
                        Log.Information("btnGuardar_Click: servidor iniciado en puerto {Puerto}", puerto);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "btnGuardar_Click: error iniciando servidor. Puerto={Puerto}", puerto);
                        MessageBox.Show($"Configuración guardada, pero no se pudo iniciar el servidor en el puerto {puerto}.\n\nDetalle: {ex.Message}",
                            "Advertencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else if (_controller.IsRunning && puerto != puertoPrevio)
                {
                    // Servidor ya corriendo y cambió el puerto: reiniciar en el nuevo puerto.
                    try
                    {
                        await _controller.RestartPortAsync(puerto);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "btnGuardar_Click: puerto inválido o no disponible. Puerto={Puerto}", puerto);
                        MessageBox.Show($"No se puede usar el puerto {puerto}. Puede estar en uso o no tener permisos.\n\nDetalle: {ex.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                if (configValida)
                    startHidden = true;

                lblGuardado.Visible = true;
                _savedTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "btnGuardar_Click: error guardando configuración");
                MessageBox.Show("Error guardando configuración: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
