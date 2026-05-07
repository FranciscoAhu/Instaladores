
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Instaladores
{
    public partial class MainWindow : Window
    {
        public List<AppItem> Apps { get; set; }
        public List<Profile> Profiles { get; set; }

        private Profile _selectedProfile;
        public Profile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                _selectedProfile = value;
                AplicarPerfil();
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            Apps = CargarApps();
            Profiles = CargarPerfiles();

            DataContext = this;
        }
        //cargan los json
        private List<AppItem> CargarApps()
        {
            var json = File.ReadAllText("apps.json");
            return JsonSerializer.Deserialize<List<AppItem>>(json);
        }

        private List<Profile> CargarPerfiles()
        {
            var json = File.ReadAllText("perfiles.json");
            return JsonSerializer.Deserialize<List<Profile>>(json);
        }
        //todo: Perfiles marcan checkbox!!!
        private void AplicarPerfil()
        {
            if (SelectedProfile == null)
                return;

            foreach (var app in Apps)
            {
                app.IsSelected = SelectedProfile.Apps.Contains(app.Id);
            }
        }

        // Execute installers for selected apps when user accepts
        private async void Aceptar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteSelectedAppsAsync();
                MessageBox.Show("Instalaciones completadas.", "Instaladores", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error al ejecutar instaladores: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Find an installer file inside the app.Ruta folder
        private string FindInstallerFile(AppItem app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Ruta))
                return null;

            if (!Directory.Exists(app.Ruta))
                return null;

            if (string.Equals(app.Tipo, "msi", System.StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetFiles(app.Ruta, "*.msi")
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .FirstOrDefault();
            }

            // default: look for exe installers
            return Directory.GetFiles(app.Ruta, "*.exe")
                .OrderByDescending(f => new FileInfo(f).CreationTime)
                .FirstOrDefault();
        }

        private Task<int> RunInstallerAsync(string filePath, string args, bool elevate = true)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Installer not found", filePath);

            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = args ?? string.Empty,
                    UseShellExecute = true,
                    Verb = elevate ? "runas" : string.Empty,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                // If the filePath includes a directory, set it as the working directory
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    psi.WorkingDirectory = dir;
                }

                try
                {
                    using (var proc = Process.Start(psi))
                    {
                        proc.WaitForExit();
                        return proc.ExitCode;
                    }
                }
                catch
                {
                    return -1;
                }
            });
        }

        private async Task ExecuteSelectedAppsAsync()
        {
            var selected = Apps?.Where(a => a.IsSelected).ToList();
            if (selected == null || selected.Count == 0)
                return;

            foreach (var app in selected)
            {
                var installer = FindInstallerFile(app);
                if (installer == null)
                {
                    // no installer found: log and continue
                    File.AppendAllText("install.log", $"[{System.DateTime.Now}] Installer not found for {app.Nombre} ({app.Ruta})\r\n");
                    continue;
                }

                string fileToRun = installer;
                string args = app.Args ?? string.Empty;

                if (string.Equals(app.Tipo, "msi", System.StringComparison.OrdinalIgnoreCase))
                {
                    // prepare msiexec arguments
                    var extra = args ?? string.Empty;
                    // if args already contains .msi, assume it references an installer and use it as-is (but remove leading 'msiexec')
                    if (extra.IndexOf(".msi", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // remove leading msiexec token if present
                        if (extra.TrimStart().StartsWith("msiexec", System.StringComparison.OrdinalIgnoreCase))
                            extra = extra.Substring(7).Trim();

                        args = extra;
                    }
                    else
                    {
                        args = $"/i \"{installer}\" {extra}".Trim();
                    }

                    fileToRun = "msiexec";
                }

                File.AppendAllText("install.log", $"[{System.DateTime.Now}] Ejecutando {fileToRun} {args} for {app.Nombre}\r\n");
                var exit = await RunInstallerAsync(fileToRun, args, elevate: true);
                File.AppendAllText("install.log", $"[{System.DateTime.Now}] ExitCode={exit} for {app.Nombre}\r\n");
            }
        }
    }
}