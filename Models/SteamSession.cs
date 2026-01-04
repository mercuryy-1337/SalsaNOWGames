using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

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

        // Store encrypted password for session persistence
        [DataMember]
        public string EncryptedPassword { get; set; }

        // Session is valid if we have username and either password or it hasn't expired
        public bool IsValid => !string.IsNullOrEmpty(Username) && 
                               (!string.IsNullOrEmpty(EncryptedPassword) || ExpiresAt > DateTime.Now);

        /*
         * Encrypts password using Windows DPAPI for secure storage
         * Source: https://stackoverflow.com/questions/12657792/how-to-securely-save-username-password-local
         */
        public void SetPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(password);
                byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                EncryptedPassword = Convert.ToBase64String(encrypted);
            }
            catch { }
        }

        public string GetPassword()
        {
            if (string.IsNullOrEmpty(EncryptedPassword)) return null;
            try
            {
                byte[] encrypted = Convert.FromBase64String(EncryptedPassword);
                byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return null; }
        }

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
