using System;
using System.IO;
using System.Linq;

namespace QuakeServerManager.Services
{
    public class Q3ValidationService
    {
        // Common Q3 executable names to check for
        private static readonly string[] Q3Executables = {
            "quake3.exe",
            "cnq3.exe", 
            "cnq3ded.exe",
            "ioquake3.exe",
            "ioq3ded.exe",
            "q3ded.exe"
        };

        // Common Q3 folder names that might contain the executables
        private static readonly string[] Q3Subfolders = {
            "",
            "bin",
            "bin\\x64",
            "bin\\x86"
        };

        public static bool IsValidQ3Folder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return false;

            // Check for Q3 executables in the main folder and common subfolders
            foreach (var subfolder in Q3Subfolders)
            {
                var fullPath = Path.Combine(folderPath, subfolder);
                if (!Directory.Exists(fullPath)) continue;

                foreach (var executable in Q3Executables)
                {
                    var executablePath = Path.Combine(fullPath, executable);
                    if (File.Exists(executablePath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static string? FindQ3Executable(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return null;

            foreach (var subfolder in Q3Subfolders)
            {
                var fullPath = Path.Combine(folderPath, subfolder);
                if (!Directory.Exists(fullPath)) continue;

                foreach (var executable in Q3Executables)
                {
                    var executablePath = Path.Combine(fullPath, executable);
                    if (File.Exists(executablePath))
                    {
                        return executablePath;
                    }
                }
            }

            return null;
        }

        public static string GetValidationErrorMessage(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return "No Q3 installation path selected.";

            if (!Directory.Exists(folderPath))
                return "The selected folder does not exist.";

            return $"The selected folder does not contain a valid Quake III installation.\n\n" +
                   $"Expected to find one of these files:\n" +
                   $"{string.Join("\n", Q3Executables)}\n\n" +
                   $"Please select a folder containing a valid Quake III installation.";
        }
    }
} 