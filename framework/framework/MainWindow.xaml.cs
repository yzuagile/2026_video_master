using framework.Export;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace framework
{
    public partial class MainWindow : Window
    {
        private string currentVideoPath = "";
        private double currentVideoDuration = 0; // 影片持續時間（秒）
        private string pendingSubtitleText = "";
        private double trimStartSeconds = 0;
        private double trimEndSeconds = 0;

        private System.Windows.Threading.DispatcherTimer playheadTimer;
        private System.Windows.Threading.DispatcherTimer autoScrollTimer; // 自動捲動計時器
        private const double PIXELS_PER_SECOND = 20;

        // 記錄目前是否正在拖曳游標
        private bool isDraggingPlayhead = false;

        // 控制時間軸平移
        private TranslateTransform timelineTransform = new TranslateTransform();
        private double timelineOffsetX = 0;

        public MainWindow()
        {
            InitializeComponent();

            InitializePlayheadTimer(); // 初始化定時器
            // 視窗的 KeyDown 事件 (當鍵盤按鍵被按下時觸發)
            this.KeyDown += MainWindow_KeyDown;
            // 點擊軌道空白處取消選取
            VideoTrackCanvas.MouseDown += (s, e) => ClearSelection();

            // 畫布可以接收滑鼠點擊
            TimeRulerCanvas.Background = System.Windows.Media.Brushes.Transparent;
            VideoTrackCanvas.Background = System.Windows.Media.Brushes.Transparent;

            // 時間軸拖曳事件
            TimeRulerCanvas.PreviewMouseLeftButtonDown += Timeline_MouseLeftButtonDown;
            TimeRulerCanvas.PreviewMouseMove += Timeline_MouseMove;
            TimeRulerCanvas.PreviewMouseLeftButtonUp += Timeline_MouseLeftButtonUp;
            VideoTrackCanvas.PreviewMouseLeftButtonDown += Timeline_MouseLeftButtonDown;
            VideoTrackCanvas.PreviewMouseMove += Timeline_MouseMove;
            VideoTrackCanvas.PreviewMouseLeftButtonUp += Timeline_MouseLeftButtonUp;

            // 視窗載入後，綁定平移特效 (已移除滾輪綁定)
            this.Loaded += (s, e) =>
            {
                if (TimelineContentStack != null)
                {
                    // 讓 TimelineContentStack 可以被程式「推動」
                    TimelineContentStack.RenderTransform = timelineTransform;
                }
            };
        }

        private void InitializePlayheadTimer()
        {
            playheadTimer = new System.Windows.Threading.DispatcherTimer();
            // 設定每 30 毫秒更新一次畫面 (大約 33 FPS，看起來比較滑順)
            playheadTimer.Interval = TimeSpan.FromMilliseconds(30);
            playheadTimer.Tick += PlayheadTimer_Tick;

            // 初始化自動捲動計時器
            autoScrollTimer = new System.Windows.Threading.DispatcherTimer();
            autoScrollTimer.Interval = TimeSpan.FromMilliseconds(30);
            autoScrollTimer.Tick += AutoScrollTimer_Tick;
        }

        private void PlayheadTimer_Tick(object sender, EventArgs e)
        {
            // 確保有載入影片且播放器有 NaturalDuration
            if (VideoPlayer.Source != null && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                double currentTime = VideoPlayer.Position.TotalSeconds;
                if (currentTime >= trimEndSeconds)
                {
                    // 強制跳回左手把設定的開始時間
                    VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                    VideoPlayer.Play();
                }
                // 保險起見：如果因為手動拖拉等原因小於左手把
                else if (currentTime < trimStartSeconds)
                {
                    VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                }

                if (!isDraggingPlayhead)
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
        }

        private void PlaybackTimer_Tick(object sender, EventArgs e)
        {   
            if (VideoPlayer.Source != null && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                double currentTime = VideoPlayer.Position.TotalSeconds;

                // 問題 2 的核心修正：
                // 只要當前時間「大於或等於」右手把位置，就立刻彈回左手把
                if (currentTime >= trimEndSeconds)
                {
                    VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                    VideoPlayer.Play(); // 確保它是播放狀態
                }

                // 額外保險：如果使用者手動拉動進度條到左手把之前
                else if (currentTime < trimStartSeconds)
                {
                    VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                }
            }
        }


        private void AutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (isDraggingPlayhead)
            {
                // 邊緣自動平移 (Auto-scroll)
                // 當 user 正在拖曳且滑鼠靠近視窗左右兩側時，讓時間軸自動滾動
                Point mousePosInWindow = Mouse.GetPosition(this);
                bool needsScroll = false;

                // 靠近視窗右側邊緣 100 像素內 (時間軸往左滑)
                if (mousePosInWindow.X > this.ActualWidth - 100)
                {
                    timelineOffsetX -= 15; // 捲動速度
                    needsScroll = true;
                }
                // 靠近視窗左側邊緣 100 像素內 (時間軸往右滑)
                else if (mousePosInWindow.X < 100 && timelineOffsetX < 0)
                {
                    timelineOffsetX += 15;
                    needsScroll = true;
                }

                if (needsScroll)
                {
                    // 確保捲動不超過邊界
                    if (timelineOffsetX > 0) timelineOffsetX = 0;
                    double minOffset = -(TimelineContentStack.Width - this.ActualWidth + 100);
                    if (minOffset > 0) minOffset = 0;
                    if (timelineOffsetX < minOffset) timelineOffsetX = minOffset;

                    // 套用自動平移
                    timelineTransform.X = timelineOffsetX;

                    // 平移後 要依據滑鼠相對於 Canvas 的新位置重新計算紅線
                    Point canvasPos = Mouse.GetPosition(VideoTrackCanvas);
                    UpdatePlayheadPosition(canvasPos.X);
                }
            }
        }

        // 時間軸游標拖曳功能
        private bool wasPlayingBeforeDrag = false; // 紀錄拖曳前是否正在播放
        private int lastScrubTick = 0; // 紀錄上一次更新畫面的時間點
        private void Timeline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (VideoPlayer.Source == null || !VideoPlayer.NaturalDuration.HasTimeSpan) return;

            // 鎖定滑鼠，確保拖曳時即使滑鼠移出畫布外，也能繼續觸發 MouseMove
            if (sender is UIElement element)
            {
                // 取得點擊位置的 X 座標
                double clickX = e.GetPosition(element).X;

                // --- 關鍵修正：範圍限制 (Clamp) ---
                // 假設 1 秒 = 20 像素，計算合法範圍的像素邊界
                double minX = trimStartSeconds * 20;
                double maxX = trimEndSeconds * 20;

                // 如果點擊的位置在框框外，強制將數值拉回邊界
                if (clickX < minX) clickX = minX;
                if (clickX > maxX) clickX = maxX;

                // 判斷邏輯：
                // 1. 如果點在時間刻度區 (TimeRulerCanvas)，隨意點擊都能跳轉並拖曳
                bool isClickingOnRuler = sender == TimeRulerCanvas;

                // 2. 如果點在影像軌道區 (VideoTrackCanvas)，必須點在「紅線附近 (誤差 +- 10 像素)」，才能拖拉游標
                // 注意：這裡的 PlayheadLine.X1 也要在範圍內
                bool isClickingNearPlayhead = Math.Abs(clickX - PlayheadLine.X1) <= 10;

                if (isClickingOnRuler || isClickingNearPlayhead)
                {
                    isDraggingPlayhead = true;
                    element.CaptureMouse(); // 鎖定滑鼠

                    // 取得目前負責更新紅線的計時器狀態 (假設名稱為 playheadTimer)
                    wasPlayingBeforeDrag = playheadTimer.IsEnabled;

                    if (wasPlayingBeforeDrag)
                    {
                        VideoPlayer.Pause();
                        playheadTimer.Stop();
                    }

                    // 開始拖曳時 啟動自動捲動計時器
                    autoScrollTimer.Start();

                    // 使用「限制後」的座標來更新位置，確保紅線不越界
                    UpdatePlayheadPosition(clickX);

                    // 攔截事件 防止點擊穿透到影片區塊引發選取白框的邏輯
                    e.Handled = true;
                }
            }
        }

        private void Timeline_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDraggingPlayhead && sender is UIElement element)
            {
                UpdatePlayheadPosition(e.GetPosition(element).X);
            }
        }

        private void Timeline_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDraggingPlayhead && sender is UIElement element)
            {
                isDraggingPlayhead = false;
                element.ReleaseMouseCapture(); // 釋放滑鼠鎖定
            }

            // 停止拖曳時 關閉自動捲動計時器
            autoScrollTimer.Stop();

            // (若希望放開後恢復播放，可以保留以下這段。若不需要可移除)
            if (wasPlayingBeforeDrag)
            {
                VideoPlayer.Play();
                playheadTimer.Start();
            }
        }

        private void UpdatePlayheadPosition(double mouseX)
        {
            // 防呆：確保獲取到正確的影片總長度
            double duration = currentVideoDuration;
            if (duration <= 0 && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                duration = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                currentVideoDuration = duration;
            }

            if (duration <= 0) return; 

            // --- 關鍵修正：將範圍限制在剪輯區間內 ---
            // 計算左手把與右手把對應的像素位置
            double minX = trimStartSeconds * PIXELS_PER_SECOND;
            double maxX = trimEndSeconds * PIXELS_PER_SECOND;

            // 確保游標被限制在 [左手把, 右手把] 之間
            if (mouseX < minX) mouseX = minX;
            if (mouseX > maxX) mouseX = maxX;

            // 1. 即時更新紅線的視覺位置 (此時 mouseX 已經是被限制過的安全值)
            PlayheadLine.X1 = mouseX;
            PlayheadLine.X2 = mouseX;

            // 2. 計算拖曳到的時間點
            double targetSeconds = mouseX / PIXELS_PER_SECOND;

            // 3. 同步更新影片進度
            // 使用限制後的 targetSeconds，確保影片畫面不會跑出區間
            VideoPlayer.Position = TimeSpan.FromSeconds(targetSeconds);
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

            //初始化預設字卡樣式
            TxtFontSize.Text = "12";
            TxtFontColor.Text = "#FFFFFF"; // 預設白色
            SliderStroke.Value = 0;        // 預設無邊框

            // 設定下拉選單預設選取「新細明體」
            foreach (ComboBoxItem item in ComboFontFamily.Items)
            {
                if (item.Content.ToString() == "新細明體")
                {
                    ComboFontFamily.SelectedItem = item;
                    break;
                }
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
                        currentVideoDuration = duration;

                        // 確保畫布寬度足夠顯示整段影片
                        TimeRulerCanvas.Width = duration * 20 + 100;
                        VideoTrackCanvas.Width = duration * 20 + 100;

                        DrawTimeRuler(duration); // 畫刻度
                        AddVideoToTimeline(duration);

                        PlayheadLine.Visibility = Visibility.Visible; // 顯示紅線
                        PlayheadLine.X1 = 0; // 位置歸零
                        PlayheadLine.X2 = 0; // 位置歸零

                        // 修正：設定紅線長度，使其往下涵蓋多個軌道
                        PlayheadLine.Y1 = 0;
                        PlayheadLine.Y2 = 120;

                        // 匯入新影片時重置平移狀態
                        timelineOffsetX = 0;
                        timelineTransform.X = 0;
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

            trimStartSeconds = 0;
            trimEndSeconds = durationInSeconds;
            TxtStartTime.Text = "0.0";
            TxtEndTime.Text = durationInSeconds.ToString("F1");

            // 設定畫布與容器寬度
            VideoTrackCanvas.Width = totalWidth;
            TimeRulerCanvas.Width = totalWidth;
            TimelineContentStack.Width = totalWidth + 100;

            VideoTrackCanvas.Children.Clear();

            // 1. 建立容器 Grid，用來包裹影片矩形與左右拖動手把
            Grid segmentContainer = new Grid
            {
                Width = totalWidth,
                Height = 35,
                Tag = "VideoSegment"
            };

            // 2. 建立影片主體矩形
            System.Windows.Shapes.Rectangle videoSegment = new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)),
                Stroke = System.Windows.Media.Brushes.Transparent,
                StrokeThickness = 2,
                RadiusX = 3,
                RadiusY = 3
            };

            videoSegment.MouseDown += VideoSegment_MouseDown;
            segmentContainer.Children.Add(videoSegment);

            // 建立左側手把
            Thumb leftHandle = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.SizeWE,
                Background = Brushes.White,
                Opacity = 0
            };
            leftHandle.DragDelta += LeftHandle_DragDelta;
            // 新增：開始拖動時暫停
            leftHandle.DragStarted += (s, e) => VideoPlayer.Pause();
            // 新增：結束拖動時播放
            leftHandle.DragCompleted += (s, e) => VideoPlayer.Play();

            // 建立右側手把
                Thumb rightHandle = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.SizeWE,
                Background = Brushes.White,
                Opacity = 0
            };
            rightHandle.DragDelta += RightHandle_DragDelta;
            // 新增：開始拖動時暫停
            rightHandle.DragStarted += (s, e) => VideoPlayer.Pause();
            // 新增：結束拖動時播放
            rightHandle.DragCompleted += (s, e) => VideoPlayer.Play();


            segmentContainer.Children.Add(leftHandle);
            segmentContainer.Children.Add(rightHandle);

            // 5. 設定在 Canvas 上的位置
            Canvas.SetLeft(segmentContainer, 0); 
            Canvas.SetTop(segmentContainer, 45); // 維持在影像軌高度

            VideoTrackCanvas.Children.Add(segmentContainer);
        }

        // 處理左側拖動 (改變位置 + 改變寬度)
        private void LeftHandle_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement thumb && thumb.Parent is Grid container)
            {
                double currentLeft = Canvas.GetLeft(container);
                double newLeft = currentLeft + e.HorizontalChange;
                double newWidth = container.Width - e.HorizontalChange;

                if (newWidth > 10 && newLeft >= 0)
                {
                    Canvas.SetLeft(container, newLeft);
                    container.Width = newWidth;

                    trimStartSeconds = newLeft / 20; 
                    TxtStartTime.Text = trimStartSeconds.ToString("F1");

                    // 2. 關鍵：如果目前的播放位置比新的起點還早，立刻把影片跳到新起點
                    if (VideoPlayer.Position.TotalSeconds < trimStartSeconds)
                    {
                        VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                    }
                }
            }
        }

        // 處理右側拖動 (僅改變寬度)
        private void RightHandle_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement thumb && thumb.Parent is Grid container)
            {
                double newWidth = container.Width + e.HorizontalChange;
                double currentLeft = Canvas.GetLeft(container);

                if (newWidth > 10)
                {
                    container.Width = newWidth;

                    trimEndSeconds = (currentLeft + newWidth) / 20;
                    TxtEndTime.Text = trimEndSeconds.ToString("F1");

                    // 2. 關鍵：如果目前的播放位置已經超過新的終點，立刻跳回起點
                    if (VideoPlayer.Position.TotalSeconds > trimEndSeconds)
                    {
                        VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                    }
                }
            }
        }

        private void VideoSegment_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ClearSelection();

            // sender 是 Rectangle，它的 Parent 是 Grid
            if (sender is System.Windows.Shapes.Rectangle rect)
            {
                rect.Stroke = System.Windows.Media.Brushes.White;
                rect.StrokeThickness = 2;

                // 讓手把顯示出來（選取時才看得到手把，這很專業）
                if (rect.Parent is Grid parentGrid)
                {
                    foreach (var child in parentGrid.Children)
                    {
                        if (child is System.Windows.Controls.Primitives.Thumb thumb)
                            thumb.Opacity = 0.5; // 半透明灰色手把
                    }
                }
            }
            e.Handled = true;
        }
        private void VideoTrackCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ClearSelection();
        }

        // 辨識並清除字卡(Border)的白框
        private void ClearSelection()
        {
            foreach (var child in VideoTrackCanvas.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var inner in grid.Children)
                    {
                        if (inner is System.Windows.Shapes.Rectangle rect)
                            rect.Stroke = System.Windows.Media.Brushes.Transparent;

                        if (inner is System.Windows.Controls.Primitives.Thumb thumb)
                            thumb.Opacity = 0; // 隱藏手把
                    }
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
                PlayheadLine.Y1 = 0;
                PlayheadLine.Y2 = 120;

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
            if (VideoPlayer.Source == null || !VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                MessageBox.Show("請先匯入影片！", "提示");
                return;
            }
            string textToApply = TxtSubtitle.Text;
            if (string.IsNullOrEmpty(textToApply))
            {
                MessageBox.Show("請先輸入字卡內容！", "提示");
                return;
            }

            // 1. 取得目前游標的時間點與 X 座標
            double currentPosSeconds = VideoPlayer.Position.TotalSeconds;
            double startX = currentPosSeconds * PIXELS_PER_SECOND;

            // 2. 設定字卡長度為 5 秒，並計算在畫布上的寬度
            double durationSeconds = 5.0;
            double cardWidth = durationSeconds * PIXELS_PER_SECOND;

            // 3. 建立字卡的 UI 元素 (使用 Border 方便包裝文字並加上圓角背景)
            Border textCard = new Border
            {
                Width = cardWidth,
                Height = 30, // 高度設定為 30，比影片軌稍微扁一點點
                Background = new SolidColorBrush(Color.FromRgb(70, 80, 100)), // 參考附圖的灰藍色調
                CornerRadius = new CornerRadius(4),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2)
            };

            TextBlock textBlock = new TextBlock
            {
                Text = textToApply,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, // 文字過長時會顯示 "..."
                Padding = new Thickness(5, 0, 5, 0)
            };

            textCard.Child = textBlock;

            // 4. 註冊字卡的點擊事件 (使其可以被選取，點擊後會出現白框)
            textCard.MouseDown += (s, ev) =>
            {
                ClearSelection();
                textCard.BorderBrush = Brushes.White;
                ev.Handled = true; // 避免點擊穿透到背景
            };

            // 5. 設定字卡在 VideoTrackCanvas 中的位置
            Canvas.SetLeft(textCard, startX);

            // 影片軌段預設放在 Top=45，Height=35
            // 將字卡軌設定在 Top=5，就會出現在影像軌的上方
            Canvas.SetTop(textCard, 5);

            // 6. 將字卡加入畫布中
            VideoTrackCanvas.Children.Add(textCard);

            // 原本紀錄文字的邏輯
            StoreSubtitleSettings(textToApply);

            MessageBox.Show($"已記錄字卡內容：「{textToApply}」。\n\n(目前為純記錄，後續將把此參數傳遞給 FFmpeg)", "系統訊息");
            
            TxtSubtitle.Text = "";  // 自動清空輸入框，方便使用者連續輸入下一個字卡
        }        
        
        private void BtnUpdateStyle_Click(object sender, RoutedEventArgs e)
        {
            // 1. 抓取 UI 面板上的設定值
            string fontColor = TxtFontColor.Text;
            string fontSize = TxtFontSize.Text;

            // 安全地取得下拉選單選中的字體名稱
            string fontFamily = (ComboFontFamily.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Microsoft JhengHei";

            // 2. 實作 Task 1：樣式檢查與轉換邏輯
            try
            {
                // 數字檢查：確保字體大小是合法的數字
                if (!double.TryParse(fontSize, out double sizeValue) || sizeValue <= 0)
                {
                    MessageBox.Show("請輸入正確的字體大小數值！", "輸入錯誤");
                    return;
                }

                // 顏色轉換檢查：嘗試將 Hex 碼轉換為 WPF 畫筆
                var converter = new System.Windows.Media.BrushConverter();
                // ... 之前的轉換邏輯 ...
                var brush = (System.Windows.Media.Brush)converter.ConvertFromString(fontColor);

                // --- 新增：套用到畫面的預覽文字 (Task 1 實作細節) ---
                TxtPreview.Visibility = Visibility.Visible;
                TxtPreview.Text = TxtSubtitle.Text; // 顯示輸入的內容
                TxtPreview.FontSize = sizeValue;
                TxtPreview.Foreground = brush;
                TxtPreview.FontFamily = new System.Windows.Media.FontFamily(fontFamily);

                // 處理粗細 (FontWeight)
                string weightStr = (ComboFontWeight.SelectedItem as ComboBoxItem)?.Content?.ToString();
                TxtPreview.FontWeight = (weightStr == "Bold") ? FontWeights.Bold : FontWeights.Normal;

                // 3. 測試反饋 (確認邏輯有跑通)
                MessageBox.Show($"樣式已記錄：\n字體：{fontFamily}\n大小：{sizeValue}\n顏色：{fontColor}\n\n(待完成選取功能後即可套用至物件)", "樣式更新成功");
            }
            catch
            {
                // 如果使用者 Hex 碼亂輸入（例如少一個 # 或長度不對）
                MessageBox.Show("顏色格式輸入錯誤！請使用如 #FFFFFF 的 Hex 格式。", "顏色錯誤");
            }
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
                timelineOffsetX = 0;
                timelineTransform.X = 0;

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