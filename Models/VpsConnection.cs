using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace QuakeServerManager.Models
{
    public class VpsConnection : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _ip = string.Empty;
        private int _port = 22;
        private string _username = "root";
        private string _password = string.Empty;
        private string _privateKeyPath = string.Empty;
        private AuthMethod _authMethod = AuthMethod.Password;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Ip
        {
            get => _ip;
            set => SetProperty(ref _ip, value);
        }

        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public string PrivateKeyPath
        {
            get => _privateKeyPath;
            set => SetProperty(ref _privateKeyPath, value);
        }

        public AuthMethod AuthMethod
        {
            get => _authMethod;
            set => SetProperty(ref _authMethod, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public enum AuthMethod
    {
        Password,
        PrivateKey
    }
} 