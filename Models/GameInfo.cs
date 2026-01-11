using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SalsaNOWGames.Models
{
    public class GameInfo : INotifyPropertyChanged
    {
        private string _appId;
        private string _name;
        private string _headerImageUrl;
        private string _installPath;
        private bool _isInstalled;
        private bool _isDownloading;
        private double _downloadProgress;
        private string _downloadStatus;
        private long _sizeOnDisk;
        private int _playtimeMinutes;
        private string _iconUrl;
        private bool _isInstalledViaSteam;
        private bool _isInstalledViaSalsa;
        private bool _hasShortcut;
        private bool _isLoadingImage;

        public string AppId
        {
            get => _appId;
            set { _appId = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string HeaderImageUrl
        {
            get => _headerImageUrl;
            set { _headerImageUrl = value; OnPropertyChanged(); }
        }

        public string InstallPath
        {
            get => _installPath;
            set { _installPath = value; OnPropertyChanged(); }
        }

        public bool IsInstalled
        {
            get => _isInstalled;
            set { _isInstalled = value; OnPropertyChanged(); }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        public string DownloadStatus
        {
            get => _downloadStatus;
            set { _downloadStatus = value; OnPropertyChanged(); }
        }

        public long SizeOnDisk
        {
            get => _sizeOnDisk;
            set { _sizeOnDisk = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeOnDiskFormatted)); }
        }

        public string SizeOnDiskFormatted
        {
            get
            {
                if (_sizeOnDisk <= 0) return "";
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = _sizeOnDisk;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        public int PlaytimeMinutes
        {
            get => _playtimeMinutes;
            set { _playtimeMinutes = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlaytimeFormatted)); }
        }

        public string PlaytimeFormatted
        {
            get
            {
                if (_playtimeMinutes <= 0) return "Never played";
                double hours = _playtimeMinutes / 60.0;
                if (hours < 1) return $"{_playtimeMinutes} mins";
                return $"{hours:0.#} hrs";
            }
        }

        public string IconUrl
        {
            get => _iconUrl;
            set { _iconUrl = value; OnPropertyChanged(); }
        }

        public bool IsInstalledViaSteam
        {
            get => _isInstalledViaSteam;
            set { _isInstalledViaSteam = value; OnPropertyChanged(); }
        }

        public bool IsInstalledViaSalsa
        {
            get => _isInstalledViaSalsa;
            set { _isInstalledViaSalsa = value; OnPropertyChanged(); }
        }

        public bool HasShortcut
        {
            get => _hasShortcut;
            set { _hasShortcut = value; OnPropertyChanged(); }
        }

        public bool IsLoadingImage
        {
            get => _isLoadingImage;
            set { _isLoadingImage = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
