namespace framework.Export
{
    public enum VideoFormat
    {
        MP4,
        MKV,
        MOV
    }

    public enum VideoCodec
    {
        H264,
        H265,
        VP9
    }

    public enum AudioCodec
    {
        AAC,
        MP3
    }

    public record ExportSettings
    {
        public VideoFormat Format { get; init; }
        public string Bitrate { get; init; } = string.Empty;
        public VideoCodec VideoCodec { get; init; } = VideoCodec.H264;
        public bool UseCrf { get; init; }
        public int Crf { get; init; } = 23;
        public string Preset { get; init; } = "medium";
        public int OutputWidth { get; init; }
        public int OutputHeight { get; init; }
        public AudioCodec AudioCodec { get; init; } = AudioCodec.AAC;
        public string AudioBitrate { get; init; } = "128";
        public int AudioChannels { get; init; } = 2;
        public bool EnableFastStart { get; init; }
        public string SubtitleText { get; init; } = string.Empty;
        public double TrimStartSeconds { get; init; }
        public double TrimEndSeconds { get; init; }
        public double DurationSeconds { get; init; }
        public string OutputPath { get; set; } = string.Empty;
    }
}
