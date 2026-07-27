using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Controls
{
    /// <summary>
    /// The encryption settings (#132 step 3): the switch, where the certificate comes
    /// from, and what is actually being served.
    ///
    /// Until now these were config-file only, while every other endpoint setting - port,
    /// host name, authentication, access log - was editable from the tray or the
    /// dashboard. Telling a user to hand-edit JSON for the one feature about
    /// confidentiality was the last thing keeping #132 open.
    ///
    /// A SOURCE is picked and only its own fields are shown, rather than three paths and
    /// a thumbprint all at once. That is not only tidier: the three are mutually
    /// exclusive in <see cref="CertificateResolver"/>, and showing them together invites
    /// filling in two.
    ///
    /// NOT a container control. The first cut was a nested TableLayoutPanel, and its
    /// caption column auto-sized independently of the dialog's - "Certificate from" is
    /// wider than "Authentication", so every field in this section sat four pixels right
    /// of every field above it. Two grids cannot agree on a column width by accident, so
    /// this one contributes its rows to the dialog's own grid and alignment stops being
    /// something anyone has to maintain.
    /// </summary>
    public class CertificateSettingsSection
    {
        private readonly ConfigService _config;
        private readonly int _wrapWidth;
        private readonly int _fieldWidth;

        private readonly CheckBox _useHttps = new CheckBox();
        private readonly ComboBox _source = new ComboBox();
        private readonly Label _sourceDescription = new Label();
        private readonly Label _primaryCaption = new Label { Text = "Certificate" };
        private readonly TextBox _primary = new TextBox();
        private readonly Button _browse = new Button();
        private readonly Label _keyCaption = new Label { Text = "Private key" };
        private readonly TextBox _key = new TextBox();
        private readonly Button _browseKey = new Button();
        private readonly Label _certificate = new Label();

        /// <summary>
        /// The cells the file rows occupy: a text box and its browse button travel
        /// together, so they are one control as far as the dialog's grid is concerned
        /// and the grid stays two columns wide.
        /// </summary>
        private readonly FlowLayoutPanel _primaryCell = NewCell();
        private readonly FlowLayoutPanel _keyCell = NewCell();

        /// <summary>
        /// A path long enough to matter does not fit the field, and the end - the file
        /// name - is the half that gets cut. Hovering shows the whole thing.
        /// </summary>
        private readonly ToolTip _paths = new ToolTip();

        private bool _loading;

        private readonly int _gap;

        /// <param name="gap">
        /// Space between a field and its browse button, already scaled for the display.
        /// </param>
        public CertificateSettingsSection(ConfigService config, int wrapWidth, int fieldWidth, int gap)
        {
            _config = config;
            _wrapWidth = wrapWidth;
            _fieldWidth = fieldWidth;
            _gap = gap;
        }

        /// <summary>
        /// A field and its browse button, laid out so the field's LEFT edge is exactly
        /// the cell's. FlowLayoutPanel gives its children a default margin, which put
        /// every text box three pixels right of the combo boxes above them - the same
        /// misalignment this section was restructured to remove, one level down.
        /// </summary>
        private void FillCell(FlowLayoutPanel cell, TextBox field, Button browse)
        {
            field.Width = _fieldWidth;
            field.Margin = new Padding(0);
            browse.AutoSize = true;
            browse.Text = "…";
            browse.Margin = new Padding(_gap, 0, 0, 0);

            cell.Controls.Add(field);
            cell.Controls.Add(browse);
        }

        private HttpBridgeConfig Settings => _config?.Current?.HttpBridge;

        /// <summary>
        /// Adds this section's rows to the dialog's layout, using the dialog's own row
        /// helpers so the rows are indistinguishable from the ones above them.
        /// </summary>
        public void AddTo(Action<Control> addSpanning, Action<Label, Control> addField)
        {
            _useHttps.AutoSize = true;
            _useHttps.Text = "Encrypt connections (HTTPS)";
            addSpanning(_useHttps);

            _source.DropDownStyle = ComboBoxStyle.DropDownList;
            _source.Width = _fieldWidth;
            foreach (CertificateSource source in CertificateSourceLabel.Order)
                _source.Items.Add(CertificateSourceLabel.For(source));
            addField(new Label { Text = "Certificate from" }, _source);

            _sourceDescription.AutoSize = true;
            _sourceDescription.MaximumSize = new Size(_wrapWidth, 0);
            _sourceDescription.ForeColor = SystemColors.GrayText;
            addSpanning(_sourceDescription);

            _primary.Name = "certificateField";
            FillCell(_primaryCell, _primary, _browse);
            addField(_primaryCaption, _primaryCell);

            _key.Name = "certificateKeyField";
            FillCell(_keyCell, _key, _browseKey);
            addField(_keyCaption, _keyCell);

            // What is being served and until when. The subject and expiry were already
            // on EndpointStatus and nothing showed them, so the only way to find out
            // which certificate was live was to read log.txt at startup.
            _certificate.AutoSize = true;
            _certificate.MaximumSize = new Size(_wrapWidth, 0);
            addSpanning(_certificate);

            Wire();
            Load();
        }

        private static FlowLayoutPanel NewCell() => new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };

        private void Wire()
        {
            _useHttps.CheckedChanged += (s, e) =>
            {
                if (_loading) return;

                // Commit whatever is typed first: someone fills the paths in and ticks
                // the box, and the box would otherwise be validated against the empty
                // settings they just replaced.
                CommitPaths();

                var (ok, message) = _config.SetUseHttps(_useHttps.Checked);
                if (!ok)
                {
                    // Refused, so the checkbox must not stay ticked - it would claim a
                    // state the configuration does not have.
                    _loading = true;
                    try { _useHttps.Checked = false; } finally { _loading = false; }
                }

                ShowProblem(message);
                Refresh(Settings);
            };

            _source.SelectedIndexChanged += (s, e) =>
            {
                if (_loading || _source.SelectedIndex < 0) return;

                CertificateSource source = CertificateSourceLabel.Order[_source.SelectedIndex];
                _sourceDescription.Text = CertificateSourceLabel.Describe(source);

                // Changing source clears the fields rather than carrying a path into a
                // thumbprint box: they are different kinds of value, and the resolver
                // would prefer whichever was left behind.
                _loading = true;
                try { _primary.Text = string.Empty; _key.Text = string.Empty; } finally { _loading = false; }

                ApplyFieldVisibility(source);
                _config.SetCertificate(source);
                ShowProblem(null);
            };

            _primary.Leave += (s, e) => CommitPaths();
            _key.Leave += (s, e) => CommitPaths();

            _browse.Click += (s, e) => Pick(_primary, CurrentSource() == CertificateSource.PfxFile
                ? "Certificates (*.pfx;*.p12)|*.pfx;*.p12|All files (*.*)|*.*"
                : "Certificate (*.pem;*.crt;*.cer)|*.pem;*.crt;*.cer|All files (*.*)|*.*");

            _browseKey.Click += (s, e) =>
                Pick(_key, "Private key (*.pem;*.key)|*.pem;*.key|All files (*.*)|*.*");
        }

        private void Pick(TextBox target, string filter)
        {
            using (var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true })
            {
                if (!string.IsNullOrWhiteSpace(target.Text))
                {
                    try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(target.Text); }
                    catch { /* a half-typed path is not a reason to fail to open a dialog */ }
                }

                if (dialog.ShowDialog(target.FindForm()) != DialogResult.OK) return;

                target.Text = dialog.FileName;
                CommitPaths();
            }
        }

        private CertificateSource CurrentSource() =>
            _source.SelectedIndex < 0
                ? CertificateSource.PemPair
                : CertificateSourceLabel.Order[_source.SelectedIndex];

        /// <summary>
        /// Writes the typed values through, then reports what they resolve to. Called on
        /// Leave and before the dialog closes, so a value typed and confirmed with the
        /// keyboard is never silently discarded.
        /// </summary>
        public void CommitPaths()
        {
            if (_loading || _config == null) return;

            CertificateSource source = CurrentSource();
            if (source == CertificateSource.WindowsStore)
                _config.SetCertificate(source, thumbprint: _primary.Text);
            else
                _config.SetCertificate(source, path: _primary.Text, keyPath: _key.Text);

            Refresh(Settings);
        }

        /// <summary>
        /// Describes what the current settings resolve to - the certificate and its
        /// expiry, or the reason there isn't one. <see cref="CertificateResolver"/>
        /// already returns a specific message for every wrong input, so this is where
        /// they are shown rather than at the next failed start.
        /// </summary>
        public void Refresh(HttpBridgeConfig settings)
        {
            if (settings == null) return;

            _paths.SetToolTip(_primary, _primary.Text);
            _paths.SetToolTip(_key, _key.Text);

            bool configured =
                !string.IsNullOrWhiteSpace(settings.CertificatePath) ||
                !string.IsNullOrWhiteSpace(settings.CertificateThumbprint);

            if (!configured)
            {
                _certificate.Text = string.Empty;
                return;
            }

            CertificateResolution resolved = CertificateResolver.Resolve(
                settings.CertificatePath, settings.CertificateThumbprint, settings.CertificateKeyPath);

            if (resolved.Ok)
            {
                int days = (int)(resolved.Certificate.NotAfter - DateTime.Now).TotalDays;
                _certificate.ForeColor = days <= 14 ? Color.Firebrick : Color.ForestGreen;
                _certificate.Text =
                    $"{resolved.Certificate.Subject}, valid until " +
                    $"{resolved.Certificate.NotAfter:yyyy-MM-dd}" +
                    (days < 0 ? " - EXPIRED, clients will refuse it."
                              : days <= 14 ? $" - {days} day(s) left." : ".");
                resolved.Certificate.Dispose();
            }
            else
            {
                ShowProblem(resolved.Problem);
            }
        }

        private void ShowProblem(string problem)
        {
            if (string.IsNullOrEmpty(problem)) return;
            _certificate.ForeColor = Color.Firebrick;
            _certificate.Text = problem;
        }

        private void Load()
        {
            HttpBridgeConfig settings = Settings;
            if (settings == null) return;

            _loading = true;
            try
            {
                CertificateSource source = CertificateSourceLabel.SourceOf(settings);
                _source.SelectedIndex = CertificateSourceLabel.Order.ToList().IndexOf(source);
                _sourceDescription.Text = CertificateSourceLabel.Describe(source);

                _primary.Text = source == CertificateSource.WindowsStore
                    ? settings.CertificateThumbprint ?? string.Empty
                    : settings.CertificatePath ?? string.Empty;
                _key.Text = settings.CertificateKeyPath ?? string.Empty;

                _useHttps.Checked = settings.UseHttps;
                ApplyFieldVisibility(source);
            }
            finally
            {
                _loading = false;
            }

            Refresh(settings);
        }

        /// <summary>Only the fields the chosen source actually uses.</summary>
        private void ApplyFieldVisibility(CertificateSource source)
        {
            bool store = source == CertificateSource.WindowsStore;
            bool pem = source == CertificateSource.PemPair;

            _primaryCaption.Text = store ? "Thumbprint" : pem ? "Certificate" : "PFX file";
            _browse.Visible = !store;

            _keyCaption.Visible = pem;
            _keyCell.Visible = pem;

            _primary.PlaceholderText = store ? "paste from the certificate dialog"
                                     : pem ? "fullchain.pem" : "certificate.pfx";
            if (pem) _key.PlaceholderText = "privkey.pem";
        }
    }
}
