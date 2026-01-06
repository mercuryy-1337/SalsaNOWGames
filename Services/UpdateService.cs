using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace SalsaNOWGames.Services
{
    [DataContract]
    public class UpdateInfo
    {
        [DataMember(Name = "version")]
        public string Version { get; set; } = string.Empty;
        
        [DataMember(Name = "filename")]
        public string FileName { get; set; }
        
        [DataMember(Name = "download_url")]
        public string DownloadUrl { get; set; }
    }

    public class UpdateService
    {
        private const string VersionUrl = "https://savegfn.geforcenowspecs.cloud/salsa_version.json";

        public Version GetCurrentVersion()
        {
            try
            {
                var asmVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (asmVersion != null)
                    return asmVersion;
            }
            catch { }
            return new Version(0, 0, 0);
        }

        public async Task<UpdateInfo> CheckForUpdateAsync()
        {
            try
            {
                string json;
                using (var client = new WebClient())
                {
                    json = await client.DownloadStringTaskAsync(VersionUrl);
                }

                var serializer = new DataContractJsonSerializer(typeof(UpdateInfo));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var info = (UpdateInfo)serializer.ReadObject(ms);
                    
                    if (info == null || string.IsNullOrEmpty(info.Version))
                        return null;

                    if (!Version.TryParse(info.Version, out var remoteVersion))
                        return null;

                    var current = GetCurrentVersion();

                    if (remoteVersion > current)
                    {
                        return info;
                    }
                }
            }
            catch
            {
                // Silently fail - don't block app startup
            }
            return null;
        }

        public async Task<string> DownloadUpdateAsync(UpdateInfo info)
        {
            if (string.IsNullOrEmpty(info?.DownloadUrl)) return null;
            try
            {
                using (var client = new WebClient())
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), info.FileName ?? "SalsaNOWGames_update.exe");
                    await client.DownloadFileTaskAsync(info.DownloadUrl, tempPath);
                    return tempPath;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}