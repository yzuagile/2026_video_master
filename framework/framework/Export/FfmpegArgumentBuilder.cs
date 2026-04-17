using System.Collections.Generic;
using System.Globalization;

namespace framework.Export
{
    public static class FfmpegArgumentBuilder
    {
        public static List<string> Build(string inputVideoPath, ExportSettings settings)
        {
            var args = new List<string>
            {
                "-hide_banner",
                "-y",
                "-i",
                inputVideoPath
            };

            if (settings.TrimStartSeconds >= 0 && settings.TrimEndSeconds > settings.TrimStartSeconds)
            {
                args.Add("-ss");
                args.Add(settings.TrimStartSeconds.ToString("F2", CultureInfo.InvariantCulture));
                args.Add("-to");
                args.Add(settings.TrimEndSeconds.ToString("F2", CultureInfo.InvariantCulture));
            }

            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-b:v");
            args.Add(settings.Bitrate + "k");
            args.Add("-preset");
            args.Add("medium");
            args.Add("-c:a");
            args.Add("aac");
            args.Add("-b:a");
            args.Add("128k");

            if (!string.IsNullOrWhiteSpace(settings.SubtitleText))
            {
                args.Add("-vf");
                args.Add(BuildDrawTextFilter(settings.SubtitleText));
            }

            args.Add(settings.OutputPath);
            return args;
        }

        public static string BuildDrawTextFilter(string subtitleText)
        {
            var safeText = subtitleText.Replace("\r\n", " ").Replace("\n", " ").Replace("\"", "\\\"");
            return $"drawtext=font=Arial:text={safeText}:fontcolor=white:fontsize=24:box=1:boxcolor=black@0.5:boxborderw=5:x=(w-text_w)/2:y=h-60";
        }
    }
}