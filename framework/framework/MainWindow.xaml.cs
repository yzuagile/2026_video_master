using Microsoft.Win32; // 為了使用 OpenFileDialog
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using framework.Export;

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
        // 新增這兩個變數
        private System.Windows.Threading.DispatcherTimer playheadTimer;
        private const double PIXELS_PER_SECOND = 20; // 必須跟你之前畫刻度時的比例一樣！
        public MainWindow()
        {
            InitializeComponent();
            InitializePlayheadTimer(); // 初始化定時器
            // 註冊視窗的 KeyDown 事件 (當鍵盤按鍵被按下時觸發)
            this.KeyDown += MainWindow_KeyDown;
            // 新增這行：點擊軌道空白處取消選取
            VideoTrackCanvas.MouseDown += (s, e) => ClearSelection();
        }
        private void InitializePlayheadTimer()
        {
            playheadTimer = new System.Windows.Threading.DispatcherTimer();
            // 設定每 30 毫秒更新一次畫面 (大約 33 FPS，看起來比較滑順)
            playheadTimer.Interval = TimeSpan.FromMilliseconds(30);
            playheadTimer.Tick += PlayheadTimer_Tick;
        }
        private void PlayheadTimer_Tick(object sender, EventArgs e)
        {
            // 確保有載入影片且播放器有 NaturalDuration
            if (VideoPlayer.Source != null && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                // 取得目前的播放時間
                double currentPositionSeconds = VideoPlayer.Position.TotalSeconds;

                // 計算在畫布上的 X 座標：時間 (秒) * 每一秒代表的像素
                double xPosition = currentPositionSeconds * PIXELS_PER_SECOND;

                // 更新紅線的位置
                PlayheadLine.X1 = xPosition;
                PlayheadLine.X2 = xPosition;
            }
        }
        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // 1. 檢查按下的按鍵是否為 Delete 鍵
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                // 2. 防呆機制：檢查目前的「焦點」是否在輸入框 (TextBox) 上
                // 如果使用者正在打字，我們就不要觸發影片刪除功能
                if (System.Windows.Input.Keyboard.FocusedElement is TextBox)
                {
                    return; // 直接跳出，什麼都不做，讓 TextBox 自己處理字元的刪除
                }

                // 3. 如果不是在打字，就呼叫我們之前寫好的刪除按鈕邏輯
                // 這裡傳入 null 也可以，因為我們在 BtnDelete_Click 裡沒有實際用到 sender 和 e
                BtnDelete_Click(this, new RoutedEventArgs());

                // 告訴系統這個按鍵事件已經處理完畢了，不用再往下傳遞
                e.Handled = true;
            }
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

                // 註冊 MediaOpened 事件，確保在影片資訊載入後才執行繪製
                VideoPlayer.MediaOpened += (s, ev) =>
                {
                    if (VideoPlayer.NaturalDuration.HasTimeSpan)
                    {
                        double duration = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;

                        // 確保畫布寬度足夠顯示整段影片
                        TimeRulerCanvas.Width = duration * 20 + 100;
                        VideoTrackCanvas.Width = duration * 20 + 100;

                        DrawTimeRuler(duration); // 畫刻度
                        AddVideoToTimeline(duration);
                        PlayheadLine.Visibility = Visibility.Visible; // 顯示紅線
                        PlayheadLine.X1 = 0; // 位置歸零
                        PlayheadLine.X2 = 0; // 位置歸零
                    }
                };

                VideoPlayer.Play(); // 讀取後自動播放
                //確保有啟動計時器
                if (playheadTimer != null)
                {
                    playheadTimer.Start();
                }
            }
        }

        private void AddVideoToTimeline(double durationInSeconds)
        {
            double pixelPerSecond = 20;
            double totalWidth = durationInSeconds * pixelPerSecond;

            // 關鍵修正 1：主動設定 Canvas 的寬度，ScrollViewer 才會出現捲軸
            VideoTrackCanvas.Width = totalWidth;
            TimeRulerCanvas.Width = totalWidth;
            TimelineContentStack.Width = totalWidth + 100; // 留一點右側空白

            // 關鍵修正 2：確保 Canvas 內的物件從 0 開始
            VideoTrackCanvas.Children.Clear();

            System.Windows.Shapes.Rectangle videoSegment = new System.Windows.Shapes.Rectangle
            {
                Width = totalWidth,
                Height = 35,
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)),
                Stroke = System.Windows.Media.Brushes.Transparent, 
                StrokeThickness = 2,
                RadiusX = 3,
                RadiusY = 3
            };

            videoSegment.MouseDown += VideoSegment_MouseDown;

            Canvas.SetLeft(videoSegment, 0); // 確保對齊標尺 0 刻度
            Canvas.SetTop(videoSegment, 2);
            VideoTrackCanvas.Children.Add(videoSegment);
        }

        private void VideoSegment_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 先取消所有人的選取
            ClearSelection();

            if (sender is System.Windows.Shapes.Rectangle rect)
            {
                rect.Stroke = System.Windows.Media.Brushes.White; // 變白框
                rect.StrokeThickness = 2;
            }

            // 關鍵：標記事件已處理，不讓滑鼠點擊事件「穿透」到下層的 VideoTrackCanvas
            e.Handled = true;
        }

        private void VideoTrackCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ClearSelection();
        }

        private void ClearSelection()
        {
            foreach (var child in VideoTrackCanvas.Children)
            {
                if (child is System.Windows.Shapes.Rectangle rect)
                {
                    rect.Stroke = System.Windows.Media.Brushes.Transparent;
                }
            }           
        }

        private void DrawTimeRuler(double totalSeconds)
        {
            TimeRulerCanvas.Children.Clear();
            double pixelPerSecond = 20; // 必須與 AddVideoToTimeline 的比例一致
            double majorTickInterval = 5; // 每 5 秒一個大刻度（帶數字）
            double minorTickInterval = 1; // 每 1 秒一個小刻度

            // 根據影片總長度或固定寬度繪製（例如繪製到 2000 像素寬）
            for (double s = 0; s * pixelPerSecond < TimeRulerCanvas.ActualWidth || s <= totalSeconds; s += minorTickInterval)
            {
                double x = s * pixelPerSecond;
                bool isMajor = s % majorTickInterval == 0;

                // 建立刻度線
                System.Windows.Shapes.Line tick = new System.Windows.Shapes.Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = isMajor ? 5 : 15, // 大刻度比較長
                    Y2 = 25,
                    Stroke = System.Windows.Media.Brushes.Gray,
                    StrokeThickness = 1
                };
                TimeRulerCanvas.Children.Add(tick);

                // 如果是大刻度，加上時間文字
                if (isMajor)
                {
                    TextBlock timeText = new TextBlock
                    {
                        Text = $"{s}s",
                        Foreground = System.Windows.Media.Brushes.LightGray,
                        FontSize = 10,
                        Margin = new Thickness(x + 2, 0, 0, 0)
                    };
                    TimeRulerCanvas.Children.Add(timeText);
                }
            }
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                currentVideoDuration = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                // 影片載入成功後，顯示紅線，並將位置歸零
                PlayheadLine.Visibility = Visibility.Visible;
                PlayheadLine.X1 = 0;
                PlayheadLine.X2 = 0;

                // 如果你設定載入後會自動播放，記得也要啟動 Timer
                playheadTimer.Start();
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
                    var exportSettings = CreateExportSettings(format, bitrate, exportWin);

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
            playheadTimer.Start(); // 啟動定時器，開始更新紅線
        }

        // 按鈕：暫停
        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Pause();
            playheadTimer.Stop(); // 暫停定時器
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
        // 按鈕：刪除選取的影像片段
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            // 1. 邏輯檢查：確認是否有匯入影片
            if (string.IsNullOrEmpty(currentVideoPath))
            {
                MessageBox.Show("目前沒有載入任何影片片段。", "提示");
                return;
            }

            // 2. 執行刪除動作 (目前你們的設計是單一音軌，這裡示範清空時間軸)
            var result = MessageBox.Show("確定要從時間軸移除此影片嗎？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // 清空時間軸畫布
                VideoTrackCanvas.Children.Clear();

                // 重設相關參數
                currentVideoPath = "";
                currentVideoDuration = 0;

                // 停止播放器並清除來源
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
                // 隱藏紅線並停止更新
                playheadTimer.Stop();
                PlayheadLine.Visibility = Visibility.Collapsed;
                MessageBox.Show("已成功從時間軸移除片段。", "系統訊息");
            }
        }
        private void StoreTrimSettings(double startSeconds, double endSeconds)
        {
            trimStartSeconds = startSeconds;
            trimEndSeconds = endSeconds;
            // TODO: 未來可在這裡驗證並記錄剪輯參數，讓 ExportWindow 使用
        }

        private ExportSettings CreateExportSettings(VideoFormat format, string bitrate, ExportWindow exportWindow)
        {
            return new ExportSettings
            {
                Format = format,
                Bitrate = bitrate,
                VideoCodec = exportWindow.SelectedVideoCodec,
                AudioCodec = exportWindow.SelectedAudioCodec,
                OutputWidth = exportWindow.OutputWidth,
                OutputHeight = exportWindow.OutputHeight,
                EnableFastStart = exportWindow.EnableFastStart,
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

        private string? FindFfmpegExecutable()
        {
            return FfmpegLocator.LocateExecutable();
        }

        private bool ExecuteExport(ExportSettings settings)
        {
            if (string.IsNullOrEmpty(settings.OutputPath))
            {
                MessageBox.Show("未設定輸出檔案路徑。", "錯誤");
                return false;
            }

            var ffmpegPath = FindFfmpegExecutable();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                MessageBox.Show("找不到 FFmpeg 執行檔。請安裝 FFmpeg，或將 ffmpeg.exe 放在應用程式執行目錄中。", "錯誤");
                return false;
            }

            var args = FfmpegArgumentBuilder.Build(currentVideoPath, settings);

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
            catch (Win32Exception)
            {
                MessageBox.Show("找不到 FFmpeg 執行檔，請安裝 FFmpeg 或將 ffmpeg.exe 放在應用程式執行目錄中。", "錯誤");
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出失敗：{ex.Message}\n{ex.StackTrace}", "錯誤");
                return false;
            }
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