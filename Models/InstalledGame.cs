using System.Runtime.Serialization;

namespace SalsaNOWGames.Models
{
    [DataContract]
    public class InstalledGame
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "is_installed")]
        public bool IsInstalled { get; set; }

        [DataMember(Name = "header_image_url")]
        public string HeaderImageUrl { get; set; }

        [DataMember(Name = "install_path")]
        public string InstallPath { get; set; }

        [DataMember(Name = "installed_date")]
        public string InstalledDate { get; set; }
    }
}
