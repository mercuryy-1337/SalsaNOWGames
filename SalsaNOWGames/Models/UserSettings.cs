using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SalsaNOWGames.Models
{
    [DataContract]
    public class UserSettings
    {
        [DataMember]
        public string SteamId { get; set; }
        
        [DataMember]
        public string SteamUsername { get; set; }
        
        [DataMember]
        public string AvatarUrl { get; set; }
        
        [DataMember]
        public string InstallDirectory { get; set; }
        
        [DataMember]
        public List<string> InstalledAppIds { get; set; }
        
        [DataMember]
        public DateTime? LastLogin { get; set; }

        public UserSettings()
        {
            InstalledAppIds = new List<string>();
            InstallDirectory = @"I:\Apps\SalsaNOW\DepotDownloader\Games";
        }
    }
}
