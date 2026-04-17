using Microsoft.Win32; // 為了使用 OpenFileDialog
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace framework // ⚠️注意：如果你的專案名稱不同，請把這裡改成你的專案名稱
{
    // 定義支援的影片格式

    public partial class MainWindow : Window
    {
        private string currentVideoPath = "";
        private double currentVideoDuration = 0; // 影片持續時間（秒）
        private string pendingSubtitleText = "";
        private double trimStartSeconds = 0;
        private double trimEndSeconds = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ================= 工具列功能 =================

        // 按鈕：匯入影片
        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            // 限定只能選擇常見的影片格式
            openFileDialog.Filter = "影片檔案|*.mp4;*.mov;*.avi|所有檔案|*.*";

            if (openFileDialog.ShowDialog() == true)
            {
                currentVideoPath = openFileDialog.FileName;
                VideoPlayer.Source = new Uri(currentVideoPath);
                VideoPlayer.MediaOpened += VideoPlayer_MediaOpened;
                VideoPlayer.Play(); // 讀取後自動播放
            }
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                currentVideoDuration = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            }
        }

        // 按鈕：輸出影片 (未來串接 FFmpeg 的入口)
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentVideoPath))
            {
                MessageBox.Show("請先匯入影片！", "錯誤");
                return;
            }

            try
            {
                // 初始化並顯示彈跳視窗
                ExportWindow exportWin = new ExportWindow(currentVideoDuration);
                exportWin.Owner = this; // 讓視窗居中於主視窗

                if (exportWin.ShowDialog() == true)
                {
                    // 當使用者點擊「開始匯出」
                    var format = exportWin.SelectedFormat;
                    var bitrate = exportWin.FinalBitrate;
                    var exportSettings = CreateExportSettings(format, bitrate);

                    if (!AskSaveExportPath(exportSettings))
                    {
                        return;
                    }

                    if (ExecuteExport(exportSettings))
                    {
                        MessageBox.Show($"匯出完成：{exportSettings.OutputPath}", "完成");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出視窗開啟失敗：{ex.Message}\n{ex.StackTrace}", "錯誤");
            }
        }

        // ================= 播放器控制 =================

        // 按鈕：播放
        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Play();
        }

        // 按鈕：暫停
        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Pause();
        }

        // ================= 分頁功能：字卡設定 =================

        // 按鈕：套用字卡
        private void BtnAddText_Click(object sender, RoutedEventArgs e)
        {
            string textToApply = TxtSubtitle.Text;
            if (string.IsNullOrEmpty(textToApply))
            {
                MessageBox.Show("請先輸入字卡內容！", "提示");
                return;
            }

            StoreSubtitleSettings(textToApply);

            MessageBox.Show($"已記錄字卡內容：「{textToApply}」。\n\n(目前為純記錄，後續將把此參數傳遞給 FFmpeg)", "系統訊息");
        }

        // ================= 分頁功能：影像剪輯 =================

        private void StoreSubtitleSettings(string text)
        {
            pendingSubtitleText = text;
            // TODO: 未來可在這裡封裝字幕參數，並傳遞給輸出流程
        }

        private void StoreTrimSettings(double startSeconds, double endSeconds)
        {
            trimStartSeconds = startSeconds;
            trimEndSeconds = endSeconds;
            // TODO: 未來可在這裡驗證並記錄剪輯參數，讓 ExportWindow 使用
        }

        private ExportSettings CreateExportSettings(VideoFormat format, string bitrate)
        {
            // TODO: 擴充更多輸出選項，例如分辨率、編碼器、音訊設定
            return new ExportSettings
            {
                Format = format,
                Bitrate = bitrate,
                SubtitleText = pendingSubtitleText,
                TrimStartSeconds = trimStartSeconds,
                TrimEndSeconds = trimEndSeconds,
                DurationSeconds = currentVideoDuration
            };
        }

        private bool AskSaveExportPath(ExportSettings settings)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "MP4 檔案 (*.mp4)|*.mp4|MKV 檔案 (*.mkv)|*.mkv|MOV 檔案 (*.mov)|*.mov";
            saveFileDialog.FileName = Path.GetFileNameWithoutExtension(currentVideoPath) + "." + settings.Format.ToString().ToLower();
            saveFileDialog.DefaultExt = settings.Format.ToString().ToLower();
            saveFileDialog.AddExtension = true;
            saveFileDialog.InitialDirectory = Path.GetDirectoryName(currentVideoPath);

            if (saveFileDialog.ShowDialog() == true)
            {
                settings.OutputPath = saveFileDialog.FileName;
                return true;
            }

            return false;
        }

        private bool ExecuteExport(ExportSettings settings)
        {
            if (string.IsNullOrEmpty(settings.OutputPath))
            {
                MessageBox.Show("未設定輸出檔案路徑。", "錯誤");
                return false;
            }

            var ffmpegPath = "ffmpeg";
            var args = new List<string>
            {
                "-hide_banner",
                "-y",
                "-i",
                currentVideoPath
            };

            if (settings.TrimStartSeconds >= 0 && settings.TrimEndSeconds > settings.TrimStartSeconds)
            {
                args.Add("-ss");
                args.Add(settings.TrimStartSeconds.ToString("F2"));
                args.Add("-to");
                args.Add(settings.TrimEndSeconds.ToString("F2"));
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
                var safeText = settings.SubtitleText.Replace("\r\n", " ").Replace("\n", " ").Replace("\"", "\\\"");
                args.Add("-vf");
                args.Add($"drawtext=font=Arial:text={safeText}:fontcolor=white:fontsize=24:box=1:boxcolor=black@0.5:boxborderw=5:x=(w-text_w)/2:y=h-60");
            }

            args.Add(settings.OutputPath);

            try
            {
                using var process = new Process();
                process.StartInfo.FileName = ffmpegPath;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                foreach (var arg in args)
                {
                    process.StartInfo.ArgumentList.Add(arg);
                }

                process.Start();
                string stdOut = process.StandardOutput.ReadToEnd();
                string stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    MessageBox.Show($"FFmpeg 執行失敗：\n{stdErr}", "錯誤");
                    return false;
                }

                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show("找不到 FFmpeg 執行檔，請確保已安裝 FFmpeg 並可在系統路徑中呼叫。", "錯誤");
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出失敗：{ex.Message}\n{ex.StackTrace}", "錯誤");
                return false;
            }
        }

        private record ExportSettings
        {
            public VideoFormat Format { get; init; }
            public string Bitrate { get; init; } = string.Empty;
            public string SubtitleText { get; init; } = string.Empty;
            public double TrimStartSeconds { get; init; }
            public double TrimEndSeconds { get; init; }
            public double DurationSeconds { get; init; }
            public string OutputPath { get; set; } = string.Empty;
        }

        // 按鈕：設定剪輯標記
        private void BtnTrim_Click(object sender, RoutedEventArgs e)
        {
            // 抓取使用者輸入的開始與結束時間
            if (!double.TryParse(TxtStartTime.Text, out double startTime))
            {
                MessageBox.Show("請輸入有效的開始時間（秒）！", "錯誤");
                return;
            }
            if (!double.TryParse(TxtEndTime.Text, out double endTime))
            {
                MessageBox.Show("請輸入有效的結束時間（秒）！", "錯誤");
                return;
            }
            if (startTime < 0 || endTime <= startTime)
            {
                MessageBox.Show("結束時間必須大於開始時間，且不得為負值。", "錯誤");
                return;
            }

            StoreTrimSettings(startTime, endTime);

            MessageBox.Show($"已記錄剪輯指令：\n保留從第 {startTime} 秒 到 第 {endTime} 秒的片段。\n\n(後續輸出時會將此參數交給 FFmpeg 進行裁切)", "剪輯設定");
        }
    }
}