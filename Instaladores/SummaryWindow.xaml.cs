using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Instaladores
{
    public partial class SummaryWindow : Window
    {
        public SummaryWindow(List<SummaryItem> items)
        {
            InitializeComponent();
            SummaryList.ItemsSource = items;
        }

        private void Cerrar_Click(object sender, RoutedEventArgs e) => this.Close();
    }

    public class SummaryItem
    {
        public string Nombre { get; set; }
        public string Status { get; set; }
        public Brush StatusColor { get; set; }
    }
}