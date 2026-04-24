using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
            args.Add(GetVideoCodecName(settings.VideoCodec));

            if (settings.UseCrf)
            {
                args.Add("-crf");
                args.Add(settings.Crf.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                args.Add("-b:v");
                args.Add(settings.Bitrate + "k");
            }

            args.Add("-preset");
            args.Add(string.IsNullOrWhiteSpace(settings.Preset) ? "medium" : settings.Preset);

            if (settings.VideoCodec == VideoCodec.H264 || settings.VideoCodec == VideoCodec.H265)
            {
                args.Add("-pix_fmt");
                args.Add("yuv420p");
            }

            var videoFilter = BuildVideoFilter(settings);
            if (!string.IsNullOrWhiteSpace(videoFilter))
            {
                args.Add("-vf");
                args.Add(videoFilter);
            }

            args.Add("-c:a");
            args.Add(GetAudioCodecName(settings.AudioCodec));
            args.Add("-b:a");
            args.Add(settings.AudioBitrate + "k");

            if (settings.AudioChannels > 0)
            {
                args.Add("-ac");
                args.Add(settings.AudioChannels.ToString(CultureInfo.InvariantCulture));
            }

            if (settings.EnableFastStart && settings.Format == VideoFormat.MP4)
            {
                args.Add("-movflags");
                args.Add("+faststart");
            }

            args.Add(settings.OutputPath);
            return args;
        }

        private static string GetVideoCodecName(VideoCodec codec)
        {
            return codec switch
            {
                VideoCodec.H265 => "libx265",
                VideoCodec.VP9 => "libvpx-vp9",
                _ => "libx264",
            };
        }

        private static string GetAudioCodecName(AudioCodec codec)
        {
            return codec switch
            {
                AudioCodec.MP3 => "libmp3lame",
                _ => "aac",
            };
        }

        private static string BuildVideoFilter(ExportSettings settings)
        {
            var filters = new List<string>();

            if (settings.OutputWidth > 0 || settings.OutputHeight > 0)
            {
                filters.Add(BuildScaleFilter(settings.OutputWidth, settings.OutputHeight));
            }

            if (!string.IsNullOrWhiteSpace(settings.SubtitleText))
            {
                filters.Add(BuildDrawTextFilter(settings.SubtitleText));
            }

            return string.Join(",", filters.Where(f => !string.IsNullOrWhiteSpace(f)));
        }

        private static string BuildScaleFilter(int width, int height)
        {
            if (width > 0 && height > 0)
            {
                return $"scale={width}:{height}";
            }

            if (width > 0)
            {
                return $"scale={width}:-2";
            }

            if (height > 0)
            {
                return $"scale=-2:{height}";
            }

            return string.Empty;
        }

        public static string BuildDrawTextFilter(string subtitleText)
        {
            var safeText = subtitleText.Replace("\r\n", " ").Replace("\n", " ").Replace("\"", "\\\"");
            return $"drawtext=font=Arial:text={safeText}:fontcolor=white:fontsize=24:box=1:boxcolor=black@0.5:boxborderw=5:x=(w-text_w)/2:y=h-60";
        }
    }
}
