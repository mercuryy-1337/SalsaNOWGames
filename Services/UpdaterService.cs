using System;
using System.Diagnostics;
using System.IO;

namespace SalsaNOWGames.Services
{
    public class UpdaterService
    {
        /*
         * Performs a seamless update: waits for current process exit, overwrites exe, relaunches.
         * Implemented via a temporary batch script to avoid file lock issues.
         */
        public bool LaunchSeamlessUpdate(string downloadedExePath)
        {
            try
            {
                string currentExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                // Target file name is the current executable (overwrite in place)
                string targetExe = currentExe;

                // Build batch script
                string scriptPath = Path.Combine(Path.GetTempPath(), $"sls_update_{Guid.NewGuid():N}.bat");
                string script = $"@echo off\r\n" +
                                "setlocal enableextensions\r\n" +
                                $"echo SalsaNOWGames updater starting...\r\n" +
                                $"set TARGET=\"{targetExe}\"\r\n" +
                                $"set NEW=\"{downloadedExePath}\"\r\n" +
                                ":WAITLOOP\r\n" +
                                "rem Wait until original process terminates\r\n" +
                                $"tasklist /FI \"IMAGENAME eq {Path.GetFileName(targetExe)}\" | find /I \"{Path.GetFileName(targetExe)}\" >NUL\r\n" +
                                "if %ERRORLEVEL%==0 (\r\n" +
                                "  ping -n 2 127.0.0.1 >NUL\r\n" +
                                "  goto WAITLOOP\r\n" +
                                ")\r\n" +
                                "echo Original process exited. Overwriting...\r\n" +
                                "copy /Y %NEW% %TARGET% >NUL\r\n" +
                                "echo Launching updated application...\r\n" +
                                "start \"\" %TARGET%\r\n" +
                                "del %NEW% >NUL 2>&1\r\n" +
                                "del %~f0 >NUL 2>&1\r\n";

                File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C \"{scriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}