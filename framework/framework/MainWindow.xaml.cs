using framework.Export;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace framework
{
// ─────────────────────────────────────────────────────────────
    //  字卡樣式資料模型
    // ─────────────────────────────────────────────────────────────
    public class SubtitleStyle
    {
        public string Text            { get; set; } = "";
        public string FontFamily      { get; set; } = "微軟正黑體";
        public double FontSize        { get; set; } = 24;
        public string FontWeight      { get; set; } = "Normal";
        public bool   IsItalic        { get; set; } = false;
        public bool   IsUnderline     { get; set; } = false;
        public string FontColor       { get; set; } = "#FFFFFF";
        public string ShadowColor     { get; set; } = "#000000";
        public double StrokeWidth     { get; set; } = 0;
        public string StrokeColor     { get; set; } = "#000000";
        public string BackgroundColor { get; set; } = "#00000000";
        public string Position        { get; set; } = "底部置中（Bottom Center）";
        public double StartSeconds    { get; set; } = 0;
        public double DurationSeconds { get; set; } = 5;

        // 拖曳自訂座標
        public bool   UseCustomPosition { get; set; } = false;
        public double CustomX           { get; set; } = 0;
        public double CustomY           { get; set; } = 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  FFmpeg 字幕濾鏡轉換器
    // ─────────────────────────────────────────────────────────────
    public static class SubtitleFilterBuilder
    {
        public static string Build(IEnumerable<SubtitleStyle> styles, double videoDuration)
        {
            var filters = new List<string>();

            foreach (var s in styles)
            {
                string fontPath    = ResolveFontPath(s.FontFamily);
                string escapedText = s.Text
                    .Replace("\\", "\\\\")
                    .Replace("'",  "\\'")
                    .Replace(":",  "\\:");

                string fc     = ToFfmpegColor(s.FontColor,   1.0);
                string sc     = ToFfmpegColor(s.StrokeColor, 1.0);
                string shadow = ToFfmpegColor(s.ShadowColor, 0.8);

                (string x, string y) = ResolvePosition(s);

                string bold   = (s.FontWeight == "Bold" || s.FontWeight == "ExtraBold") ? "1" : "0";
                string italic = s.IsItalic ? "1" : "0";

                double endSec  = Math.Min(s.StartSeconds + s.DurationSeconds, videoDuration);
                string enable  = $"between(t\\,{s.StartSeconds:F3}\\,{endSec:F3})";

                var parts = new List<string>
                {
                    $"fontfile='{fontPath}'",
                    $"text='{escapedText}'",
                    $"fontsize={s.FontSize}",
                    $"fontcolor={fc}",
                    $"bold={bold}",
                    $"italic={italic}",
                    $"x={x}",
                    $"y={y}",
                    $"enable='{enable}'"
                };

                if (s.StrokeWidth > 0)
                {
                    parts.Add($"borderw={s.StrokeWidth}");
                    parts.Add($"bordercolor={sc}");
                }

                parts.Add("shadowx=2");
                parts.Add("shadowy=2");
                parts.Add($"shadowcolor={shadow}");

                if (!s.BackgroundColor.TrimStart('#').StartsWith("00"))
                {
                    string bg = ToFfmpegColor(s.BackgroundColor, 1.0);
                    parts.Add("box=1");
                    parts.Add($"boxcolor={bg}");
                    parts.Add("boxborderw=6");
                }

                filters.Add("drawtext=" + string.Join(":", parts));
            }

            return string.Join(",", filters);
        }

        private static string ResolveFontPath(string fontFamily)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "微軟正黑體",       @"C\:/Windows/Fonts/msjh.ttc" },
                { "新細明體",         @"C\:/Windows/Fonts/mingliu.ttc" },
                { "標楷體",           @"C\:/Windows/Fonts/kaiu.ttf" },
                { "Arial",            @"C\:/Windows/Fonts/arial.ttf" },
                { "Times New Roman",  @"C\:/Windows/Fonts/times.ttf" },
                { "Consolas",         @"C\:/Windows/Fonts/consola.ttf" },
            };
            return map.TryGetValue(fontFamily, out var path) ? path : @"C\:/Windows/Fonts/msjh.ttc";
        }

        private static string ToFfmpegColor(string hex, double alphaOverride)
        {
            hex = hex.TrimStart('#');
            string r, g, b;
            if      (hex.Length == 8) { r = hex[2..4]; g = hex[4..6]; b = hex[6..8]; }
            else if (hex.Length == 6) { r = hex[0..2]; g = hex[2..4]; b = hex[4..6]; }
            else                      { r = "FF"; g = "FF"; b = "FF"; }
            int aa = (int)(alphaOverride * 255);
            return $"0x{r}{g}{b}{aa:X2}";
        }

        private static (string x, string y) ResolvePosition(SubtitleStyle s)
        {
            if (s.UseCustomPosition)
                return (((int)s.CustomX).ToString(), ((int)s.CustomY).ToString());

            return s.Position switch
            {
                "頂部置中（Top Center）"    => ("(w-text_w)/2", "20"),
                "中央置中（Middle Center）" => ("(w-text_w)/2", "(h-text_h)/2"),
                "底部靠左（Bottom Left）"   => ("20",           "h-text_h-30"),
                "底部靠右（Bottom Right）"  => ("w-text_w-20",  "h-text_h-30"),
                _                           => ("(w-text_w)/2", "h-text_h-30"),
            };
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  主視窗
    // ─────────────────────────────────────────────────────────────
    public partial class MainWindow : Window
    {
        // ── 基本狀態
        private string currentVideoPath     = "";
        private double currentVideoDuration = 0; // 影片持續時間（秒）
        private string pendingSubtitleText  = "";

        // ── 字卡清單（新系統，對應時間軸字卡軌）
        private List<SubtitleStyle> subtitleList = new();

        // ── 時間軸字卡軌：目前選取的字卡 Border
        private Border? selectedSubtitleCard = null;

        // ── Overlay Canvas 字卡對應表
        private Dictionary<SubtitleStyle, Border> overlayBorderMap = new();

        // ── Overlay 拖曳狀態
        private Border?        draggedOverlayBorder = null;
        private SubtitleStyle? draggedOverlayStyle  = null;
        private Point          dragOffset;
        private bool           isDraggingOverlay    = false;

        // ── 播放頭計時器
        private System.Windows.Threading.DispatcherTimer playheadTimer;
        private System.Windows.Threading.DispatcherTimer autoScrollTimer;
        private const double PIXELS_PER_SECOND = 20;

        // 記錄目前是否正在拖曳游標
        private bool isDraggingPlayhead  = false;
        private bool wasPlayingBeforeDrag = false;

        //  ── 時間軸平移
        private TranslateTransform timelineTransform = new TranslateTransform();
        private double timelineOffsetX = 0;

        // ── 影像片段拖曳狀態
        private bool isDraggingSegment      = false;
        private double segmentDragStartMouseX = 0;
        private double segmentDragStartLeft   = 0;
        private double segmentDragTrimDuration = 0;
        private bool isDraggingTextSegment   = false;
        private double textDragStartMouseX   = 0;
        private double textDragStartLeft     = 0;
        private Point segmentDragStartPoint;
        private double segmentStartLeft;

        // ── 影像片段資料結構
        public class VideoSegmentData
        {
            public Guid   Id             { get; set; } = Guid.NewGuid(); // 唯一識別碼
            public double TimelineStart  { get; set; }                   // 在時間軸上的起始秒數 (Canvas Left)
            public double InternalOffset { get; set; }                   // 影片內容的起始點 (從原始影片第幾秒開始撥)
            public double Duration       { get; set; }                   // 片段持續長度
            public Grid   UIElement      { get; set; }                   // 對應的 UI 物件 (藍色框框)
        }
        // --- 資料結構物件化管理 ---
        private List<VideoSegmentData> videoSegments = new List<VideoSegmentData>();
        private VideoSegmentData? selectedSegment    = null;

        // 使用「捷徑」屬性，讓舊代碼可以唯讀目前的數值（解決 CS0200 的讀取部分）
        private double trimStartSeconds => selectedSegment?.TimelineStart ?? 0;
        private double trimEndSeconds   => (selectedSegment?.TimelineStart + selectedSegment?.Duration) ?? currentVideoDuration;
        
        public enum EditorTool { Select, Scissors }
        private EditorTool currentTool = EditorTool.Select;

        // ══════════════════════════════════════
        //  建構子 & 初始化
        // ══════════════════════════════════════
        public MainWindow()
        {
            InitializeComponent();
            InitializePlayheadTimer(); 

            this.KeyDown += MainWindow_KeyDown;
           
            VideoTrackCanvas.MouseDown    += (s, e) => ClearSelection();  // 點擊軌道空白處取消選取
            SubtitleTrackCanvas.MouseDown += (s, e) => ClearSelection();

            TimeRulerCanvas.Background     = Brushes.Transparent;
            VideoTrackCanvas.Background    = Brushes.Transparent;
            SubtitleTrackCanvas.Background = Brushes.Transparent;

            // 時間軸拖曳（播放頭）
            foreach (var canvas in new[] { TimeRulerCanvas, VideoTrackCanvas, SubtitleTrackCanvas })
            {
                canvas.PreviewMouseLeftButtonDown += Timeline_MouseLeftButtonDown;
                canvas.PreviewMouseMove           += Timeline_MouseMove;
                canvas.PreviewMouseLeftButtonUp   += Timeline_MouseLeftButtonUp;
            }

            this.Loaded += (s, e) =>
            {
                if (TimelineContentStack != null)
                    TimelineContentStack.RenderTransform = timelineTransform;

                RefreshMiniPreview();
            };
        }

        // ══════════════════════════════════════
        //  計時器
        // ══════════════════════════════════════
        private void InitializePlayheadTimer()
        {
            playheadTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
            playheadTimer.Tick += PlayheadTimer_Tick;

            autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
            autoScrollTimer.Tick += AutoScrollTimer_Tick;
        }

        private void PlayheadTimer_Tick(object sender, EventArgs e)
        {
            // 確保有載入影片且播放器有 NaturalDuration
            if (VideoPlayer.Source == null || !VideoPlayer.NaturalDuration.HasTimeSpan) return;
            
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
                double x = currentTime * PIXELS_PER_SECOND;
                PlayheadLine.X1 = x;
                PlayheadLine.X2 = x;
                UpdateSubtitleOverlay(currentTime);
            }
        }

        private void AutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!isDraggingPlayhead) return;
            
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

        // ══════════════════════════════════════
        //  播放頭拖曳
        // ══════════════════════════════════════
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
            if (isDraggingPlayhead && sender is UIElement elem)
                UpdatePlayheadPosition(e.GetPosition(elem).X);
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
            // 新增防護邏輯：計算邊界並限制滑鼠座標
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
            // 【新增】：拖曳游標時即時更新字卡
            UpdateSubtitleOverlay(targetSeconds);
        }

        // ══════════════════════════════════════
        //  ScrollViewer 滾輪
        // ══════════════════════════════════════
        private void TimelineScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                double newOffset = sv.HorizontalOffset - (e.Delta * 0.8);
                newOffset = Math.Max(0, Math.Min(newOffset, sv.ScrollableWidth));
                sv.ScrollToHorizontalOffset(newOffset);
                e.Handled = true;
            }
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                return;
            }
        }

         // ══════════════════════════════════════
        //  鍵盤快速鍵
        // ══════════════════════════════════════
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F1) { MainTabControl.SelectedIndex = 0; e.Handled = true; return; }  // 字卡設定
            if (e.Key == Key.F2) { MainTabControl.SelectedIndex = 1; e.Handled = true; return; }  // 影像剪輯
            if (e.Key == Key.F3) { MainTabControl.SelectedIndex = 2; e.Handled = true; return; }  // 畫面調整

            if (e.Key == Key.V) { SetEditorTool(EditorTool.Select);   e.Handled = true; }
            if (e.Key == Key.C) { SetEditorTool(EditorTool.Scissors); e.Handled = true; }

            if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox)
            {
                BtnDelete_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void SetEditorTool(EditorTool tool)
        {
            currentTool = tool;
            if (tool == EditorTool.Select)
            {
                this.Cursor = Cursors.Arrow;
                if (RadioSelect != null && RadioSelect.IsChecked == false) RadioSelect.IsChecked = true;
            }
            else if (tool == EditorTool.Scissors)
            {
                this.Cursor = Cursors.Cross;
                if (RadioScissors != null && RadioScissors.IsChecked == false) RadioScissors.IsChecked = true;
            }
        }

        private void ToolRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag != null)
            {
                string toolName = rb.Tag.ToString();
                if (toolName == "Select")
                    SetEditorTool(EditorTool.Select);
                else if (toolName == "Scissors")
                    SetEditorTool(EditorTool.Scissors);
            }
        }

        // ══════════════════════════════════════
        //  工具列：匯入 / 輸出
        // ══════════════════════════════════════
        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "影片檔案|*.mp4;*.mov;*.avi;*.mkv|所有檔案|*.*" };
            if (dlg.ShowDialog() != true) return;
            
            currentVideoPath  = dlg.FileName;
            VideoPlayer.Source = new Uri(currentVideoPath);

            // 註冊 MediaOpened 事件，確保在影片資訊載入後才執行繪製
            VideoPlayer.MediaOpened += (s, ev) =>
            {
                if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;
                double dur = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                currentVideoDuration = dur;

                double w = dur * PIXELS_PER_SECOND + 100;
                TimeRulerCanvas.Width      = w;
                VideoTrackCanvas.Width     = w;
                SubtitleTrackCanvas.Width  = w;
                TimelineContentStack.Width = w;

                DrawTimeRuler(dur);
                AddVideoToTimeline(dur);
                RedrawSubtitleCards();

                PlayheadLine.Visibility = Visibility.Visible;
                PlayheadLine.X1 = PlayheadLine.X2 = 0;
                PlayheadLine.Y1 = 0; 
                PlayheadLine.Y2 = 190;

                timelineOffsetX     = 0;
                timelineTransform.X = 0;
            };

            VideoPlayer.Play(); 
            playheadTimer.Start();
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;
            currentVideoDuration    = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            PlayheadLine.Visibility = Visibility.Visible;
            PlayheadLine.X1 = PlayheadLine.X2 = 0;
            PlayheadLine.Y1 = 0; PlayheadLine.Y2 = 190;
            playheadTimer.Start();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentVideoPath))
            { MessageBox.Show("請先匯入影片！", "錯誤"); return; }

            try
            {
                var exportWin = new ExportWindow(currentVideoDuration) { Owner = this };
                if (exportWin.ShowDialog() != true) return;

                // Create ExportSettings object with SubtitleText initialized
                var subtitleText = subtitleList.Count > 0
                    ? SubtitleFilterBuilder.Build(subtitleList, currentVideoDuration)
                    : string.Empty;

                var settings = new ExportSettings
                {
                    Format = exportWin.SelectedFormat,
                    Bitrate = exportWin.FinalBitrate,
                    VideoCodec = exportWin.SelectedVideoCodec,
                    AudioCodec = exportWin.SelectedAudioCodec,
                    AudioBitrate = "128",
                    AudioChannels = 2,
                    OutputWidth = exportWin.OutputWidth,
                    OutputHeight = exportWin.OutputHeight,
                    EnableFastStart = exportWin.EnableFastStart,
                    TrimStartSeconds = trimStartSeconds,
                    TrimEndSeconds = trimEndSeconds,
                    DurationSeconds = currentVideoDuration,
                    SubtitleText = subtitleText // Initialize SubtitleText here
                };

                if (!AskSaveExportPath(settings)) return;
                if (ExecuteExport(settings))
                    MessageBox.Show($"匯出完成：\n{settings.OutputPath}", "完成");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出視窗開啟失敗：{ex.Message}\n{ex.StackTrace}", "錯誤");
            }
        }

        // ══════════════════════════════════════
        //  播放器控制
        // ══════════════════════════════════════
        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Play();
            playheadTimer.Start();
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Pause();
            playheadTimer.Stop();
        }

        // ══════════════════════════════════════
        //  字卡設定面板 ── 即時預覽事件
        // ══════════════════════════════════════
        private void StyleControl_Changed(object sender, SelectionChangedEventArgs e) => RefreshMiniPreview();

        private void StyleControl_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshColorSwatch(sender as TextBox);
            RefreshMiniPreview();
        }

        private void StyleControl_CheckChanged(object sender, RoutedEventArgs e) => RefreshMiniPreview();

        private void StyleControl_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblStrokeValue != null)
                LblStrokeValue.Text = ((int)SliderStroke.Value).ToString();
            RefreshMiniPreview();
        }

        private void RefreshColorSwatch(TextBox? tb)
        {
            if (tb == null) return;
            var (swatch, _) = GetSwatchForTextBox(tb);
            if (swatch == null) return;
            try
            {
                var bc = new BrushConverter();
                swatch.Background = (Brush)bc.ConvertFromString(tb.Text)!;
            }
            catch { /* 格式不合法時忽略 */ }
        }

        private (Border? swatch, string field) GetSwatchForTextBox(TextBox tb)
        {
            if (tb == TxtFontColor)   return (FontColorSwatch,   "font");
            if (tb == TxtShadowColor) return (ShadowColorSwatch, "shadow");
            if (tb == TxtBorderColor) return (BorderColorSwatch, "border");
            if (tb == TxtBgColor)     return (BgColorSwatch,     "bg");
            return (null, "");
        }

        private void RefreshMiniPreview()
        {
            if (TxtMiniPreview == null || MiniPreviewBorder == null) return;
            var style = ReadStyleFromUI();
            style.Text = string.IsNullOrWhiteSpace(TxtSubtitle?.Text) ? "字卡預覽" : TxtSubtitle.Text;
            ApplyStyleToTextBlock(TxtMiniPreview, MiniPreviewBorder, style, scaleDown: true);
        }

        // ══════════════════════════════════════
        //  字卡設定面板 ── 讀取 UI
        // ══════════════════════════════════════
        private SubtitleStyle ReadStyleFromUI()
        {
            double.TryParse(TxtFontSize?.Text, out double fontSize);
            if (fontSize <= 0) fontSize = 24;

            double.TryParse(TxtSubStartTime?.Text, out double startSec);
            double.TryParse(TxtSubDuration?.Text,  out double durSec);
            if (durSec <= 0) durSec = 5;

            return new SubtitleStyle
            {
                FontFamily      = (ComboFontFamily?.SelectedItem  as ComboBoxItem)?.Content?.ToString() ?? "微軟正黑體",
                FontSize        = fontSize,
                FontWeight      = (ComboFontWeight?.SelectedItem  as ComboBoxItem)?.Content?.ToString() ?? "Normal",
                IsItalic        = ChkItalic?.IsChecked    == true,
                IsUnderline     = ChkUnderline?.IsChecked == true,
                FontColor       = TxtFontColor?.Text    ?? "#FFFFFF",
                ShadowColor     = TxtShadowColor?.Text  ?? "#000000",
                StrokeWidth     = SliderStroke?.Value   ?? 0,
                StrokeColor     = TxtBorderColor?.Text  ?? "#000000",
                BackgroundColor = TxtBgColor?.Text      ?? "#00000000",
                Position        = (ComboPosition?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "底部置中（Bottom Center）",
                StartSeconds    = startSec,
                DurationSeconds = durSec,
            };
        }

        // ══════════════════════════════════════
        //  字卡設定面板 ── 套用字卡
        // ══════════════════════════════════════
        private void BtnAddText_Click(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.Source == null || !VideoPlayer.NaturalDuration.HasTimeSpan)
            { MessageBox.Show("請先匯入影片！", "提示"); return; }

            if (string.IsNullOrWhiteSpace(TxtSubtitle.Text))
            { MessageBox.Show("請先輸入字卡內容！", "提示"); return; }

            var style  = ReadStyleFromUI();
            style.Text = TxtSubtitle.Text;

            // 若未手動填寫開始時間，使用目前播放位置
            if (style.StartSeconds <= 0 && VideoPlayer.Position.TotalSeconds > 0)
                style.StartSeconds = VideoPlayer.Position.TotalSeconds;

            subtitleList.Add(style);
            RedrawSubtitleCards();
            CreateOverlayCard(style);

            pendingSubtitleText = style.Text; // 保留相容舊版 export 路徑
            TxtSubtitle.Text    = "";
        }

        // ── 更新所選字卡樣式
        private void BtnUpdateStyle_Click(object sender, RoutedEventArgs e)
        {
            if (selectedSubtitleCard == null)
            { MessageBox.Show("請先在時間軸上點選一張字卡！", "提示"); return; }

            var tag = selectedSubtitleCard.Tag as SubtitleStyle;
            if (tag == null) return;

            var newStyle = ReadStyleFromUI();
            newStyle.Text            = tag.Text;           // 保留原文字
            newStyle.StartSeconds    = tag.StartSeconds;
            newStyle.DurationSeconds = tag.DurationSeconds;

            int idx = subtitleList.IndexOf(tag);
            if (idx >= 0) subtitleList[idx] = newStyle;

            RedrawSubtitleCards();
            RebuildOverlayCards();
            MessageBox.Show("字卡樣式已更新！", "完成");
        }

        // ══════════════════════════════════════
        //  字卡 Overlay（疊在影片上，可拖曳）
        // ══════════════════════════════════════
        private void BtnPreviewSubtitle_Click(object sender, RoutedEventArgs e)
        {
            RebuildOverlayCards();
        }

        private void BtnHidePreview_Click(object sender, RoutedEventArgs e)
        {
            SubtitleOverlayCanvas.Children.Clear();
            overlayBorderMap.Clear();
            draggedOverlayBorder = null;
            draggedOverlayStyle  = null;
        }

        // 播放中即時更新 Overlay 可見性
        private void UpdateSubtitleOverlay(double currentTimeSec)
        {
            foreach (var kv in overlayBorderMap)
            {
                bool active = currentTimeSec >= kv.Key.StartSeconds &&
                              currentTimeSec <  kv.Key.StartSeconds + kv.Key.DurationSeconds;
                kv.Value.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RebuildOverlayCards()
        {
            SubtitleOverlayCanvas.Children.Clear();
            overlayBorderMap.Clear();
            draggedOverlayBorder = null;
            draggedOverlayStyle  = null;
            foreach (var s in subtitleList)
                CreateOverlayCard(s);
        }

        private Border CreateOverlayCard(SubtitleStyle s)
        {
            var tb     = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding      = new Thickness(10, 4, 10, 4),
                Cursor       = Cursors.SizeAll,
                Tag          = s,
                Effect       = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 2, Opacity = 0.7 }
            };
            border.Child = tb;

            ApplyStyleToTextBlock(tb, border, s, scaleDown: false);

            SubtitleOverlayCanvas.UpdateLayout();
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            (double initLeft, double initTop) = s.UseCustomPosition
                ? (s.CustomX, s.CustomY)
                : CalcDefaultPosition(s.Position,
                                      SubtitleOverlayCanvas.ActualWidth,
                                      SubtitleOverlayCanvas.ActualHeight,
                                      border.DesiredSize.Width,
                                      border.DesiredSize.Height);
            Canvas.SetLeft(border, initLeft);
            Canvas.SetTop(border,  initTop);

            border.MouseLeftButtonDown += OverlayCard_MouseDown;
            border.MouseMove           += OverlayCard_MouseMove;
            border.MouseLeftButtonUp   += OverlayCard_MouseUp;

            SubtitleOverlayCanvas.Children.Add(border);
            overlayBorderMap[s] = border;
            return border;
        }

        private void OverlayCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border b) return;

            foreach (var child in SubtitleOverlayCanvas.Children)
                if (child is Border other) other.BorderBrush = Brushes.Transparent;

            b.BorderBrush     = new SolidColorBrush(Color.FromRgb(0, 145, 255));
            b.BorderThickness = new Thickness(2);

            draggedOverlayBorder = b;
            draggedOverlayStyle  = b.Tag as SubtitleStyle;
            isDraggingOverlay    = true;
            dragOffset           = e.GetPosition(b);

            b.CaptureMouse();
            e.Handled = true;

            if (draggedOverlayStyle != null)
                SyncPositionToPanel(Canvas.GetLeft(b), Canvas.GetTop(b));
        }

        private void OverlayCard_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingOverlay || draggedOverlayBorder == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;

            Point mouseOnCanvas = e.GetPosition(SubtitleOverlayCanvas);
            double newLeft = mouseOnCanvas.X - dragOffset.X;
            double newTop  = mouseOnCanvas.Y - dragOffset.Y;

            double maxLeft = SubtitleOverlayCanvas.ActualWidth  - draggedOverlayBorder.ActualWidth;
            double maxTop  = SubtitleOverlayCanvas.ActualHeight - draggedOverlayBorder.ActualHeight;
            newLeft = Math.Clamp(newLeft, 0, Math.Max(0, maxLeft));
            newTop  = Math.Clamp(newTop,  0, Math.Max(0, maxTop));

            Canvas.SetLeft(draggedOverlayBorder, newLeft);
            Canvas.SetTop(draggedOverlayBorder,  newTop);

            SyncPositionToPanel(newLeft, newTop);

            if (draggedOverlayStyle != null)
            {
                draggedOverlayStyle.UseCustomPosition = true;
                draggedOverlayStyle.CustomX           = newLeft;
                draggedOverlayStyle.CustomY           = newTop;
            }

            e.Handled = true;
        }

        private void OverlayCard_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isDraggingOverlay = false;
            draggedOverlayBorder?.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void SyncPositionToPanel(double x, double y)
        {
            if (TxtPosX != null) TxtPosX.Text = ((int)x).ToString();
            if (TxtPosY != null) TxtPosY.Text = ((int)y).ToString();
        }

        private static (double left, double top) CalcDefaultPosition(
            string position, double cw, double ch, double bw, double bh)
        {
            return position switch
            {
                "頂部置中（Top Center）"    => ((cw - bw) / 2, 20),
                "中央置中（Middle Center）" => ((cw - bw) / 2, (ch - bh) / 2),
                "底部靠左（Bottom Left）"   => (20, ch - bh - 30),
                "底部靠右（Bottom Right）"  => (cw - bw - 20, ch - bh - 30),
                _                           => ((cw - bw) / 2, ch - bh - 30),
            };
        }

        private void ApplyStyleToTextBlock(TextBlock tb, Border border, SubtitleStyle s, bool scaleDown)
        {
            double scale = scaleDown ? 0.5 : 1.0;
            var bc       = new BrushConverter();

            tb.Text       = s.Text;
            tb.FontFamily = new FontFamily(s.FontFamily);
            tb.FontSize   = s.FontSize * scale;

            tb.FontWeight = s.FontWeight switch
            {
                "Bold"      => FontWeights.Bold,
                "ExtraBold" => FontWeights.ExtraBold,
                "Light"     => FontWeights.Light,
                _           => FontWeights.Normal
            };
            tb.FontStyle       = s.IsItalic   ? FontStyles.Italic    : FontStyles.Normal;
            tb.TextDecorations = s.IsUnderline ? TextDecorations.Underline : null;

            try { tb.Foreground = (Brush)bc.ConvertFromString(s.FontColor)!; }
            catch { tb.Foreground = Brushes.White; }

            double sw = s.StrokeWidth * scale;
            if (sw > 0)
            {
                try
                {
                    var sc = (Color)ColorConverter.ConvertFromString(s.StrokeColor)!;
                    tb.Effect = new DropShadowEffect { Color = sc, BlurRadius = sw * 2, ShadowDepth = 0, Opacity = 1.0 };
                }
                catch { tb.Effect = null; }
            }
            else
            {
                try
                {
                    var sc = (Color)ColorConverter.ConvertFromString(s.ShadowColor)!;
                    tb.Effect = new DropShadowEffect { Color = sc, BlurRadius = 4, ShadowDepth = 2, Opacity = 0.7 };
                }
                catch { tb.Effect = null; }
            }

            try { border.Background = (Brush)bc.ConvertFromString(s.BackgroundColor)!; }
            catch { border.Background = Brushes.Transparent; }
        }

        // ══════════════════════════════════════
        //  時間軸：字卡軌繪製（含拖曳 & 左右手把縮放）
        // ══════════════════════════════════════
        private void RedrawSubtitleCards()
        {
            SubtitleTrackCanvas.Children.Clear();
            selectedSubtitleCard = null;

            foreach (var s in subtitleList)
            {
                double leftX = s.StartSeconds    * PIXELS_PER_SECOND;
                double width = Math.Max(s.DurationSeconds * PIXELS_PER_SECOND, 10);

                // 外層 Grid（負責整體拖曳 & 容納手把）
                var container = new Grid
                {
                    Width  = width,
                    Height = 32,
                    Tag    = s,                    // Tag 直接存 SubtitleStyle，方便回寫
                    Cursor = Cursors.SizeAll,
                    Background = Brushes.Transparent  // 讓空白處也能被點擊
                };

                // 紫色主體卡片
                var card = new Border
                {
                    Background      = new SolidColorBrush(Color.FromRgb(80, 50, 130)),
                    CornerRadius    = new CornerRadius(4),
                    BorderBrush     = Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag             = s,   // SubtitleStyle，供 Delete / UpdateStyle 使用
                    ToolTip         = $"{s.Text}\n{s.StartSeconds:F1}s → {s.StartSeconds + s.DurationSeconds:F1}s"
                };

                var tb = new TextBlock
                {
                    Text                = s.Text,
                    Foreground          = Brushes.White,
                    FontSize            = 11,
                    VerticalAlignment   = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming        = TextTrimming.CharacterEllipsis,
                    Padding             = new Thickness(6, 0, 4, 0)
                };
                card.Child = tb;

                // 左側縮放手把
                var leftHandle = new Thumb
                {
                    Width = 8, HorizontalAlignment = HorizontalAlignment.Left,
                    Cursor = Cursors.SizeWE, Background = Brushes.White, Opacity = 0
                };
                leftHandle.DragDelta += SubtitleLeftHandle_DragDelta;

                // 右側縮放手把
                var rightHandle = new Thumb
                {
                    Width = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Cursor = Cursors.SizeWE, Background = Brushes.White, Opacity = 0
                };
                rightHandle.DragDelta += SubtitleRightHandle_DragDelta;

                container.Children.Add(card);
                container.Children.Add(leftHandle);
                container.Children.Add(rightHandle);

                // 點擊卡片主體：選取 + 填回 UI
                var capturedContainer = container;
                var capturedStyle     = s;
                card.MouseDown += (_, ev) =>
                {
                    ClearSelection();
                    card.BorderBrush     = Brushes.White;
                    selectedSubtitleCard = card;   // 仍用 card Border 記錄選取
                    // 顯示左右手把
                    foreach (var child in capturedContainer.Children)
                        if (child is Thumb t) t.Opacity = 0.6;
                    LoadStyleToUI(capturedStyle);

                    // 開始整體拖曳
                    isDraggingTextSegment = true;
                    textDragStartMouseX   = ev.GetPosition(SubtitleTrackCanvas).X;
                    textDragStartLeft     = Canvas.GetLeft(capturedContainer);
                    capturedContainer.CaptureMouse();
                    ev.Handled = true;
                };

                // 整體拖曳移動
                container.MouseMove += (_, ev) =>
                {
                    if (!isDraggingTextSegment) return;
                    double delta   = ev.GetPosition(SubtitleTrackCanvas).X - textDragStartMouseX;
                    double newLeft = textDragStartLeft + delta;
                    double maxLeft = currentVideoDuration * PIXELS_PER_SECOND - capturedContainer.Width;
                    newLeft = Math.Clamp(newLeft, 0, Math.Max(0, maxLeft));
                    Canvas.SetLeft(capturedContainer, newLeft);

                    // 即時回寫，讓面板數值與播放器判斷同步
                    capturedStyle.StartSeconds = newLeft / PIXELS_PER_SECOND;
                    if (TxtSubStartTime != null)
                        TxtSubStartTime.Text = capturedStyle.StartSeconds.ToString("F1");
                };

                // 放開滑鼠：結束拖曳，回寫最終秒數
                container.MouseLeftButtonUp += (_, ev) =>
                {
                    if (!isDraggingTextSegment) return;
                    isDraggingTextSegment = false;
                    capturedContainer.ReleaseMouseCapture();
                    capturedStyle.StartSeconds = Canvas.GetLeft(capturedContainer) / PIXELS_PER_SECOND;
                    // 更新 ToolTip
                    card.ToolTip = $"{capturedStyle.Text}\n{capturedStyle.StartSeconds:F1}s → {capturedStyle.StartSeconds + capturedStyle.DurationSeconds:F1}s";
                    ev.Handled = true;
                };

                Canvas.SetLeft(container, leftX);
                Canvas.SetTop(container, 4);
                SubtitleTrackCanvas.Children.Add(container);
            }
        }

        // ── 字卡軌左側手把：縮短/延長開始時間
        private void SubtitleLeftHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || thumb.Parent is not Grid container) return;
            if (container.Tag is not SubtitleStyle style) return;

            double currentLeft = Canvas.GetLeft(container);
            double newLeft     = currentLeft + e.HorizontalChange;
            double newWidth    = container.Width - e.HorizontalChange;

            if (newWidth < 10 || newLeft < 0) return;

            Canvas.SetLeft(container, newLeft);
            container.Width = newWidth;

            style.StartSeconds    = newLeft  / PIXELS_PER_SECOND;
            style.DurationSeconds = newWidth / PIXELS_PER_SECOND;

            if (TxtSubStartTime != null) TxtSubStartTime.Text = style.StartSeconds.ToString("F1");
            if (TxtSubDuration  != null) TxtSubDuration.Text  = style.DurationSeconds.ToString("F1");
        }

        // ── 字卡軌右側手把：縮短/延長結束時間
        private void SubtitleRightHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || thumb.Parent is not Grid container) return;
            if (container.Tag is not SubtitleStyle style) return;

            double newWidth  = container.Width + e.HorizontalChange;
            double currentLeft = Canvas.GetLeft(container);
            double maxEnd    = currentVideoDuration * PIXELS_PER_SECOND;

            if (newWidth < 10 || (currentLeft + newWidth) > maxEnd) return;

            container.Width       = newWidth;
            style.DurationSeconds = newWidth / PIXELS_PER_SECOND;

            if (TxtSubDuration != null) TxtSubDuration.Text = style.DurationSeconds.ToString("F1");
        }

        // 點擊字卡時，把樣式填回 UI
        private void LoadStyleToUI(SubtitleStyle s)
        {
            TxtSubtitle.Text        = s.Text;
            TxtFontSize.Text        = s.FontSize.ToString();
            TxtFontColor.Text       = s.FontColor;
            TxtShadowColor.Text     = s.ShadowColor;
            TxtBorderColor.Text     = s.StrokeColor;
            TxtBgColor.Text         = s.BackgroundColor;
            SliderStroke.Value      = s.StrokeWidth;
            ChkItalic.IsChecked     = s.IsItalic;
            ChkUnderline.IsChecked  = s.IsUnderline;
            TxtSubStartTime.Text    = s.StartSeconds.ToString("F1");
            TxtSubDuration.Text     = s.DurationSeconds.ToString("F1");

            SelectComboByContent(ComboFontFamily, s.FontFamily);
            SelectComboByContent(ComboFontWeight, s.FontWeight);
            SelectComboByContent(ComboPosition,   s.Position);

            RefreshMiniPreview();
        }

        private void SelectComboByContent(ComboBox cb, string value)
        {
            foreach (ComboBoxItem item in cb.Items)
                if (item.Content?.ToString() == value) { cb.SelectedItem = item; return; }
        }

        // ══════════════════════════════════════
        //  時間軸：影像軌繪製
        // ══════════════════════════════════════
        private void AddVideoToTimeline(double durationInSeconds)
        {
            double totalWidth = durationInSeconds * PIXELS_PER_SECOND;

            // 1. 初始化畫布與寬度
             VideoTrackCanvas.Width     = totalWidth + 100;
            SubtitleTrackCanvas.Width  = totalWidth + 100;
            TimeRulerCanvas.Width      = totalWidth + 100;
            TimelineContentStack.Width = totalWidth + 100;
            VideoTrackCanvas.Children.Clear();
            videoSegments.Clear(); // 清空舊的資料清單

            var newSegment = new VideoSegmentData
            {
                TimelineStart = 0,
                InternalOffset = 0,
                Duration = durationInSeconds
            };

            Grid segmentGrid = CreateSegmentUI(newSegment);
            newSegment.UIElement = segmentGrid; // 將 UI 儲存在物件中以便後續操作

            videoSegments.Add(newSegment);
            selectedSegment = newSegment;

            TxtStartTime.Text = "0.0";
            TxtEndTime.Text = durationInSeconds.ToString("F1");

            // 灰色底層
            var fullBar = new System.Windows.Shapes.Rectangle
            {
                Width  = totalWidth, Height = 35,
                Fill   = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                RadiusX = 3, RadiusY = 3, IsHitTestVisible = false
            };
            Canvas.SetLeft(fullBar, 0); Canvas.SetTop(fullBar, 45);
            VideoTrackCanvas.Children.Add(fullBar);
            VideoTrackCanvas.Children.Add(segmentGrid);
        }

        private Grid CreateSegmentUI(VideoSegmentData data)
        {
            Grid container = new Grid
            {
                Width = data.Duration * PIXELS_PER_SECOND,
                Height = 35,
                Tag = "VideoSegment",
                Cursor = Cursors.SizeAll
            };

            var rect = new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Fill = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Stroke = Brushes.Transparent,
                StrokeThickness = 2,
                RadiusX = 3, RadiusY = 3
            };
            rect.MouseDown += VideoSegment_MouseDown;
            container.Children.Add(rect);

            var leftHandle = new Thumb { Width = 8, HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.SizeWE, Background = Brushes.White, Opacity = 0 };
            leftHandle.DragDelta += LeftHandle_DragDelta;
            leftHandle.DragStarted += (s, e) => VideoPlayer.Pause();
            leftHandle.DragCompleted += (s, e) => VideoPlayer.Play();

            var rightHandle = new Thumb { Width = 8, HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.SizeWE, Background = Brushes.White, Opacity = 0 };
            rightHandle.DragDelta += RightHandle_DragDelta;
            rightHandle.DragStarted += (s, e) => VideoPlayer.Pause();
            rightHandle.DragCompleted += (s, e) => VideoPlayer.Play();

            container.Children.Add(leftHandle);
            container.Children.Add(rightHandle);
            container.MouseMove += VideoSegment_MouseMove;
            container.MouseLeftButtonUp += VideoSegment_MouseUp;

            // 設定初始位置
            Canvas.SetLeft(container, data.TimelineStart * PIXELS_PER_SECOND);
            Canvas.SetTop(container, 45);

            return container;
        }

        // 處理左側拖動：非破壞性，只移動標記，可以隨時還原
        private void LeftHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement thumb && thumb.Parent is Grid container)
            {
                var data = videoSegments.FirstOrDefault(s => s.UIElement == container);
                if (data == null) return;

                double currentLeft = Canvas.GetLeft(container);
                double newLeft = currentLeft + e.HorizontalChange;
                double newWidth = container.Width - e.HorizontalChange;

                if (newWidth > 10 && newLeft >= 0)
                {
                    Canvas.SetLeft(container, newLeft);
                    container.Width = newWidth;
                    data.TimelineStart = newLeft / PIXELS_PER_SECOND;
                    data.Duration = newWidth / PIXELS_PER_SECOND;
                    data.InternalOffset = data.TimelineStart; 
                    TxtStartTime.Text = data.TimelineStart.ToString("F1");
                    TxtEndTime.Text = (data.TimelineStart + data.Duration).ToString("F1");
                    if (VideoPlayer.Position.TotalSeconds < data.TimelineStart)
                        VideoPlayer.Position = TimeSpan.FromSeconds(data.TimelineStart);
                }
            }
        }

        private void RightHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is FrameworkElement thumb && thumb.Parent is Grid container)
            {
                var data = videoSegments.FirstOrDefault(s => s.UIElement == container);
                if (data == null) return;

                double newWidth = container.Width + e.HorizontalChange;
                double currentLeft = Canvas.GetLeft(container);
                double maxEnd = currentVideoDuration * PIXELS_PER_SECOND;
                if (newWidth > 10 && (currentLeft + newWidth) <= maxEnd)
                {
                    container.Width = newWidth;
                    data.Duration = newWidth / PIXELS_PER_SECOND;
                    TxtEndTime.Text = (data.TimelineStart + data.Duration).ToString("F1");
                    if (VideoPlayer.Position.TotalSeconds > (data.TimelineStart + data.Duration))
                        VideoPlayer.Position = TimeSpan.FromSeconds(data.TimelineStart);
                }
            }
        }

        private void VideoSegment_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ClearSelection();

            if (sender is System.Windows.Shapes.Rectangle rect && rect.Parent is Grid parentGrid)
            {
                var segment = videoSegments.FirstOrDefault(s => s.UIElement == parentGrid);
                if (segment == null) return;

                // --- 剪刀工具邏輯 ---
                if (currentTool == EditorTool.Scissors)
                {
                    // 取得點擊在時間軸畫布上的 X 座標
                    double clickX = e.GetPosition(VideoTrackCanvas).X;
                    double splitTime = clickX / PIXELS_PER_SECOND;

                    // 執行分割 (呼叫先前寫好的 SplitSegment 方法)
                    SplitSegment(segment, splitTime);

                    e.Handled = true;
                    return;
                }

                // --- 原有的選取與拖移邏輯 ---
                selectedSegment = segment; // 設定目前選中的物件

                rect.Stroke = Brushes.White;
                rect.StrokeThickness = 2;

                foreach (var child in parentGrid.Children)
                    if (child is Thumb thumb)   thumb.Opacity = 0.5;

                // 記錄起始狀態供拖移使用
                isDraggingSegment = true;
                Point currentPos = e.GetPosition(VideoTrackCanvas);
                segmentDragStartPoint = currentPos;
                segmentDragStartMouseX = currentPos.X;

                // 使用 parentGrid 獲取當前 Left 位置
                segmentDragStartLeft = Canvas.GetLeft(parentGrid);
                segmentStartLeft = segmentDragStartLeft; 

                // 從物件中獲取長度，不再依賴全域變數
                segmentDragTrimDuration = segment.Duration; 

                // 同步 UI 文字框
                TxtStartTime.Text = segment.TimelineStart.ToString("F1");
                TxtEndTime.Text = (segment.TimelineStart + segment.Duration).ToString("F1");

                parentGrid.CaptureMouse();
            }

            e.Handled = true;
        }
        
        private void VideoTrackCanvas_MouseDown(object sender, MouseButtonEventArgs e) => ClearSelection();

        // 整體拖移：MouseMove
        private void VideoSegment_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingSegment || selectedSegment == null) return;

            if (sender is Grid container)
            {
                double currentMouseX = e.GetPosition(VideoTrackCanvas).X;
                double delta = currentMouseX - segmentDragStartMouseX;
                double newLeft = Math.Clamp(segmentDragStartLeft + delta,
                    0, (currentVideoDuration - container.Width / PIXELS_PER_SECOND) * PIXELS_PER_SECOND);
                Canvas.SetLeft(container, newLeft);
                selectedSegment.TimelineStart = newLeft / PIXELS_PER_SECOND;
                selectedSegment.InternalOffset = selectedSegment.TimelineStart;
                TxtStartTime.Text = selectedSegment.TimelineStart.ToString("F1");
                TxtEndTime.Text = (selectedSegment.TimelineStart + selectedSegment.Duration).ToString("F1");
            }
        }

        // 整體拖移：MouseUp
        private void VideoSegment_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDraggingSegment && selectedSegment != null)
            {
                isDraggingSegment = false;

                if (sender is Grid container)
                {
                    container.ReleaseMouseCapture();

                    // 取得放開滑鼠時，方塊在 Canvas 上的位置
                    double currentLeft = Canvas.GetLeft(container);

                    // 判斷位移量是否超過 1 像素 (避免單純點擊時微小的抖動觸發跳轉)
                    if (Math.Abs(currentLeft - segmentStartLeft) > 1.0)
                    {
                        // --- 核心修改：更新資料物件 ---
                        // 1. 更新資料 (秒數 = 像素 / 比例)
                        selectedSegment.TimelineStart = currentLeft / PIXELS_PER_SECOND;
                        // Duration 保持不變（因為這是平移），或是根據 container.Width 重新計算以保險
                        selectedSegment.Duration = container.Width / PIXELS_PER_SECOND;
                        selectedSegment.InternalOffset = selectedSegment.TimelineStart;

                        // 2. 更新 UI 數字文字框 (使用物件屬性)
                        TxtStartTime.Text = selectedSegment.TimelineStart.ToString("F1");
                        TxtEndTime.Text = (selectedSegment.TimelineStart + selectedSegment.Duration).ToString("F1");

                        // 3. 只有真的移動位置後，才將影片跳轉到新的起點預覽
                        VideoPlayer.Position = TimeSpan.FromSeconds(selectedSegment.TimelineStart);
                    }
                    // else: 位移太小視為單純點擊，不執行任何跳轉
                }
            }
            e.Handled = true;
        }

        private void SplitSegment(VideoSegmentData segment, double splitPointSeconds)
        {
            double relativeSplit = splitPointSeconds - segment.TimelineStart;

            var nextSegment = new VideoSegmentData
            {
                TimelineStart  = splitPointSeconds,
                InternalOffset = segment.InternalOffset + relativeSplit,
                Duration       = segment.Duration - relativeSplit
            };

            segment.Duration         = relativeSplit;
            segment.UIElement.Width  = segment.Duration * PIXELS_PER_SECOND;

            Grid nextGrid = CreateSegmentUI(nextSegment);
            nextSegment.UIElement = nextGrid;
            videoSegments.Add(nextSegment);
            VideoTrackCanvas.Children.Add(nextGrid);

            VideoPlayer.Position = TimeSpan.FromSeconds(splitPointSeconds);
        }

        // 辨識並清除字卡(Border)的白框
        private void ClearSelection()
        {
            selectedSubtitleCard = null;

            // 影像軌：Grid 包 Rectangle + Thumb
            foreach (var child in VideoTrackCanvas.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var inner in grid.Children)
                    {
                        if (inner is System.Windows.Shapes.Rectangle r) r.Stroke = Brushes.Transparent;
                        if (inner is Border b)  b.BorderBrush = Brushes.Transparent;
                        if (inner is Thumb  t)  t.Opacity     = 0;
                    }
                }
            }

            // 字卡軌：Grid 包 Border + Thumb（新架構）
            foreach (var child in SubtitleTrackCanvas.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var inner in grid.Children)
                    {
                        if (inner is Border b) b.BorderBrush = Brushes.Transparent;
                        if (inner is Thumb  t) t.Opacity     = 0;
                    }
                }
            }
        }

        private void DrawTimeRuler(double totalSeconds)
        {
            TimeRulerCanvas.Children.Clear();
            double pps = PIXELS_PER_SECOND;

            for (double s = 0; s <= totalSeconds + 1; s += 1)
            {
                double x       = s * pps;
                bool   isMajor = s % 5 == 0;

                TimeRulerCanvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = x, X2 = x, Y1 = isMajor ? 5 : 15, Y2 = 25,
                    Stroke = Brushes.Gray, StrokeThickness = 1
                });

                if (isMajor)
                {
                    var txt = new TextBlock { Text = $"{s:0}s", Foreground = Brushes.LightGray, FontSize = 10 };
                    Canvas.SetLeft(txt, x + 2); Canvas.SetTop(txt, 2);
                    TimeRulerCanvas.Children.Add(txt);
                }
            }
        }

        // ══════════════════════════════════════
        //  刪除
        // ══════════════════════════════════════
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            // 優先刪除選取的字卡
            if (selectedSubtitleCard != null)
            {
                if (selectedSubtitleCard.Tag is SubtitleStyle s)
                    subtitleList.Remove(s);
                selectedSubtitleCard = null;
                RedrawSubtitleCards();
                RebuildOverlayCards();
                return;
            }

            if (string.IsNullOrEmpty(currentVideoPath))
            { MessageBox.Show("目前沒有載入任何影片片段。", "提示"); return; }

            var r = MessageBox.Show("確定要從時間軸移除此影片嗎？", "確認刪除",
                                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes)
            {
                VideoTrackCanvas.Children.Clear();
                SubtitleTrackCanvas.Children.Clear();
                subtitleList.Clear();
                currentVideoPath        = "";
                currentVideoDuration    = 0;
                timelineOffsetX         = 0;
                timelineTransform.X     = 0;
                VideoPlayer.Stop();
                VideoPlayer.Source      = null;
                playheadTimer.Stop();
                PlayheadLine.Visibility = Visibility.Collapsed;
                SubtitleOverlayCanvas.Children.Clear();
                overlayBorderMap.Clear();
            }
        }

        // ══════════════════════════════════════
        //  影像剪輯面板
        // ══════════════════════════════════════
        private void BtnTrim_Click(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.Source == null || selectedSegment == null) return;

            if (!double.TryParse(TxtStartTime.Text, out double st) ||
                !double.TryParse(TxtEndTime.Text,   out double et))
            { MessageBox.Show("請輸入有效的時間！", "錯誤"); return; }

            if (st < 0 || et <= st || et > currentVideoDuration)
            { MessageBox.Show($"時間不合法（總長：{currentVideoDuration:F1}秒）", "錯誤"); return; }

            StoreTrimSettings(st, et);
            segmentDragTrimDuration = et - st;
            VideoPlayer.Position    = TimeSpan.FromSeconds(st);
            MessageBox.Show($"已設定剪輯：{st:F1}秒 ~ {et:F1}秒", "系統訊息");
        }
        
        private void StoreTrimSettings(double startSeconds, double endSeconds)
        {
            if (selectedSegment == null) return;
            
            selectedSegment.TimelineStart = startSeconds;
            selectedSegment.Duration = endSeconds - startSeconds;
            selectedSegment.InternalOffset = startSeconds;

            if (selectedSegment.UIElement != null)
            {
                Canvas.SetLeft(selectedSegment.UIElement, startSeconds * PIXELS_PER_SECOND);
                selectedSegment.UIElement.Width = (endSeconds - startSeconds) * PIXELS_PER_SECOND;
            }    
        }

        // ══════════════════════════════════════
        //  匯出輔助
        // ══════════════════════════════════════
        private ExportSettings CreateExportSettings(VideoFormat format, string bitrate, ExportWindow ew)
        {
            return new ExportSettings
            {
                Format           = format,
                Bitrate          = bitrate,
                VideoCodec       = ew.SelectedVideoCodec,
                AudioCodec       = ew.SelectedAudioCodec,
                AudioBitrate     = "128",
                AudioChannels    = 2,
                OutputWidth      = ew.OutputWidth,
                OutputHeight     = ew.OutputHeight,
                EnableFastStart  = ew.EnableFastStart,
                TrimStartSeconds = trimStartSeconds,
                TrimEndSeconds   = trimEndSeconds,
                DurationSeconds  = currentVideoDuration,
                SubtitleText     = pendingSubtitleText,
            };
        }

        private bool AskSaveExportPath(ExportSettings settings)
        {
             var dlg = new SaveFileDialog
            {
                Filter           = "MP4 檔案 (*.mp4)|*.mp4|MKV 檔案 (*.mkv)|*.mkv|MOV 檔案 (*.mov)|*.mov",
                FileName         = Path.GetFileNameWithoutExtension(currentVideoPath) + "." + settings.Format.ToString().ToLower(),
                DefaultExt       = settings.Format.ToString().ToLower(),
                AddExtension     = true,
                InitialDirectory = Path.GetDirectoryName(currentVideoPath)
            };
            if (dlg.ShowDialog() != true) return false;
            settings.OutputPath = dlg.FileName;
            return true;
        }

        private bool ExecuteExport(ExportSettings settings)
        {
            if (string.IsNullOrEmpty(settings.OutputPath))
            { MessageBox.Show("未設定輸出檔案路徑。", "錯誤"); return false; }

            var ffmpegPath = FfmpegLocator.LocateExecutable();
            if (string.IsNullOrEmpty(ffmpegPath))
            { MessageBox.Show("找不到 FFmpeg 執行檔。請安裝或將 ffmpeg.exe 放在應用程式目錄。", "錯誤"); return false; }

            var args = FfmpegArgumentBuilder.Build(currentVideoPath, settings);

            try
            {
                using var proc = new Process();
                proc.StartInfo.FileName               = ffmpegPath;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError  = true;
                proc.StartInfo.UseShellExecute        = false;
                proc.StartInfo.CreateNoWindow         = true;

                foreach (var a in args) proc.StartInfo.ArgumentList.Add(a);

                proc.Start();
                string err = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                { MessageBox.Show($"FFmpeg 執行失敗：\n{err}", "錯誤"); return false; }
                return true;
            }
            catch (Win32Exception) { MessageBox.Show("找不到 FFmpeg 執行檔。", "錯誤"); return false; }
            catch (Exception ex)   { MessageBox.Show($"匯出失敗：{ex.Message}", "錯誤"); return false; }
        }
    }
}
