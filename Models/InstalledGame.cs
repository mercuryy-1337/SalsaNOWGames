using System.Runtime.Serialization;

namespace SalsaNOWGames.Models
{
    [DataContract]
    public class InstallStatus
    {
        [DataMember(Name = "steam")]
        public bool Steam { get; set; }

        [DataMember(Name = "salsa")]
        public bool Salsa { get; set; }

        public bool IsInstalled => Steam || Salsa;
    }

    [DataContract]
    public class InstalledGame
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "install")]
        public InstallStatus Install { get; set; } = new InstallStatus();

        // Computed property - true if installed via Steam OR Salsa
        public bool IsInstalled => Install?.IsInstalled ?? false;

        [DataMember(Name = "header_image_url")]
        public string HeaderImageUrl { get; set; }

        [DataMember(Name = "install_path")]
        public string InstallPath { get; set; }

        [DataMember(Name = "installed_date")]
        public string InstalledDate { get; set; }

        [DataMember(Name = "has_shortcut")]
        public bool HasShortcut { get; set; }
    }
}
