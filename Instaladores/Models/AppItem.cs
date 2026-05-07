using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.ComponentModel;

namespace Instaladores
{
    public class AppItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _nombre;
        private string _id;
        private string _ruta;
        private string _tipo;
        private string _args;
        private int _progress;
        private bool _isBusy;
        private bool _showProgress;

        public string Nombre
        {
            get => _nombre;
            set
            {
                if (_nombre == value) return;
                _nombre = value;
                OnPropertyChanged(nameof(Nombre));
            }
        }

        public string Id
        {
            get => _id;
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        public string Ruta
        {
            get => _ruta;
            set
            {
                if (_ruta == value) return;
                _ruta = value;
                OnPropertyChanged(nameof(Ruta));
            }
        }

        public string Tipo
        {
            get => _tipo;
            set
            {
                if (_tipo == value) return;
                _tipo = value;
                OnPropertyChanged(nameof(Tipo));
            }
        }

        public string Args
        {
            get => _args;
            set
            {
                if (_args == value) return;
                _args = value;
                OnPropertyChanged(nameof(Args));
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                if (_progress == value) return;
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
            }
        }

        public bool ShowProgress
        {
            get => _showProgress;
            set
            {
                if (_showProgress == value) return;
                _showProgress = value;
                OnPropertyChanged(nameof(ShowProgress));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string nombre)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
        }
    }
}