using System;
using System.Collections.Generic;
using System.IO;

namespace framework.Export
{
    public static class FfmpegLocator
    {
        public static string? LocateExecutable(IEnumerable<string>? additionalSearchDirectories = null)
        {
            var candidates = new List<string>();

            // 優先級 1: 應用程式本地目錄
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"));

            // 優先級 2: FFMPEG_PATH 環境變數
            var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            if (!string.IsNullOrWhiteSpace(ffmpegPath))
            {
                candidates.AddRange(NormalizeSearchPath(ffmpegPath));
            }

            // 優先級 3: 使用者提供的額外搜索目錄
            if (additionalSearchDirectories is not null)
            {
                foreach (var directory in additionalSearchDirectories)
                {
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        candidates.Add(Path.Combine(directory, "ffmpeg.exe"));
                    }
                }
            }

            // 優先級 4: 常見位置
            candidates.AddRange(GetCommonFfmpegLocations());

            // 優先級 5: PATH 環境變數
            var pathResult = SearchPathEnvironment("ffmpeg.exe");
            if (pathResult is not null)
            {
                return pathResult;
            }

            // 在候選項中搜尋
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    var normalizedPath = Path.GetFullPath(candidate);
                    if (File.Exists(normalizedPath))
                    {
                        return normalizedPath;
                    }
                }
                catch
                {
                    // 忽略存取錯誤，繼續尋找其他候選路徑
                }
            }

            return null;
        }

    /// <summary>
    /// 將搜索路徑標準化，處理既是檔案也可能是目錄的情況
    /// </summary>
    private static List<string> NormalizeSearchPath(string path)
    {
        var result = new List<string>();
        
        try
        {
            var normalizedPath = Path.GetFullPath(path);
            
            // 如果路徑以 .exe 結尾，直接視為檔案
            if (normalizedPath.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(normalizedPath);
            }
            else
            {
                // 否則視為目錄
                result.Add(Path.Combine(normalizedPath, "ffmpeg.exe"));
            }
        }
        catch
        {
            // 路徑無效，忽略
        }

        return result;
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
            };

            foreach (var root in directories)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                // ffmpeg 通常安裝在 bin 子目錄中
                yield return Path.Combine(root, "ffmpeg", "bin", "ffmpeg.exe");
                // 某些安裝可能直接在根目錄
                yield return Path.Combine(root, "ffmpeg.exe");
            }
        }
    }
}
