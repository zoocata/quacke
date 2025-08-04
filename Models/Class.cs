using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace QuakeServerManager.Models
{
    public class Class : INotifyPropertyChanged
    {
        private string _name = string.Empty; // n
        private string _model = string.Empty; // m
        private int _baseSpeed = 320; // s
        private int _spawnHealth = 100; // h
        private int _maxArmour = 100; // a
        private int _armourClass = 1; // ac
        private int _hookType = 0; // ht
        private int _hookPull = 0; // hp
        private int _hookSpeed = 0; // hs
        private bool _doubleJump = false; // jd
        private bool _rampJump = false; // jr
        private string _weapon2 = string.Empty; // w2 (MG)
        private string _weapon3 = string.Empty; // w3 (SG)
        private string _weapon4 = string.Empty; // w4 (MGun)
        private string _weapon5 = string.Empty; // w5 (GL)
        private string _weapon6 = string.Empty; // w6 (RL)
        private string _weapon7 = string.Empty; // w7 (LG)
        private string _weapon8 = string.Empty; // w8 (PG)
        private int _startingWeapon = 3; // sw
        private string _description = string.Empty;
        private string _fileName = string.Empty;

        /// <summary>Class name (n)</summary>
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        /// <summary>Player model (m)</summary>
        public string Model { get => _model; set => SetProperty(ref _model, value); }
        /// <summary>Base speed (s)</summary>
        public int BaseSpeed { get => _baseSpeed; set => SetProperty(ref _baseSpeed, value); }
        /// <summary>Spawn health (h)</summary>
        public int SpawnHealth { get => _spawnHealth; set => SetProperty(ref _spawnHealth, value); }
        /// <summary>Maximum armour (a)</summary>
        public int MaxArmour { get => _maxArmour; set => SetProperty(ref _maxArmour, value); }
        /// <summary>Armour class (ac): 0=Green, 1=Yellow, 2=Red</summary>
        public int ArmourClass { get => _armourClass; set => SetProperty(ref _armourClass, value); }
        /// <summary>Hook type (ht): 0=None, 1=Offhand, 2=Onhand</summary>
        public int HookType { get => _hookType; set => SetProperty(ref _hookType, value); }
        /// <summary>Hook pull (hp)</summary>
        public int HookPull { get => _hookPull; set => SetProperty(ref _hookPull, value); }
        /// <summary>Hook speed (hs)</summary>
        public int HookSpeed { get => _hookSpeed; set => SetProperty(ref _hookSpeed, value); }
        /// <summary>Double jump (jd)</summary>
        public bool DoubleJump { get => _doubleJump; set => SetProperty(ref _doubleJump, value); }
        /// <summary>Ramp jump (jr)</summary>
        public bool RampJump { get => _rampJump; set => SetProperty(ref _rampJump, value); }
        /// <summary>Weapon2 (w2): MG</summary>
        public string Weapon2 { get => _weapon2; set => SetProperty(ref _weapon2, value); }
        /// <summary>Weapon3 (w3): SG</summary>
        public string Weapon3 { get => _weapon3; set => SetProperty(ref _weapon3, value); }
        /// <summary>Weapon4 (w4): Machine Gun</summary>
        public string Weapon4 { get => _weapon4; set => SetProperty(ref _weapon4, value); }
        /// <summary>Weapon5 (w5): Grenade Launcher</summary>
        public string Weapon5 { get => _weapon5; set => SetProperty(ref _weapon5, value); }
        /// <summary>Weapon6 (w6): Rocket Launcher</summary>
        public string Weapon6 { get => _weapon6; set => SetProperty(ref _weapon6, value); }
        /// <summary>Weapon7 (w7): Lightning Gun</summary>
        public string Weapon7 { get => _weapon7; set => SetProperty(ref _weapon7, value); }
        /// <summary>Weapon8 (w8): Plasma Gun</summary>
        public string Weapon8 { get => _weapon8; set => SetProperty(ref _weapon8, value); }
        /// <summary>Starting weapon (sw)</summary>
        public int StartingWeapon { get => _startingWeapon; set => SetProperty(ref _startingWeapon, value); }
        /// <summary>Description (for UI only)</summary>
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        /// <summary>File name for this class config</summary>
        public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }

        // Additional properties for UI display (these map to the existing properties)
        /// <summary>Display name for UI (same as Name)</summary>
        public string DisplayName { get => Name; set => Name = value; }
        /// <summary>Speed for UI (same as BaseSpeed)</summary>
        public int Speed { get => BaseSpeed; set => BaseSpeed = value; }
        /// <summary>Health for UI (same as SpawnHealth)</summary>
        public int Health { get => SpawnHealth; set => SpawnHealth = value; }
        /// <summary>Armor for UI (same as MaxArmour)</summary>
        public int Armor { get => MaxArmour; set => MaxArmour = value; }
        /// <summary>JumpDamage for UI (same as DoubleJump)</summary>
        public bool JumpDamage { get => DoubleJump; set => DoubleJump = value; }
        /// <summary>JumpRestriction for UI (same as RampJump)</summary>
        public bool JumpRestriction { get => RampJump; set => RampJump = value; }

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
} 