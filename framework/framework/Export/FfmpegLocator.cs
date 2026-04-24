using System;
using System.Collections.Generic;
using System.IO;

namespace framework.Export
{
    public static class FfmpegLocator
    {
        public static string? LocateExecutable(IEnumerable<string>? additionalSearchDirectories = null)
        {
            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "ffmpeg.exe")
            };

            if (additionalSearchDirectories is not null)
            {
                foreach (var directory in additionalSearchDirectories)
                {
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }

                    candidates.Add(Path.Combine(directory, "ffmpeg.exe"));
                }
            }

            var environmentPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                candidates.Add(environmentPath);
                candidates.Add(Path.Combine(environmentPath, "ffmpeg.exe"));
            }

            candidates.AddRange(GetCommonFfmpegLocations());

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                    // 忽略存取錯誤，繼續尋找其他候選路徑
                }
            }

            return SearchPathEnvironment("ffmpeg.exe");
        }

        private static string? SearchPathEnvironment(string executableName)
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return null;
            }

            foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedEntry = pathEntry.Trim();
                if (string.IsNullOrEmpty(trimmedEntry))
                {
                    continue;
                }

                try
                {
                    var candidate = Path.Combine(trimmedEntry, executableName);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                    // 忽略不合法路徑或存取問題
                }
            }

            return null;
        }

        private static IEnumerable<string> GetCommonFfmpegLocations()
        {
            var directories = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            foreach (var root in directories)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                yield return Path.Combine(root, "ffmpeg.exe");
                yield return Path.Combine(root, "ffmpeg", "bin", "ffmpeg.exe");
                yield return Path.Combine(root, "FFmpeg", "bin", "ffmpeg.exe");
            }

            yield return Path.Combine("C:\\", "ffmpeg.exe");
            yield return Path.Combine("C:\\", "ffmpeg", "bin", "ffmpeg.exe");
            yield return Path.Combine("C:\\", "Program Files", "ffmpeg", "bin", "ffmpeg.exe");
            yield return Path.Combine("C:\\", "Program Files (x86)", "ffmpeg", "bin", "ffmpeg.exe");
        }
    }
}
