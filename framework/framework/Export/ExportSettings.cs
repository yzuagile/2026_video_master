namespace framework.Export
{
    public enum VideoFormat
    {
        MP4,
        MKV,
        MOV
    }

    public record ExportSettings
    {
        public VideoFormat Format { get; init; }
        public string Bitrate { get; init; } = string.Empty;
        public string SubtitleText { get; init; } = string.Empty;
        public double TrimStartSeconds { get; init; }
        public double TrimEndSeconds { get; init; }
        public double DurationSeconds { get; init; }
        public string OutputPath { get; set; } = string.Empty;
    }
}