using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System;
namespace framework.Export
{
    public static class FfmpegArgumentBuilder
    {
        public static List<string> Build(string inputVideoPath, ExportSettings settings, bool hasAudio = true)
        {
            var args = new List<string> { "-hide_banner", "-y", "-i", inputVideoPath };

            bool hasMultipleSegments = settings.Segments != null && settings.Segments.Count > 0;
            string currentVideoLabel = "[0:v]"; // 預設輸入流
            string currentAudioLabel = "[0:a]"; // 預設輸入流

            if (hasMultipleSegments)
            {
                var filterParts = new List<string>();
                int idx = 0;
                double currentTime = 0;
                string concatInputs = "";

                foreach (var seg in settings.Segments)
                {
                    if (seg.TimelineStart > currentTime + 0.05) // 0.05秒容錯
                    {
                        double gapDur = seg.TimelineStart - currentTime;
                        filterParts.Add($"[0:v]trim=start=0:end={gapDur.ToString("F3", CultureInfo.InvariantCulture)},drawbox=color=black:t=fill,setpts=PTS-STARTPTS[v{idx}]");

                        if (hasAudio)
                            filterParts.Add($"[0:a]atrim=start=0:end={gapDur.ToString("F3", CultureInfo.InvariantCulture)},volume=0,asetpts=PTS-STARTPTS[a{idx}]");
                        else
                            filterParts.Add($"anullsrc=channel_layout=stereo:sample_rate=44100,atrim=start=0:end={gapDur.ToString("F3", CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS[a{idx}]");

                        concatInputs += $"[v{idx}][a{idx}]";
                        idx++;
                    }
                    double endOffset = seg.InternalOffset + seg.Duration;
                    filterParts.Add($"[0:v]trim=start={seg.InternalOffset.ToString("F3", CultureInfo.InvariantCulture)}:end={endOffset.ToString("F3", CultureInfo.InvariantCulture)},setpts=PTS-STARTPTS[v{idx}]");
                    filterParts.Add($"[0:a]atrim=start={seg.InternalOffset.ToString("F3", CultureInfo.InvariantCulture)}:end={endOffset.ToString("F3", CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS[a{idx}]");
                    concatInputs += $"[v{idx}][a{idx}]";

                    currentTime = seg.TimelineStart + seg.Duration;
                    idx++;
                }
                if (idx > 0)
                {
                    filterParts.Add($"{concatInputs}concat=n={idx}:v=1:a=1[concatv][concata]");
                    currentVideoLabel = "[concatv]";
                    currentAudioLabel = "[concata]";
                }

                string vf = BuildVideoFilter(settings);
                if (!string.IsNullOrWhiteSpace(vf))
                {
                    filterParts.Add($"{currentVideoLabel}{vf}[finalv]");
                    currentVideoLabel = "[finalv]";
                }

                args.Add("-filter_complex");
                args.Add(string.Join(";", filterParts));
                args.Add("-map");
                args.Add(currentVideoLabel);

                // ⭐ 4. 如果有合併聲音，才需要 map 聲音軌
                args.Add("-map");
                args.Add(currentAudioLabel);
            }
            else
            {
                if (settings.TrimStartSeconds >= 0 && settings.TrimEndSeconds > settings.TrimStartSeconds)
                {
                    args.Insert(2, "-ss");
                    args.Insert(3, settings.TrimStartSeconds.ToString("F2", CultureInfo.InvariantCulture));
                    args.Insert(4, "-to");
                    args.Insert(5, settings.TrimEndSeconds.ToString("F2", CultureInfo.InvariantCulture));
                }

                var videoFilter = BuildVideoFilter(settings);
                if (!string.IsNullOrWhiteSpace(videoFilter))
                {
                    args.Add("-vf");
                    args.Add(videoFilter);
                }
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

            if (hasAudio)
            {
                args.Add("-c:a");
                args.Add(GetAudioCodecName(settings.AudioCodec));
                args.Add("-b:a");
                args.Add(settings.AudioBitrate + "k");

                if (settings.AudioChannels > 0)
                {
                    args.Add("-ac");
                    args.Add(settings.AudioChannels.ToString(CultureInfo.InvariantCulture));
                }
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
                filters.Add(settings.SubtitleText);
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
