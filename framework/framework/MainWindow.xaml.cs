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

        // 藍色框框整體拖移
        private bool isDraggingSegment = false;
        private double segmentDragStartMouseX = 0;
        private double segmentDragStartLeft = 0;
        private double segmentDragTrimDuration = 0; // 拖移前的框框寬度（秒），保持不變
        private bool isDraggingTextSegment = false;
        private double textDragStartMouseX = 0;
        private double textDragStartLeft = 0;

        private Point segmentDragStartPoint;
        private double segmentStartLeft;

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

                if (!isDraggingSegment)
                {
                    if (currentTime >= trimEndSeconds)
                    {
                        // 播到 trimEnd：停止並跳回 trimStart
                        VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                    }
                    else if (currentTime < trimStartSeconds)
                    {
                        // 紅線在框框左側：跳到 trimStart 開始播
                        VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                    }
                }

                if (!isDraggingPlayhead)
                {
                    double currentPositionSeconds = VideoPlayer.Position.TotalSeconds;
                    double xPosition = currentPositionSeconds * PIXELS_PER_SECOND;
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

                if (!isDraggingSegment && currentTime >= trimEndSeconds)
                {
                    VideoPlayer.Stop();
                    VideoPlayer.Position = TimeSpan.FromSeconds(0);
                    playheadTimer.Stop();
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

                // 判斷邏輯：
                // 1. 如果點在時間刻度區 (TimeRulerCanvas)，隨意點擊都能跳轉並拖曳
                bool isClickingOnRuler = sender == TimeRulerCanvas;

                // 2. 如果點在影像軌道區 (VideoTrackCanvas)，必須點在「紅線附近 (誤差 +- 10 像素)」，才能拖拉游標
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

            // ==========================================
            // ⭐ 新增防護邏輯：計算邊界並限制滑鼠座標
            // ==========================================
            // 計算影片總長度對應的像素寬度 (最大允許的 X 座標)
            double maxMouseX = duration * PIXELS_PER_SECOND;

            // 將滑鼠 X 座標強制限制在 0 到 maxMouseX 之間
            // 如果 mouseX 小於 0，safeMouseX 會等於 0；如果超過 maxMouseX，就會停在 maxMouseX
            double safeMouseX = Math.Max(0, Math.Min(mouseX, maxMouseX));
            // ==========================================

            // 1. 即時更新紅線的視覺位置 (改成使用 safeMouseX)
            PlayheadLine.X1 = safeMouseX;
            PlayheadLine.X2 = safeMouseX;

            // 2. 計算拖曳到的時間點 (改成使用 safeMouseX)
            double targetSeconds = safeMouseX / PIXELS_PER_SECOND;

            // 3. 同步更新影片進度
            VideoPlayer.Position = TimeSpan.FromSeconds(targetSeconds);
        }
        private void TimelineScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // 確保觸發事件的元件是我們的 ScrollViewer
            if (sender is ScrollViewer scrollViewer)
            {
                // 定義捲動速度的靈敏度 (可以根據團隊操作手感調整這個數值，通常 0.5~1 之間)
                double scrollSensitivity = 0.8;

                // 滾輪往上滾 (e.Delta > 0) 時，畫面往左移；往下滾 (e.Delta < 0) 時，畫面往右移
                // 計算新的水平偏移量
                double newOffset = scrollViewer.HorizontalOffset - (e.Delta * scrollSensitivity);

                // 防護網：限制畫面不能捲出左邊界 (0)，也不能超過右邊界 (ScrollableWidth)
                newOffset = Math.Max(0, Math.Min(newOffset, scrollViewer.ScrollableWidth));

                // 執行水平捲動
                scrollViewer.ScrollToHorizontalOffset(newOffset);

                // 非常重要的一行：告訴 WPF「這個滾輪動作我已經處理完了」，
                // 這樣它就不會再繼續執行預設的上下垂直捲動了！
                e.Handled = true;
            }
            // 如果你想判斷有沒有按著 Ctrl 鍵 (例如未來想做 Ctrl+滾輪 = 時間軸放大縮小)
            // 想擴充功能時請使用這行程式碼
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                // 執行放大縮小 (Zoom) 邏輯...
                e.Handled = true;
                return;
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
            double totalWidth = durationInSeconds * PIXELS_PER_SECOND;

            trimStartSeconds = 0;
            trimEndSeconds = durationInSeconds;
            segmentDragTrimDuration = durationInSeconds;
            TxtStartTime.Text = "0.0";
            TxtEndTime.Text = durationInSeconds.ToString("F1");

            VideoTrackCanvas.Width = totalWidth;
            TimeRulerCanvas.Width = totalWidth;
            TimelineContentStack.Width = totalWidth + 100;

            VideoTrackCanvas.Children.Clear();

            // ── 1. 灰色底層：代表完整影片，固定不動 ──
            System.Windows.Shapes.Rectangle fullVideoBar = new System.Windows.Shapes.Rectangle
            {
                Width = totalWidth,
                Height = 35,
                Fill = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                RadiusX = 3,
                RadiusY = 3,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(fullVideoBar, 0);
            Canvas.SetTop(fullVideoBar, 45);
            VideoTrackCanvas.Children.Add(fullVideoBar);

            // ── 2. 藍色框框容器：輸出範圍標記，可縮短 / 移動，隨時可還原 ──
            Grid segmentContainer = new Grid
            {
                Width = totalWidth,
                Height = 35,
                Tag = "VideoSegment",
                Cursor = Cursors.SizeAll
            };

            // 藍色主體矩形
            System.Windows.Shapes.Rectangle videoSegment = new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Fill = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Stroke = Brushes.Transparent,
                StrokeThickness = 2,
                RadiusX = 3,
                RadiusY = 3
            };
            videoSegment.MouseDown += VideoSegment_MouseDown;
            segmentContainer.Children.Add(videoSegment);

            // 左側手把
            Thumb leftHandle = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.SizeWE,
                Background = Brushes.White,
                Opacity = 0
            };
            leftHandle.DragDelta += LeftHandle_DragDelta;
            leftHandle.DragStarted += (s, e) => VideoPlayer.Pause();
            leftHandle.DragCompleted += (s, e) => VideoPlayer.Play();

            // 右側手把
            Thumb rightHandle = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.SizeWE,
                Background = Brushes.White,
                Opacity = 0
            };
            rightHandle.DragDelta += RightHandle_DragDelta;
            rightHandle.DragStarted += (s, e) => VideoPlayer.Pause();
            rightHandle.DragCompleted += (s, e) => VideoPlayer.Play();

            segmentContainer.Children.Add(leftHandle);
            segmentContainer.Children.Add(rightHandle);

            // 整體拖移事件
            segmentContainer.MouseMove += VideoSegment_MouseMove;
            segmentContainer.MouseLeftButtonUp += VideoSegment_MouseUp;

            Canvas.SetLeft(segmentContainer, 0);
            Canvas.SetTop(segmentContainer, 45);
            VideoTrackCanvas.Children.Add(segmentContainer);
        }

        // 處理左側拖動：非破壞性，只移動標記，可以隨時還原
        private void LeftHandle_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement thumb && thumb.Parent is Grid container)
            {
                double currentLeft = Canvas.GetLeft(container);
                double newLeft = currentLeft + e.HorizontalChange;
                double newWidth = container.Width - e.HorizontalChange;

                // 左邊界：不超過灰色底層左側（0）；右邊界：框框最小寬度 10px
                if (newWidth > 10 && newLeft >= 0)
                {
                    Canvas.SetLeft(container, newLeft);
                    container.Width = newWidth;

                    trimStartSeconds = newLeft / PIXELS_PER_SECOND;
                    TxtStartTime.Text = trimStartSeconds.ToString("F1");
                    segmentDragTrimDuration = trimEndSeconds - trimStartSeconds;

                    if (VideoPlayer.Position.TotalSeconds < trimStartSeconds)
                        VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                }
                
            }
        }

        // 處理右側拖動：非破壞性，只移動標記，可以隨時還原
        private void RightHandle_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement thumb && thumb.Parent is Grid container)
            {
                double newWidth = container.Width + e.HorizontalChange;
                double currentLeft = Canvas.GetLeft(container);

                // 右邊界：不超過灰色底層右側（影片總長）；最小寬度 10px
                double maxEnd = currentVideoDuration * PIXELS_PER_SECOND;
                if (newWidth > 10 && (currentLeft + newWidth) <= maxEnd)
                {
                    container.Width = newWidth;

                    trimEndSeconds = (currentLeft + newWidth) / PIXELS_PER_SECOND;
                    TxtEndTime.Text = trimEndSeconds.ToString("F1");
                    segmentDragTrimDuration = trimEndSeconds - trimStartSeconds;

                    if (VideoPlayer.Position.TotalSeconds > trimEndSeconds)
                        VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                }
            }
        }

        private void VideoSegment_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ClearSelection();

            // 1. 確認點擊的是影片矩形 (Rectangle)
            if (sender is System.Windows.Shapes.Rectangle rect)
            {
                // 2. 矩形的 Parent 就是包裹它的 Grid (也就是我們在 AddVideoToTimeline 建立的物件)
                if (rect.Parent is Grid parentGrid)
                {
                    rect.Stroke = System.Windows.Media.Brushes.White;
                    rect.StrokeThickness = 2;

                    // 顯示手把 (選取效果)
                    foreach (var child in parentGrid.Children)
                    {
                        if (child is System.Windows.Controls.Primitives.Thumb thumb)
                            thumb.Opacity = 0.5;
                    }

                    // 3. 開始整體拖移邏輯：記錄起始狀態
                    isDraggingSegment = true;

                    // 取得目前的滑鼠位置 (相對於畫布)
                    Point currentPos = e.GetPosition(VideoTrackCanvas);
                    segmentDragStartPoint = currentPos;
                    segmentDragStartMouseX = currentPos.X;

                    // 【修正點】：使用 parentGrid 來獲取當前的 Left 位置，並存入 segmentStartLeft
                    segmentDragStartLeft = Canvas.GetLeft(parentGrid);
                    segmentStartLeft = segmentDragStartLeft; // 確保與 MouseUp 判斷用的變數同步

                    segmentDragTrimDuration = trimEndSeconds - trimStartSeconds; // 保存框框寬度（秒）

                    // 擷取滑鼠，確保移出方塊外也能繼續拖動
                    parentGrid.CaptureMouse();
                }
            }
            e.Handled = true;
        }
        private void VideoTrackCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ClearSelection();
        }

        // 整體拖移：MouseMove
        private void VideoSegment_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!isDraggingSegment) return;
            if (sender is Grid container)
            {
                double currentMouseX = e.GetPosition(VideoTrackCanvas).X;
                double delta = currentMouseX - segmentDragStartMouseX;
                double newLeft = segmentDragStartLeft + delta;

                // 邊界保護：不能超出影片總長度左側
                double maxLeft = (currentVideoDuration - segmentDragTrimDuration) * PIXELS_PER_SECOND;
                if (newLeft < 0) newLeft = 0;
                if (newLeft > maxLeft) newLeft = maxLeft;

                Canvas.SetLeft(container, newLeft);

                // 更新 trim 時間：保持框框寬度（秒）不變，只平移
                trimStartSeconds = newLeft / PIXELS_PER_SECOND;
                trimEndSeconds = trimStartSeconds + segmentDragTrimDuration;
                TxtStartTime.Text = trimStartSeconds.ToString("F1");
                TxtEndTime.Text = trimEndSeconds.ToString("F1");
                
            }
        }

        // 整體拖移：MouseUp
        private void VideoSegment_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isDraggingSegment)
            {
                isDraggingSegment = false;

                // 【修正點】：先將 sender 轉型為 Grid，並命名為 container
                if (sender is Grid container)
                {
                    container.ReleaseMouseCapture();

                    // 取得放開滑鼠時，方塊在 Canvas 上的位置
                    double currentLeft = Canvas.GetLeft(container);

                    // 判斷位移量是否超過 1 像素 (避免單純點擊時微小的抖動觸發跳轉)
                    if (Math.Abs(currentLeft - segmentStartLeft) > 1.0)
                    {
                        // 1. 更新剪輯數值（秒數 = 像素 / 比例）
                        trimStartSeconds = currentLeft / PIXELS_PER_SECOND;
                        trimEndSeconds = trimStartSeconds + (container.Width / PIXELS_PER_SECOND);

                        // 2. 更新 UI 數字文字框
                        TxtStartTime.Text = trimStartSeconds.ToString("F1");
                        TxtEndTime.Text = trimEndSeconds.ToString("F1");

                        // 3. 只有真的移動位置後，才將影片跳轉到新的起點預覽
                        VideoPlayer.Position = TimeSpan.FromSeconds(trimStartSeconds);
                    }
                    // else: 位移太小視為單純點擊，不執行任何跳轉
                }
            }
            e.Handled = true;
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
                        // 影像軌的選取框清除
                        if (inner is System.Windows.Shapes.Rectangle rect)
                            rect.Stroke = System.Windows.Media.Brushes.Transparent;

                        // 字卡軌的選取框清除 ---
                        if (inner is Border border)
                            border.BorderBrush = System.Windows.Media.Brushes.Transparent;

                        // 隱藏左右手把
                        if (inner is System.Windows.Controls.Primitives.Thumb thumb)
                            thumb.Opacity = 0;
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

        // ================= 字卡軌設定 =================

        // 按鈕套用字卡
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

            // 3. 改用 Grid 當作容器，讓它能包容文字區塊與左右縮放手把
            Grid textContainer = new Grid
            {
                Width = cardWidth,
                Height = 30,
                Tag = "TextSegment",
                Cursor = Cursors.SizeAll,
                Background = Brushes.Transparent // 確保空白處也能被點擊拖曳
            };

            Border textCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(70, 80, 100)),
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
                TextTrimming = TextTrimming.CharacterEllipsis,
                Padding = new Thickness(5, 0, 5, 0)
            };

            textCard.Child = textBlock;

            // 綁定點擊主體事件 (選取、準備拖曳)
            textCard.MouseDown += TextSegment_MouseDown;
            textContainer.Children.Add(textCard);

            // 左側手把
            Thumb leftHandle = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.SizeWE,
                Background = Brushes.White,
                Opacity = 0 // 預設隱藏，點擊選取時才顯示
            };
            leftHandle.DragDelta += TextLeftHandle_DragDelta;

            // 右側手把
            Thumb rightHandle = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.SizeWE,
                Background = Brushes.White,
                Opacity = 0
            };
            rightHandle.DragDelta += TextRightHandle_DragDelta;

            textContainer.Children.Add(leftHandle);
            textContainer.Children.Add(rightHandle);

            // 綁定整體拖曳與放開事件
            textContainer.MouseMove += TextSegment_MouseMove;
            textContainer.MouseLeftButtonUp += TextSegment_MouseUp;

            // 設定字卡在畫布上的位置 (Top=5，顯示在影像軌上方)
            Canvas.SetLeft(textContainer, startX);
            Canvas.SetTop(textContainer, 5);

            VideoTrackCanvas.Children.Add(textContainer);

            StoreSubtitleSettings(textToApply);

            TxtSubtitle.Text = "";  // 清空輸入框
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

        // ================= 字卡軌專用：縮放邏輯 =================

        // 處理字卡左側縮放
        private void TextLeftHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb thumb && thumb.Parent is Grid container)
            {
                double currentLeft = Canvas.GetLeft(container);
                double newLeft = currentLeft + e.HorizontalChange;
                double newWidth = container.Width - e.HorizontalChange;

                // 左邊界：不超過 0，最小保留 10px 寬度
                if (newWidth > 10 && newLeft >= 0)
                {
                    Canvas.SetLeft(container, newLeft);
                    container.Width = newWidth;
                }
            }
        }

        // 處理字卡右側縮放
        private void TextRightHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb thumb && thumb.Parent is Grid container)
            {
                double newWidth = container.Width + e.HorizontalChange;
                double currentLeft = Canvas.GetLeft(container);
                double maxEnd = currentVideoDuration * PIXELS_PER_SECOND;

                // 右邊界：不超過影片總長，最小保留 10px 寬度
                if (newWidth > 10 && (currentLeft + newWidth) <= maxEnd)
                {
                    container.Width = newWidth;
                }
            }
        }

        // ================= 字卡軌專用：整體拖曳邏輯 =================

        private void TextSegment_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ClearSelection();

            if (sender is Border textCard && textCard.Parent is Grid parentGrid)
            {
                // 顯示白框
                textCard.BorderBrush = Brushes.White;

                // 顯示左右手把
                foreach (var child in parentGrid.Children)
                {
                    if (child is Thumb thumb) thumb.Opacity = 0.5;
                }

                // 記錄拖曳起始狀態
                isDraggingTextSegment = true;
                textDragStartMouseX = e.GetPosition(VideoTrackCanvas).X;
                textDragStartLeft = Canvas.GetLeft(parentGrid);

                parentGrid.CaptureMouse();
                e.Handled = true;
            }
        }

        private void TextSegment_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingTextSegment) return;
            if (sender is Grid container)
            {
                double currentMouseX = e.GetPosition(VideoTrackCanvas).X;
                double delta = currentMouseX - textDragStartMouseX;
                double newLeft = textDragStartLeft + delta;

                // 邊界保護：不能拖到小於 0，也不能超過影片最右側
                double maxLeft = (currentVideoDuration * PIXELS_PER_SECOND) - container.Width;
                if (maxLeft < 0) maxLeft = 0;
                if (newLeft < 0) newLeft = 0;
                if (newLeft > maxLeft) newLeft = maxLeft;

                Canvas.SetLeft(container, newLeft);
            }
        }

        private void TextSegment_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDraggingTextSegment)
            {
                isDraggingTextSegment = false;
                if (sender is Grid container)
                {
                    container.ReleaseMouseCapture();

                    // 未來如果需要記錄這張字卡的實際起訖時間傳給 FFmpeg，可以在這裡計算：
                    // double cardStartTime = Canvas.GetLeft(container) / PIXELS_PER_SECOND;
                    // double cardEndTime = cardStartTime + (container.Width / PIXELS_PER_SECOND);
                }
            }
            e.Handled = true;
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
            if (VideoPlayer.Source == null) return;

            if (!double.TryParse(TxtStartTime.Text, out double startTime) ||
                !double.TryParse(TxtEndTime.Text, out double endTime))
            {
                MessageBox.Show("請輸入有效的開始與結束時間！", "錯誤");
                return;
            }

            // 邏輯檢查
            if (startTime < 0 || endTime <= startTime || endTime > currentVideoDuration)
            {
                MessageBox.Show($"時間設定不合法（影片總長：{currentVideoDuration:F1}秒）", "錯誤");
                return;
            }

            // 1. 更新全域變數
            trimStartSeconds = startTime;
            trimEndSeconds = endTime;
            segmentDragTrimDuration = endTime - startTime;

            // 2. 同步更新 UI 上的藍色框 (Grid)
            foreach (var child in VideoTrackCanvas.Children)
            {
                if (child is Grid container && (string)container.Tag == "VideoSegment")
                {
                    double newLeft = startTime * PIXELS_PER_SECOND;
                    double newWidth = (endTime - startTime) * PIXELS_PER_SECOND;

                    Canvas.SetLeft(container, newLeft);
                    container.Width = newWidth;
                    break;
                }
            }

            // 3. 讓播放器跳轉至新的開始點預覽
            VideoPlayer.Position = TimeSpan.FromSeconds(startTime);
            MessageBox.Show($"已同步剪輯範圍：{startTime}s - {endTime}s", "設定成功");
        }

    }
}