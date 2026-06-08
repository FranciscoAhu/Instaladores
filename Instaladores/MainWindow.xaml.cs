using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Instaladores
{
    public partial class MainWindow : Window
    {
        public List<AppItem> Apps { get; set; }
        public List<Profile> Profiles { get; set; }
        private List<Process> _runningProcesses = new List<Process>();
        private bool _isCancellationRequested = false;

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
            // Verificar conexión a internet antes de iniciar
            if (!HayConexionInternet())
            {
                MessageBox.Show(
                    "Se requiere conexión a internet para ejecutar esta aplicación.",
                    "Sin conexión",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            // Preguntar modo de instalación
            var resultado = MessageBox.Show(
                "¿Deseas instalar desde la red corporativa?\n\nSí \nNo = Local",
                "Modo de instalación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            bool usarRed = resultado == MessageBoxResult.Yes;

            if (usarRed)
                // Mapear unidad de red
                MapearUnidadRed();

            if (!usarRed)
                MessageBox.Show(
                    "Para usar el modo local debes copiar la carpeta Apps en la siguiente ruta C:/Users/Artemisa/Desktop",
                    "Modo local",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                    );

            InitializeComponent();

            Apps = CargarApps(usarRed);
            Profiles = CargarPerfiles();

            DataContext = this;
        }
        private bool HayConexionInternet()
        {
            try
            {
                using (var client = new System.Net.WebClient())
                using (client.OpenRead("http://www.google.com"))
                    return true;
            }
            catch
            {
                return false;
            }
        }

        

        private static readonly byte[] KEY = Encoding.UTF8.GetBytes("Cns2024$SecretK1"); // exactamente 16 caracteres
        private static readonly byte[] IV = Encoding.UTF8.GetBytes("Cns2024$InitVec1"); // exactamente 16 caracteres

        private string DesencriptarPassword(string encrypted)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = KEY;
                aes.IV = IV;

                byte[] data = Convert.FromBase64String(encrypted);

                using (var decryptor = aes.CreateDecryptor())
                using (var ms = new MemoryStream(data))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var reader = new StreamReader(cs))
                    return reader.ReadToEnd();
            }
        }

        private string EncriptarPassword(string password)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = KEY;
                aes.IV = IV;

                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    byte[] data = Encoding.UTF8.GetBytes(password);
                    cs.Write(data, 0, data.Length);
                    cs.FlushFinalBlock();
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }
        private const string PASSWORD_CIFRADA = "el_base64_que_generaste";
        private void MapearUnidadRed()
        {
            try
            {
                // Primero verificar si ya es accesible sin mapear
                if (Directory.Exists(@"\\REPOCST\Apps"))
                {
                    File.AppendAllText("install.log", $"[{DateTime.Now}] Ruta UNC ya accesible sin mapear\r\n");
                    return;
                }
                string password = DesencriptarPassword("BVwoGgSfD11UuDumlO1ilA==");
                // Intentar con credenciales
                var psi = new ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = $@"use \\REPOCST\Apps /user:artemisa {password}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    File.AppendAllText("install.log", $"[{DateTime.Now}] net use ExitCode: {proc.ExitCode}\r\n");
                    File.AppendAllText("install.log", $"[{DateTime.Now}] net use Output: {output}\r\n");
                    File.AppendAllText("install.log", $"[{DateTime.Now}] net use Error: {error}\r\n");
                }

                // Esperar que se estabilice
                System.Threading.Thread.Sleep(3000);

                // Verificar acceso
                bool accesible = Directory.Exists(@"\\REPOCST\Apps");
                File.AppendAllText("install.log", $"[{DateTime.Now}] Accesible después del mapeo: {accesible}\r\n");

                if (!accesible)
                {
                    MessageBox.Show(
                        "No se pudo conectar al servidor de instaladores.\n\nVerifica que el equipo esté en la red corporativa.",
                        "Error de red",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("install.log", $"[{DateTime.Now}] Excepción MapearUnidadRed: {ex.Message}\r\n");
                MessageBox.Show(
                    $"Error al conectar con el servidor:\n\n{ex.Message}",
                    "Error de red",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private List<AppItem> CargarApps(bool usarRed)
        {
            string archivo = usarRed ? "appsRed.json" : "appsLocal.json";
            var json = File.ReadAllText(archivo);
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

            // Guardar el estado de los fabricantes antes de aplicar el perfil
            var hpApp = Apps.FirstOrDefault(a => a.Id == "HP");
            var lenovoApp = Apps.FirstOrDefault(a => a.Id == "Lenovo");

            bool wasHpSelected = hpApp?.IsSelected ?? false;
            bool wasLenovoSelected = lenovoApp?.IsSelected ?? false;

            // Aplicar el perfil
            foreach (var app in Apps)
            {
                app.IsSelected = SelectedProfile.Apps.Contains(app.Id);
            }

            // Restaurar el estado de los fabricantes
            if (hpApp != null)
                hpApp.IsSelected = wasHpSelected;
            if (lenovoApp != null)
                lenovoApp.IsSelected = wasLenovoSelected;
        }

        private async void Aceptar_Click(object sender, RoutedEventArgs e)
        {
            // Deshabilitar botones para evitar doble click y bloquear cancelación durante instalación
            AceptarButton.IsEnabled = false;
            HPButton.IsEnabled = false;
            LenovoButton.IsEnabled = false;

            // Resetear la bandera de cancelación
            _isCancellationRequested = false;

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
                HPButton.IsEnabled = true;
                LenovoButton.IsEnabled = true;
            }
        }

        private List<int> GetChildProcessIds(int parentId)
        {
            var result = new List<int>();
            var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentId}");

            foreach (ManagementObject obj in searcher.Get())
            {
                int childId = Convert.ToInt32(obj["ProcessId"]);
                result.Add(childId);

                //  recursivo 
                result.AddRange(GetChildProcessIds(childId));
            }

            return result;
        }

        private void KillProcessTree(int parentPid)
        {
            try
            {
                var allPids = new List<int>();

                // obtener todos los hijos
                allPids.AddRange(GetChildProcessIds(parentPid));

                // agregar el padre al final
                allPids.Add(parentPid);

                // matar desde los hijos hacia arriba
                foreach (var pid in allPids.Distinct().Reverse<int>())
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);

                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            proc.WaitForExit(2000);
                        }
                    }
                    catch
                    {
                        // puede fallar si ya murió
                    }
                }
            }
            catch
            {
                // fallback 
                Process.Start("taskkill", $"/F /PID {parentPid} /T");
            }
        }
        // Este método se ejecuta cuando el usuario hace clic en el botón "Cancelar". Establece la bandera de cancelación, intenta matar todos los procesos en ejecución relacionados con las instalaciones y muestra un mensaje de confirmación al usuario. También registra las acciones de cancelación en el archivo de log. Después de cancelar, cierra la ventana principal.
        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            _isCancellationRequested = true;

            foreach (var process in _runningProcesses.ToList())
            {
                try
                {
                    if (process != null)
                    {
                        KillProcessTree(process.Id);

                        File.AppendAllText("install.log",
                            $"[{DateTime.Now}] Árbol de proceso PID {process.Id} cancelado\r\n");
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText("install.log",
                        $"[{DateTime.Now}] Error al cancelar PID {process?.Id}: {ex.Message}\r\n");
                }
            }

            _runningProcesses.Clear();

            /*MessageBox.Show("Las instalaciones fueron canceladas.",
                            "Cancelación",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
            */

            this.Close();
        }

        // Muestra una ventana resumen al finalizar la instalación, indicando qué aplicaciones se instalaron correctamente y cuáles tuvieron errores. Solo muestra las aplicaciones que fueron seleccionadas para instalación. Cada aplicación se muestra con su nombre, estado (OK o ERROR) y un color verde para éxito o rojo para error.
        private void ShowInstallationSummary()
        {
            var selected = Apps?.Where(a => a.IsSelected).ToList();
            if (selected == null || selected.Count == 0)
                return;

            var items = new List<SummaryItem>();

            foreach (var app in selected)
            {
                items.Add(new SummaryItem
                {
                    Nombre = app.Nombre,
                    Status = app.InstallationSucceeded ? " OK" : " ERROR",
                    StatusColor = app.InstallationSucceeded
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green)
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Tomato)
                });
            }

            var summary = new SummaryWindow(items);
            summary.ShowDialog();
        }
        //Botones
        private void HP_Button(object sender, RoutedEventArgs e)
        {
            var hpApp = Apps.FirstOrDefault(a => a.Id == "HP");
            var lenovoApp = Apps.FirstOrDefault(a => a.Id == "Lenovo");

            if (hpApp != null)
            {
                hpApp.IsSelected = true;
            }

            if (lenovoApp != null)
            {
                lenovoApp.IsSelected = false;
            }
        }

        private void Lenovo_Button(object sender, RoutedEventArgs e)
        {
            var lenovoApp = Apps.FirstOrDefault(a => a.Id == "Lenovo");
            var hpApp = Apps.FirstOrDefault(a => a.Id == "HP");

            if (lenovoApp != null)
            {
                lenovoApp.IsSelected = true;
            }

            if (hpApp != null)
            {
                hpApp.IsSelected = false;
            }
        }

        private void DeselectAll_Button(object sender, RoutedEventArgs e)
        {
            foreach (var app in Apps)
            {
                app.IsSelected = false;
            }
        }
        /* Este método no se está utilizando actualmente, pero se puede usar para verificar si una aplicación ya está instalada antes de intentar instalarla. */
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

        /* Este método busca el archivo instalador más reciente(.msi o .exe) en la ruta especificada para la aplicación.Si el tipo es "msi", busca archivos.msi; de lo contrario, busca archivos.exe.Devuelve la ruta completa del instalador encontrado o null si no se encuentra ninguno. */
        private string FindInstallerFile(AppItem app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Ruta))
            {
                File.AppendAllText("install.log", $"[{DateTime.Now}] FindInstallerFile: app o ruta nula para {app?.Nombre}\r\n");
                return null;
            }

            File.AppendAllText("install.log", $"[{DateTime.Now}] Buscando instalador en: {app.Ruta}\r\n");

            // Reintentar varias veces si la ruta no existe (para dar tiempo al mapeo de red)
            int maxRetries = 5;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                if (Directory.Exists(app.Ruta))
                {
                    File.AppendAllText("install.log", $"[{DateTime.Now}] Directorio encontrado: {app.Ruta}\r\n");

                    if (string.Equals(app.Tipo, "msi", StringComparison.OrdinalIgnoreCase))
                    {
                        var files = Directory.GetFiles(app.Ruta, "*.msi")
                            .OrderByDescending(f => new FileInfo(f).CreationTime)
                            .ToList();

                        File.AppendAllText("install.log", $"[{DateTime.Now}] Archivos MSI encontrados: {files.Count}\r\n");

                        if (files.Count > 0)
                        {
                            File.AppendAllText("install.log", $"[{DateTime.Now}] Usando: {files.First()}\r\n");
                            return files.First();
                        }
                    }

                    var exeFiles = Directory.GetFiles(app.Ruta, "*.exe")
                        .OrderByDescending(f => new FileInfo(f).CreationTime)
                        .ToList();

                    File.AppendAllText("install.log", $"[{DateTime.Now}] Archivos EXE encontrados: {exeFiles.Count}\r\n");

                    if (exeFiles.Count > 0)
                    {
                        File.AppendAllText("install.log", $"[{DateTime.Now}] Usando: {exeFiles.First()}\r\n");
                        return exeFiles.First();
                    }

                    // No encontró instalador
                    File.AppendAllText("install.log", $"[{DateTime.Now}] No se encontraron archivos .exe o .msi en {app.Ruta}\r\n");
                    return null;
                }

                retryCount++;
                if (retryCount < maxRetries)
                {
                    File.AppendAllText("install.log", $"[{DateTime.Now}] Directorio no encontrado: {app.Ruta} (intento {retryCount}/{maxRetries})\r\n");
                    System.Threading.Thread.Sleep(1000); // Esperar 1 segundo antes de reintentar
                }
            }

            File.AppendAllText("install.log", $"[{DateTime.Now}] Directorio NO encontrado después de {maxRetries} intentos: {app.Ruta}\r\n");
            return null;
        }
        /* este método ejecuta el instalador especificado con los argumentos proporcionados y reporta el progreso a través de la interfaz IProgress<int>. Si elevate es true, se ejecutará con privilegios elevados. El método maneja la ejecución del proceso, monitorea su progreso simulado y devuelve el código de salida al finalizar. También maneja excepciones y reporta errores a través del progreso. */
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

                    // Añadir el proceso a la lista de procesos en ejecución
                    lock (_runningProcesses)
                    {
                        _runningProcesses.Add(proc);
                    }

                    progress?.Report(0);

                    var rnd = new Random();
                    int simulated = 0;

                    while (!proc.HasExited)
                    {
                        await Task.Delay(500);
                        simulated = Math.Min(95, simulated + rnd.Next(3, 10));
                        progress?.Report(simulated);
                    }

                    // Remover el proceso de la lista cuando termina
                    lock (_runningProcesses)
                    {
                        _runningProcesses.Remove(proc);
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
        /* Este método es el núcleo de la aplicación, encargado de ejecutar los instaladores seleccionados por el usuario.Primero, filtra las aplicaciones seleccionadas y luego itera sobre ellas para ejecutar sus instaladores correspondientes. Durante la ejecución, maneja casos especiales (como Crystal Viewer), reporta el progreso, maneja errores y permite la cancelación de la instalación.Al finalizar, actualiza el estado de cada aplicación según el resultado de la instalación. */
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
                // Verificar si se ha solicitado cancelación
                if (_isCancellationRequested)
                {
                    File.AppendAllText("install.log", $"[{DateTime.Now}] Instalación cancelada por el usuario\r\n");
                    break;
                }

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