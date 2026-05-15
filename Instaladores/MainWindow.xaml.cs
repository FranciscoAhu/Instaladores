using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

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

        private async void Aceptar_Click(object sender, RoutedEventArgs e)
        {
            // Deshabilitar botones para evitar doble click y bloquear cancelación durante instalación
            AceptarButton.IsEnabled = false;

            try
            {
                await ExecuteSelectedAppsAsync();
                ShowInstallationSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al ejecutar instaladores: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                foreach (var app in Apps)
                {
                    app.ShowProgress = false;
                    app.IsBusy = false;
                }

                // Rehabilitar botones al terminar (o si hay error)
                AceptarButton.IsEnabled = true;
                CancelarButton.IsEnabled = true;
            }
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ShowInstallationSummary()
        {
            var selected = Apps?.Where(a => a.IsSelected).ToList();
            if (selected == null || selected.Count == 0)
                return;

            var message = "Resumen de Instalaciones:\r\n\r\n";
            bool hasErrors = false;

            foreach (var app in selected)
            {
                var status = app.InstallationSucceeded ? "✓ OK" : "✗ ERROR";
                message += $"{app.Nombre}: {status}";

                if (!app.InstallationSucceeded)
                {
                    hasErrors = true;
                    if (!string.IsNullOrEmpty(app.InstallationError))
                    {
                        message += $"\n  Motivo: {app.InstallationError}";
                    }
                }
                message += "\r\n";
            }

            MessageBox.Show(message, "Resumen de Instalaciones", MessageBoxButton.OK, 
                hasErrors ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        private void HP_Button(object sender, RoutedEventArgs e)
        {

            foreach (var app in Apps)
            {
                app.IsSelected = app.Id == "HP";
            }

        }
        private void Lenovo_Button(object sender, RoutedEventArgs e)
        {

            foreach (var app in Apps)
            {
                app.IsSelected = app.Id == "Lenovo";
            }

        }

        private bool IsApplicationInstalled(AppItem app)
        {
            if (string.IsNullOrWhiteSpace(app.Nombre))
                return false;

            try
            {
                // Buscar en el Registry de Windows (Programs and Features)
                // HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (key != null)
                    {
                        foreach (string subkeyName in key.GetSubKeyNames())
                        {
                            using (RegistryKey subkey = key.OpenSubKey(subkeyName))
                            {
                                if (subkey != null)
                                {
                                    object displayName = subkey.GetValue("DisplayName");
                                    if (displayName != null && displayName.ToString().IndexOf(app.Nombre, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }

                // También buscar en HKEY_CURRENT_USER para usuario actual
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (key != null)
                    {
                        foreach (string subkeyName in key.GetSubKeyNames())
                        {
                            using (RegistryKey subkey = key.OpenSubKey(subkeyName))
                            {
                                if (subkey != null)
                                {
                                    object displayName = subkey.GetValue("DisplayName");
                                    if (displayName != null && displayName.ToString().IndexOf(app.Nombre, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("install.log", $"[{DateTime.Now}] Error checking registry for {app.Nombre}: {ex.Message}\r\n");
            }

            return false;
        }

        private string FindInstallerFile(AppItem app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Ruta))
                return null;

            if (!Directory.Exists(app.Ruta))
                return null;

            if (string.Equals(app.Tipo, "msi", StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetFiles(app.Ruta, "*.msi")
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .FirstOrDefault();
            }

            return Directory.GetFiles(app.Ruta, "*.exe")
                .OrderByDescending(f => new FileInfo(f).CreationTime)
                .FirstOrDefault();
        }

        private async Task<int> RunInstallerAsync(string filePath, string args, IProgress<int> progress = null, bool elevate = true)
        {
            if (string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !string.Equals(Path.GetFileName(filePath), "msiexec", StringComparison.OrdinalIgnoreCase)))
                throw new FileNotFoundException("Installer not found", filePath);

            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = args ?? string.Empty,
                UseShellExecute = true,
                Verb = elevate ? "runas" : string.Empty,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                psi.WorkingDirectory = dir;

            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                        return -1;

                    progress?.Report(0);

                    var rnd = new Random();
                    int simulated = 0;

                    while (!proc.HasExited)
                    {
                        await Task.Delay(500);
                        simulated = Math.Min(95, simulated + rnd.Next(3, 10));
                        progress?.Report(simulated);
                    }

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

            // Activa ShowProgress solo en las seleccionadas → oculta checkboxes y bloquea selección
            foreach (var app in Apps)
            {
                app.ShowProgress = app.IsSelected;
                app.InstallationSucceeded = false;
                app.InstallationError = null;
                app.WasInstalled = false;
            }

            foreach (var app in selected)
            {
                var installer = FindInstallerFile(app);
                if (installer == null)
                {
                    app.InstallationSucceeded = false;
                    app.InstallationError = "No se encontró el instalador";
                    app.Progress = 0;
                    app.IsBusy = false;
                    MessageBox.Show($"Error al instalar {app.Nombre}:\n\nNo se encontró el instalador en {app.Ruta}", 
                        "Error de Instalación", MessageBoxButton.OK, MessageBoxImage.Error);
                    File.AppendAllText("install.log", $"[{DateTime.Now}] Installer not found for {app.Nombre} ({app.Ruta})\r\n");
                    continue;
                }

                try
                {
                    // Caso especial: Crystal Viewer ejecuta EXE y luego MSI
                    if (string.Equals(app.Id, "Crystal", StringComparison.OrdinalIgnoreCase))
                    {
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

                        int finalExitCode = 0;

                        if (!string.IsNullOrEmpty(exeInstaller))
                        {
                            File.AppendAllText("install.log", $"[{DateTime.Now}] Ejecutando {exeInstaller} (sin args) for {app.Nombre} (exe)\r\n");

                            app.IsBusy = true;
                            app.Progress = 0;

                            var progressExe = new Progress<int>(p => app.Progress = p);
                            var exitExe = await RunInstallerAsync(exeInstaller, string.Empty, progressExe, elevate: true);

                            File.AppendAllText("install.log", $"[{DateTime.Now}] ExitCode={exitExe} for {app.Nombre} (exe)\r\n");
                            finalExitCode = exitExe;
                        }

                        if (!string.IsNullOrEmpty(msiInstaller))
                        {
                            string msiArgs = $"/i \"{msiInstaller}\" /qn /norestart";

                            File.AppendAllText("install.log", $"[{DateTime.Now}] Ejecutando msiexec {msiArgs} for {app.Nombre} (msi)\r\n");

                            var progressMsi = new Progress<int>(p => app.Progress = p);
                            var exitMsi = await RunInstallerAsync("msiexec", msiArgs, progressMsi, elevate: true);

                            File.AppendAllText("install.log", $"[{DateTime.Now}] ExitCode={exitMsi} for {app.Nombre} (msi)\r\n");
                            finalExitCode = exitMsi;
                        }

                        if (finalExitCode == 0)
                        {
                            app.InstallationSucceeded = true;
                            app.Progress = 100;
                        }
                        else
                        {
                            app.InstallationSucceeded = false;
                            app.InstallationError = $"Error en la instalación (código: {finalExitCode})";
                            app.Progress = 0;
                            MessageBox.Show($"Error al instalar {app.Nombre}:\n\n{app.InstallationError}", 
                                "Error de Instalación", MessageBoxButton.OK, MessageBoxImage.Error);
                        }

                        app.IsBusy = false;
                        continue;
                    }

                    string fileToRun = installer;
                    string args = app.Args ?? string.Empty;

                    if (string.Equals(app.Tipo, "msi", StringComparison.OrdinalIgnoreCase))
                    {
                        string extra = app.Args ?? string.Empty;

                        if (extra.IndexOf(".msi", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (extra.TrimStart().StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
                                extra = extra.Substring(7).Trim();

                            extra = System.Text.RegularExpressions.Regex.Replace(extra, "\".*?\\.msi\"", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            extra = System.Text.RegularExpressions.Regex.Replace(extra, @"\S+\.msi", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        }

                        args = $"/i \"{installer}\" {extra}".Trim();
                        fileToRun = "msiexec";
                    }

                    File.AppendAllText("install.log", $"[{DateTime.Now}] Ejecutando {fileToRun} {args} for {app.Nombre}\r\n");

                    app.IsBusy = true;
                    app.Progress = 0;

                    var progress = new Progress<int>(p => app.Progress = p);
                    var exit = await RunInstallerAsync(fileToRun, args, progress, elevate: true);

                    File.AppendAllText("install.log", $"[{DateTime.Now}] ExitCode={exit} for {app.Nombre}\r\n");

                    if (exit == 0)
                    {
                        app.InstallationSucceeded = true;
                        app.Progress = 100;
                    }
                    else
                    {
                        app.InstallationSucceeded = false;
                        app.InstallationError = $"Error en la instalación (código: {exit})";
                        app.Progress = -1;
                        MessageBox.Show($"Error al instalar {app.Nombre}:\n\n{app.InstallationError}", 
                            "Error de Instalación", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    app.IsBusy = false;
                }
                catch (Exception ex)
                {
                    app.InstallationSucceeded = false;
                    app.InstallationError = ex.Message;
                    app.Progress = 0;
                    app.IsBusy = false;
                    MessageBox.Show($"Error al instalar {app.Nombre}:\n\n{ex.Message}", 
                        "Error de Instalación", MessageBoxButton.OK, MessageBoxImage.Error);
                    File.AppendAllText("install.log", $"[{DateTime.Now}] Exception for {app.Nombre}: {ex.Message}\r\n");
                }
            }
        }
    }
}