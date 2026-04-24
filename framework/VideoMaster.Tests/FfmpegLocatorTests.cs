using System;
using System.IO;
using framework.Export;

namespace VideoMaster.Tests;

public class FfmpegLocatorTests
{
    [Fact]
    public void LocateExecutable_UsesAdditionalSearchDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var ffmpegPath = Path.Combine(tempDirectory, "ffmpeg.exe");
            File.WriteAllText(ffmpegPath, "dummy");

            var result = FfmpegLocator.LocateExecutable(new[] { tempDirectory });

            Assert.Equal(Path.GetFullPath(ffmpegPath), result);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void LocateExecutable_SearchesEnvironmentPath()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var ffmpegPath = Path.Combine(tempDirectory, "ffmpeg.exe");
            File.WriteAllText(ffmpegPath, "dummy");

            var originalPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", tempDirectory + Path.PathSeparator + originalPath);

            try
            {
                var result = FfmpegLocator.LocateExecutable();

                Assert.Equal(Path.GetFullPath(ffmpegPath), result);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
            }
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }
}
