using System;
using System.Runtime.Serialization;

namespace SalsaNOWGames.Models
{
    [DataContract]
    public class SteamSession
    {
        [DataMember]
        public string SteamId { get; set; }

        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string AvatarUrl { get; set; }

        [DataMember]
        public string SteamLoginSecure { get; set; }

        [DataMember]
        public string SessionId { get; set; }

        [DataMember]
        public string SteamRememberLogin { get; set; }

        [DataMember]
        public DateTime ExpiresAt { get; set; }

        [DataMember]
        public string RefreshToken { get; set; }

        public bool IsValid => !string.IsNullOrEmpty(SteamLoginSecure) && 
                               !string.IsNullOrEmpty(SteamId) &&
                               ExpiresAt > DateTime.Now;

        public string GetDepotDownloaderArgs()
        {
            // DepotDownloader can use -username with refresh token or session
            // Format for using existing session
            if (!string.IsNullOrEmpty(RefreshToken))
            {
                return $"-username \"{Username}\" -remember-password";
            }
            return "";
        }
    }
}
