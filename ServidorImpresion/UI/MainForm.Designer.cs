namespace ServidorImpresion
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabPrincipal;
        private System.Windows.Forms.TabPage tabAvanzado;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.TextBox txtPort;
        private System.Windows.Forms.Label lblPrinter;
        private System.Windows.Forms.ComboBox cbPrinters;

        private System.Windows.Forms.Label lblBaudRate;
        private System.Windows.Forms.ComboBox cbBaudRate;
        private System.Windows.Forms.Label lblBaudRateNota;
        private System.Windows.Forms.Label lblMaxTicket;
        private System.Windows.Forms.NumericUpDown nudMaxTicketKB;
        private System.Windows.Forms.Label lblMaxTicketUnidad;
        private System.Windows.Forms.Button btnApiKey;
        private System.Windows.Forms.ComboBox cbTestTipo;
        private System.Windows.Forms.Button btnTestImpresion;
        private System.Windows.Forms.Button btnGuardar;
        private System.Windows.Forms.Button btnCancelar;
        private System.Windows.Forms.Label lblGuardado;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            tabMain = new System.Windows.Forms.TabControl();
            tabPrincipal = new System.Windows.Forms.TabPage();
            tabAvanzado = new System.Windows.Forms.TabPage();
            lblPort = new System.Windows.Forms.Label();
            txtPort = new System.Windows.Forms.TextBox();
            lblPrinter = new System.Windows.Forms.Label();
            cbPrinters = new System.Windows.Forms.ComboBox();

            lblBaudRate = new System.Windows.Forms.Label();
            cbBaudRate = new System.Windows.Forms.ComboBox();
            lblBaudRateNota = new System.Windows.Forms.Label();
            lblMaxTicket = new System.Windows.Forms.Label();
            nudMaxTicketKB = new System.Windows.Forms.NumericUpDown();
            lblMaxTicketUnidad = new System.Windows.Forms.Label();
            btnApiKey = new System.Windows.Forms.Button();
            cbTestTipo = new System.Windows.Forms.ComboBox();
            btnTestImpresion = new System.Windows.Forms.Button();
            btnGuardar = new System.Windows.Forms.Button();
            lblGuardado = new System.Windows.Forms.Label();
            btnCancelar = new System.Windows.Forms.Button();
            tabMain.SuspendLayout();
            tabPrincipal.SuspendLayout();
            tabAvanzado.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudMaxTicketKB).BeginInit();
            SuspendLayout();
            //
            // tabMain
            //
            tabMain.Controls.Add(tabPrincipal);
            tabMain.Controls.Add(tabAvanzado);
            tabMain.Location = new System.Drawing.Point(0, 0);
            tabMain.Name = "tabMain";
            tabMain.SelectedIndex = 0;
            tabMain.Size = new System.Drawing.Size(545, 210);
            tabMain.TabIndex = 0;
            //
            // tabPrincipal
            //
            tabPrincipal.Controls.Add(lblPort);
            tabPrincipal.Controls.Add(txtPort);
            tabPrincipal.Controls.Add(lblPrinter);
            tabPrincipal.Controls.Add(cbPrinters);

            tabPrincipal.Location = new System.Drawing.Point(4, 29);
            tabPrincipal.Name = "tabPrincipal";
            tabPrincipal.Padding = new System.Windows.Forms.Padding(3);
            tabPrincipal.Size = new System.Drawing.Size(537, 177);
            tabPrincipal.TabIndex = 0;
            tabPrincipal.Text = "Principal";
            tabPrincipal.UseVisualStyleBackColor = true;
            //
            // lblPort
            //
            lblPort.AutoSize = true;
            lblPort.Location = new System.Drawing.Point(12, 20);
            lblPort.Name = "lblPort";
            lblPort.TabIndex = 0;
            lblPort.Text = "Puerto servidor:";
            //
            // txtPort
            //
            txtPort.Location = new System.Drawing.Point(148, 17);
            txtPort.Name = "txtPort";
            txtPort.Size = new System.Drawing.Size(80, 27);
            txtPort.TabIndex = 1;
            //
            // lblPrinter
            //
            lblPrinter.AutoSize = true;
            lblPrinter.Location = new System.Drawing.Point(12, 62);
            lblPrinter.Name = "lblPrinter";
            lblPrinter.TabIndex = 2;
            lblPrinter.Text = "Impresora:";
            //
            // cbPrinters
            //
            cbPrinters.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbPrinters.FormattingEnabled = true;
            cbPrinters.Location = new System.Drawing.Point(148, 58);
            cbPrinters.Name = "cbPrinters";
            cbPrinters.Size = new System.Drawing.Size(340, 28);
            cbPrinters.TabIndex = 3;
            //
            // tabAvanzado
            //
            tabAvanzado.Controls.Add(lblBaudRate);
            tabAvanzado.Controls.Add(cbBaudRate);
            tabAvanzado.Controls.Add(lblBaudRateNota);
            tabAvanzado.Controls.Add(lblMaxTicket);
            tabAvanzado.Controls.Add(nudMaxTicketKB);
            tabAvanzado.Controls.Add(lblMaxTicketUnidad);
            tabAvanzado.Controls.Add(btnApiKey);
            tabAvanzado.Controls.Add(cbTestTipo);
            tabAvanzado.Controls.Add(btnTestImpresion);
            tabAvanzado.Location = new System.Drawing.Point(4, 29);
            tabAvanzado.Name = "tabAvanzado";
            tabAvanzado.Padding = new System.Windows.Forms.Padding(3);
            tabAvanzado.Size = new System.Drawing.Size(537, 177);
            tabAvanzado.TabIndex = 1;
            tabAvanzado.Text = "Avanzado";
            tabAvanzado.UseVisualStyleBackColor = true;
            //
            // lblBaudRate
            //
            lblBaudRate.AutoSize = true;
            lblBaudRate.Location = new System.Drawing.Point(12, 22);
            lblBaudRate.Name = "lblBaudRate";
            lblBaudRate.TabIndex = 0;
            lblBaudRate.Text = "Velocidad (baudios):";
            //
            // cbBaudRate
            //
            cbBaudRate.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbBaudRate.FormattingEnabled = true;
            cbBaudRate.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200 });
            cbBaudRate.Location = new System.Drawing.Point(188, 19);
            cbBaudRate.Name = "cbBaudRate";
            cbBaudRate.Size = new System.Drawing.Size(110, 28);
            cbBaudRate.TabIndex = 1;
            //
            // lblBaudRateNota
            //
            lblBaudRateNota.AutoSize = true;
            lblBaudRateNota.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont.FontFamily, 8f);
            lblBaudRateNota.ForeColor = System.Drawing.Color.Gray;
            lblBaudRateNota.Location = new System.Drawing.Point(188, 51);
            lblBaudRateNota.Name = "lblBaudRateNota";
            lblBaudRateNota.TabIndex = 2;
            lblBaudRateNota.Text = "Solo para impresoras COM";
            //
            // lblMaxTicket
            //
            lblMaxTicket.AutoSize = true;
            lblMaxTicket.Location = new System.Drawing.Point(12, 85);
            lblMaxTicket.Name = "lblMaxTicket";
            lblMaxTicket.TabIndex = 3;
            lblMaxTicket.Text = "Tamaño máximo ticket:";
            //
            // nudMaxTicketKB
            //
            nudMaxTicketKB.Location = new System.Drawing.Point(188, 82);
            nudMaxTicketKB.Maximum = new decimal(new int[] { 10240, 0, 0, 0 });
            nudMaxTicketKB.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudMaxTicketKB.Name = "nudMaxTicketKB";
            nudMaxTicketKB.Size = new System.Drawing.Size(80, 27);
            nudMaxTicketKB.TabIndex = 4;
            nudMaxTicketKB.ThousandsSeparator = true;
            nudMaxTicketKB.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // lblMaxTicketUnidad
            //
            lblMaxTicketUnidad.AutoSize = true;
            lblMaxTicketUnidad.Location = new System.Drawing.Point(275, 85);
            lblMaxTicketUnidad.Name = "lblMaxTicketUnidad";
            lblMaxTicketUnidad.TabIndex = 5;
            lblMaxTicketUnidad.Text = "KB  (máx. 10 240)";
            //
            // btnApiKey
            //
            btnApiKey.Location = new System.Drawing.Point(12, 130);
            btnApiKey.Name = "btnApiKey";
            btnApiKey.Size = new System.Drawing.Size(120, 30);
            btnApiKey.TabIndex = 6;
            btnApiKey.Text = "API Key...";
            btnApiKey.UseVisualStyleBackColor = true;
            btnApiKey.Click += (_, _) => MostrarDialogoGestionApiKey();
            //
            // cbTestTipo
            //
            cbTestTipo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbTestTipo.FormattingEnabled = true;
            cbTestTipo.Items.AddRange(new object[] { "ESC/POS 32 col", "ESC/POS 42 col", "ESC/POS 48 col", "ZPL" });
            cbTestTipo.Location = new System.Drawing.Point(145, 132);
            cbTestTipo.Name = "cbTestTipo";
            cbTestTipo.Size = new System.Drawing.Size(165, 28);
            cbTestTipo.SelectedIndex = 2;
            cbTestTipo.TabIndex = 7;
            //
            // btnTestImpresion
            //
            btnTestImpresion.Location = new System.Drawing.Point(317, 130);
            btnTestImpresion.Name = "btnTestImpresion";
            btnTestImpresion.Size = new System.Drawing.Size(120, 30);
            btnTestImpresion.TabIndex = 8;
            btnTestImpresion.Text = "Imprimir test";
            btnTestImpresion.UseVisualStyleBackColor = true;
            btnTestImpresion.Enabled = false;
            btnTestImpresion.Click += BtnTestImpresion_Click;
            //
            // lblGuardado
            //
            lblGuardado.AutoSize = true;
            lblGuardado.ForeColor = System.Drawing.Color.Green;
            lblGuardado.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont.FontFamily, 9f, System.Drawing.FontStyle.Bold);
            lblGuardado.Location = new System.Drawing.Point(12, 228);
            lblGuardado.Name = "lblGuardado";
            lblGuardado.Text = "✓ Configuración guardada";
            lblGuardado.Visible = false;
            //
            // btnCancelar
            //
            btnCancelar.Location = new System.Drawing.Point(432, 220);
            btnCancelar.Name = "btnCancelar";
            btnCancelar.Size = new System.Drawing.Size(100, 30);
            btnCancelar.TabIndex = 1;
            btnCancelar.Text = "Cancelar";
            btnCancelar.UseVisualStyleBackColor = true;
            btnCancelar.Click += BtnCancelar_Click;
            //
            // btnGuardar
            //
            btnGuardar.Location = new System.Drawing.Point(324, 220);
            btnGuardar.Name = "btnGuardar";
            btnGuardar.Size = new System.Drawing.Size(100, 30);
            btnGuardar.TabIndex = 2;
            btnGuardar.Text = "Guardar";
            btnGuardar.UseVisualStyleBackColor = true;
            btnGuardar.Click += btnGuardar_Click;
            //
            // MainForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(545, 265);
            Controls.Add(tabMain);
            Controls.Add(btnCancelar);
            Controls.Add(btnGuardar);
            Controls.Add(lblGuardado);
            Name = "MainForm";
            Text = "Servidor TPV — Configuración";
            tabMain.ResumeLayout(false);
            tabPrincipal.ResumeLayout(false);
            tabPrincipal.PerformLayout();
            tabAvanzado.ResumeLayout(false);
            tabAvanzado.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudMaxTicketKB).EndInit();
            ResumeLayout(false);
        }

        #endregion
    }
}
