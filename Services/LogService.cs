using System;
using System.IO;
using System.Text;

namespace SalsaNOWGames.Services
{
    /// <summary>
    /// Simple logging service for debugging application issues.
    /// Logs are written to %LOCALAPPDATA%\SalsaNOWGames\salsa.log
    /// </summary>
    public static class LogService
    {
        private static readonly string LogFilePath;
        private static readonly object LockObj = new object();
        private const int MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB max
        
        static LogService()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalsaNOWGames");
            Directory.CreateDirectory(appDataPath);
            LogFilePath = Path.Combine(appDataPath, "salsa.log");
            
            // Rotate log if too large
            RotateLogIfNeeded();
        }
        
        public static void Log(string message)
        {
            WriteLog("INFO", message);
        }
        
        public static void LogWarning(string message)
        {
            WriteLog("WARN", message);
        }
        
        public static void LogError(string message)
        {
            WriteLog("ERROR", message);
        }
        
        public static void LogError(string message, Exception ex)
        {
            WriteLog("ERROR", $"{message}: {ex.GetType().Name} - {ex.Message}");
            if (ex.InnerException != null)
            {
                WriteLog("ERROR", $"  Inner: {ex.InnerException.Message}");
            }
        }
        
        public static void LogDebug(string message)
        {
#if DEBUG
            WriteLog("DEBUG", message);
#endif
        }
        
        public static void LogApi(string endpoint, bool success, string details = null)
        {
            string status = success ? "OK" : "FAILED";
            string msg = $"API [{status}] {endpoint}";
            if (!string.IsNullOrEmpty(details))
            {
                msg += $" - {details}";
            }
            WriteLog(success ? "INFO" : "ERROR", msg);
        }
        
        private static void WriteLog(string level, string message)
        {
            try
            {
                lock (LockObj)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logLine = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, logLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Silently fail if logging fails - don't crash the app
            }
        }
        
        private static void RotateLogIfNeeded()
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    var fileInfo = new FileInfo(LogFilePath);
                    if (fileInfo.Length > MaxLogSizeBytes)
                    {
                        string backupPath = LogFilePath + ".old";
                        if (File.Exists(backupPath))
                        {
                            File.Delete(backupPath);
                        }
                        File.Move(LogFilePath, backupPath);
                    }
                }
            }
            catch { }
        }
        
        public static string GetLogFilePath()
        {
            return LogFilePath;
        }
    }
}
