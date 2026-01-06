using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SalsaNOWGames.Services
{
    /*
     * Parses Steam's VDF files to extract user information
     * VDF format is Valve's proprietary key-value format used in Steam config files
     */
    public class SteamVdfService
    {
        private readonly string _loginUsersPath;

        public SteamVdfService()
        {
            _loginUsersPath = @"C:\Program Files (x86)\Steam\config\loginusers.vdf";
        }

        /*
         * Gets the PersonaName (display name) for a given Steam account username
         * Returns the username itself if PersonaName cannot be found
         */
        public string GetPersonaName(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
                return accountName;

            try
            {
                if (!File.Exists(_loginUsersPath))
                    return accountName;

                string content = File.ReadAllText(_loginUsersPath);
                
                // Find the user block that contains the matching AccountName
                // Pattern matches a user block with AccountName and PersonaName
                var userBlockPattern = new Regex(
                    @"""(\d+)""\s*\{[^}]*""AccountName""\s*""([^""]+)""[^}]*""PersonaName""\s*""([^""]+)""[^}]*\}",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                var matches = userBlockPattern.Matches(content);
                
                foreach (Match match in matches)
                {
                    string foundAccountName = match.Groups[2].Value;
                    string personaName = match.Groups[3].Value;
                    
                    if (foundAccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                    {
                        return personaName;
                    }
                }

                // Try alternate pattern where PersonaName comes before AccountName
                var altPattern = new Regex(
                    @"""(\d+)""\s*\{[^}]*""PersonaName""\s*""([^""]+)""[^}]*""AccountName""\s*""([^""]+)""[^}]*\}",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                matches = altPattern.Matches(content);
                
                foreach (Match match in matches)
                {
                    string personaName = match.Groups[2].Value;
                    string foundAccountName = match.Groups[3].Value;
                    
                    if (foundAccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                    {
                        return personaName;
                    }
                }
            }
            catch
            {
                // If anything fails, just return the account name but steam rarely changes this file format so i doubt it'll fail
            }

            return accountName;
        }
    }
}
