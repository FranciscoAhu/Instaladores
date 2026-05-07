
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        // Added missing event handler referenced from XAML
        private void Aceptar_Click(object sender, RoutedEventArgs e)
        {
            // Simple handler: close the window. Adjust behavior if needed.
            this.Close();
        }
        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            // Simple handler: close the window. Adjust behavior if needed.
            this.Close();
        }
    }
}