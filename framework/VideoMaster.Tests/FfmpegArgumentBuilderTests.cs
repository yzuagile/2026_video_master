using framework.Export;

namespace VideoMaster.Tests;

public class FfmpegArgumentBuilderTests
{
    [Fact]
    public void 建立參數_無裁切或字幕時_包含基礎編碼參數()
    {
        var settings = CreateSettings();

        var args = FfmpegArgumentBuilder.Build("input.mp4", settings);

        Assert.Equal(
            [
                "-hide_banner",
                "-y",
                "-i",
                "input.mp4",
                "-c:v",
                "libx264",
                "-b:v",
                "2000k",
                "-preset",
                "medium",
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "-b:a",
                "128k",
                "-ac",
                "2",
                "output.mp4"
            ],
            args);
    }

    [Fact]
    public void 建立參數_裁切範圍有效時_加入裁切參數()
    {
        var settings = CreateSettings() with
        {
            TrimStartSeconds = 12.345,
            TrimEndSeconds = 48.9
        };

        var args = FfmpegArgumentBuilder.Build("input.mp4", settings);

        Assert.Contains("-ss", args);
        Assert.Contains("12.35", args);
        Assert.Contains("-to", args);
        Assert.Contains("48.90", args);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    public void 建立參數_裁切範圍無效時_略過裁切參數(double trimStart, double trimEnd)
    {
        var settings = CreateSettings() with
        {
            TrimStartSeconds = trimStart,
            TrimEndSeconds = trimEnd
        };

        var args = FfmpegArgumentBuilder.Build("input.mp4", settings);

        Assert.DoesNotContain("-ss", args);
        Assert.DoesNotContain("-to", args);
    }

    [Fact]
    public void 建立參數_有字幕時_加入跳脫後的DrawText濾鏡()
    {
        var settings = CreateSettings() with
        {
            SubtitleText = "hello\r\n\"world\""
        };

        var args = FfmpegArgumentBuilder.Build("input.mp4", settings);
        var filterIndex = args.IndexOf("-vf");

        Assert.NotEqual(-1, filterIndex);
        Assert.Equal("drawtext=font=Arial:text=hello \\\"world\\\":fontcolor=white:fontsize=24:box=1:boxcolor=black@0.5:boxborderw=5:x=(w-text_w)/2:y=h-60", args[filterIndex + 1]);
    }

    [Fact]
    public void 建立參數_指定解析度時_加入Scale濾鏡()
    {
        var settings = CreateSettings() with
        {
            OutputWidth = 1280,
            OutputHeight = 720
        };

        var args = FfmpegArgumentBuilder.Build("input.mp4", settings);
        var filterIndex = args.IndexOf("-vf");

        Assert.NotEqual(-1, filterIndex);
        Assert.Equal("scale=1280:720", args[filterIndex + 1]);
    }

    [Fact]
    public void 建立參數_啟用快速啟動時_加入FastStart參數()
    {
        var settings = CreateSettings() with
        {
            EnableFastStart = true
        };

        var args = FfmpegArgumentBuilder.Build("input.mp4", settings);

        Assert.Contains("-movflags", args);
        Assert.Contains("+faststart", args);
    }

    [Fact]
    public void 建立參數_音訊轉MP3時_使用LibMp3lame()
    {
        var settings = CreateSettings() with
        {
            AudioCodec = AudioCodec.MP3,
            AudioBitrate = "192",
            AudioChannels = 1
        };

        var args = FfmpegArgumentBuilder.Build("input.mp4", settings);

        Assert.Contains("-c:a", args);
        Assert.Contains("libmp3lame", args);
        Assert.Contains("-b:a", args);
        Assert.Contains("192k", args);
        Assert.Contains("-ac", args);
        Assert.Contains("1", args);
    }

    private static ExportSettings CreateSettings()
    {
        return new ExportSettings
        {
            Format = VideoFormat.MP4,
            Bitrate = "2000",
            OutputPath = "output.mp4",
            SubtitleText = string.Empty,
            TrimStartSeconds = 0,
            TrimEndSeconds = 0,
            DurationSeconds = 120
        };
    }
}
