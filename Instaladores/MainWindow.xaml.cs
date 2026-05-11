
using System;
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

        private async Task<int> RunInstallerAsync(string filePath, string args, IProgress<int> progress = null, bool elevate = true)
        {
            if (string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !string.Equals(Path.GetFileName(filePath), "msiexec", System.StringComparison.OrdinalIgnoreCase)))
                throw new FileNotFoundException("Installer not found", filePath);

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
                    if (proc == null)
                        return -1;

                    // report start
                    progress?.Report(0);

                    var rnd = new System.Random();
                    int simulated = 0;

                    // While the process is running, simulate progress increments up to 95%
                    while (!proc.HasExited)
                    {
                        await Task.Delay(500);
                        simulated = Math.Min(95, simulated + rnd.Next(3, 10));
                        progress?.Report(simulated);
                    }

                    // Ensure we report completion
                    try
                    {
                        progress?.Report(100);
                        return proc.ExitCode;
                    }
                    catch
                    {
                        return -1;
                    }
                }
            }
            catch
            {
                progress?.Report(0);
                return -1;
            }
        }

        private async Task ExecuteSelectedAppsAsync()
        {
            var selected = Apps?.Where(a => a.IsSelected).ToList();
            if (selected == null || selected.Count == 0)
                return;

            // Only show progress for selected apps
            foreach (var app in Apps)
            {
                app.ShowProgress = app.IsSelected;
            }

            foreach (var app in selected)
            {
                var installer = FindInstallerFile(app);
                if (installer == null)
                {
                    // no installer found: log and continue
                    File.AppendAllText("install.log", $"[{System.DateTime.Now}] Installer not found for {app.Nombre} ({app.Ruta})\r\n");
                    continue;
                }

                // Special case: Crystal Viewer should run both EXE then MSI when its single checkbox is selected
                if (string.Equals(app.Id, "Crystal", System.StringComparison.OrdinalIgnoreCase))
                {
                    // find exe and msi installers in the same folder
                    string exeInstaller = null;
                    string msiInstaller = null;
                    if (Directory.Exists(app.Ruta))
                    {
                        exeInstaller = Directory.GetFiles(app.Ruta, "*.exe")
                            .OrderByDescending(f => new FileInfo(f).CreationTime)
                            .FirstOrDefault();

                        msiInstaller = Directory.GetFiles(app.Ruta, "*.msi")
                            .OrderByDescending(f => new FileInfo(f).CreationTime)
                            .FirstOrDefault();
                    }

                    // run exe first (if present). This old exe must be run without arguments.
                    if (!string.IsNullOrEmpty(exeInstaller))
                    {
                        File.AppendAllText("install.log", $"[{System.DateTime.Now}] Ejecutando {exeInstaller} (sin args) for {app.Nombre} (exe)\r\n");

                        app.IsBusy = true;
                        app.Progress = 0;

                        var progressExe = new Progress<int>(p => app.Progress = p);
                        // run EXE with no arguments because it fails when passed args
                        var exitExe = await RunInstallerAsync(exeInstaller, string.Empty, progressExe, elevate: true);

                        File.AppendAllText("install.log", $"[{System.DateTime.Now}] ExitCode={exitExe} for {app.Nombre} (exe)\r\n");
                    }

                    // then run msi (if present)
                    if (!string.IsNullOrEmpty(msiInstaller))
                    {
                        // prepare msiexec arguments. Use /qn /norestart for silent install of the MSI.
                        string msiArgs = $"/i \"{msiInstaller}\" /qn /norestart";

                        File.AppendAllText("install.log", $"[{System.DateTime.Now}] Ejecutando msiexec {msiArgs} for {app.Nombre} (msi)\r\n");

                        var progressMsi = new Progress<int>(p => app.Progress = p);
                        var exitMsi = await RunInstallerAsync("msiexec", msiArgs, progressMsi, elevate: true);

                        File.AppendAllText("install.log", $"[{System.DateTime.Now}] ExitCode={exitMsi} for {app.Nombre} (msi)\r\n");
                    }

                    app.Progress = 100;
                    app.IsBusy = false;
                    continue;
                }

                string fileToRun = installer;
                string args = app.Args ?? string.Empty;

                if (string.Equals(app.Tipo, "msi", System.StringComparison.OrdinalIgnoreCase))
                {
                    // SIEMPRE usar el MSI encontrado en la carpeta (ignorar nombre fijo en Args)
                    string extra = app.Args ?? string.Empty;

                    // limpiar por si el JSON trae "msiexec" o "/i algo.msi"
                    if (extra.IndexOf(".msi", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // quitar msiexec si viene incluido
                        if (extra.TrimStart().StartsWith("msiexec", System.StringComparison.OrdinalIgnoreCase))
                            extra = extra.Substring(7).Trim();

                        // eliminar cualquier referencia a .msi
                        extra = System.Text.RegularExpressions.Regex.Replace(extra, "\".*?\\.msi\"", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        extra = System.Text.RegularExpressions.Regex.Replace(extra, @"\S+\.msi", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }

                    // construir args correctamente con el MSI encontrado
                        args = $"/i \"{installer}\" {extra}".Trim();

                    fileToRun = "msiexec";
                }

                File.AppendAllText("install.log", $"[{System.DateTime.Now}] Ejecutando {fileToRun} {args} for {app.Nombre}\r\n");

                app.IsBusy = true;
                app.Progress = 0;

                var progress = new Progress<int>(p =>
                {
                    app.Progress = p;
                });

                var exit = await RunInstallerAsync(fileToRun, args, progress, elevate: true);

                app.Progress = 100;
                app.IsBusy = false;

                File.AppendAllText("install.log", $"[{System.DateTime.Now}] ExitCode={exit} for {app.Nombre}\r\n");
            }
        }
    }
}