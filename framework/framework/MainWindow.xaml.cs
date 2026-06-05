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
    public interface ITimelineTrackItem
    {
        double TimelineStartSeconds { get; set; }
        double TimelineDurationSeconds { get; set; }
    }

    //  字卡樣式資料模型
    public class SubtitleStyle : ITimelineTrackItem
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
        public int    TrackIndex      { get; set; } = 0;  // 0~2，對應字卡軌 1~3

        public double TimelineStartSeconds
        {
            get => StartSeconds; set => StartSeconds = value;
        }
        public double TimelineDurationSeconds
        {
            get => DurationSeconds; set => DurationSeconds = value;
        }

        // 拖曳自訂座標
        public bool   UseCustomPosition { get; set; } = false;
        public double CustomX           { get; set; } = 0;
        public double CustomY           { get; set; } = 0;
    }

    //  FFmpeg 字幕濾鏡轉換器
    public static class SubtitleFilterBuilder
    {
        public static string Build(IEnumerable<SubtitleStyle> styles, double videoDuration,
                                           double canvasW, double canvasH, double exportW, double exportH)
        {
            var filters = new List<string>();
            double scaleX = (canvasW > 0 && exportW > 0) ? exportW / canvasW : 1.0;
            double scaleY = (canvasH > 0 && exportH > 0) ? exportH / canvasH : 1.0;
            double fontScale = scaleY;

            foreach (var s in styles)
            {
                string fontPath = ResolveFontPath(s.FontFamily, s.FontWeight, s.IsItalic);
                string escapedText = s.Text.Replace("\\", "\\\\").Replace("'", "\\'").Replace(":", "\\:");
                string fc = ToFfmpegColor(s.FontColor, 1.0);
                string sc = ToFfmpegColor(s.StrokeColor, 1.0);
                string shadow = ToFfmpegColor(s.ShadowColor, 0.8);

                int scaledFontSize = Math.Max(1, (int)(s.FontSize * fontScale));
                string x, y;

                if (s.UseCustomPosition)
                {
                    x = ((int)(s.CustomX * scaleX)).ToString();
                    y = ((int)(s.CustomY * scaleY)).ToString();
                }
                else
                {
                    int padY = (int)(30 * scaleY);
                    int padX = (int)(20 * scaleX);
                    int topY = (int)(20 * scaleY);

                    y = s.Position switch
                    {
                        "頂部置中（Top Center）" => topY.ToString(),
                        "中央置中（Middle Center）" => "(h-text_h)/2",
                        "底部靠左（Bottom Left）" => $"h-text_h-{padY}",
                        "底部靠右（Bottom Right）" => $"h-text_h-{padY}",
                        _ => $"h-text_h-{padY}"
                    };
                    x = s.Position switch
                    {
                        "底部靠左（Bottom Left）" => padX.ToString(),
                        "底部靠右（Bottom Right）" => $"w-text_w-{padX}",
                        _ => "(w-text_w)/2"
                    };
                }

                double endSec = Math.Min(s.StartSeconds + s.DurationSeconds, videoDuration);
                string enable = $"between(t\\,{s.StartSeconds:F3}\\,{endSec:F3})";

                var parts = new List<string>
                {
                    $"fontfile='{fontPath}'",
                    $"text='{escapedText}'",
                    $"fontsize={scaledFontSize}", // 帶入放大的字體
                    $"fontcolor={fc}",
                    $"x={x}",                     // 帶入轉換後的座標
                    $"y={y}",
                    $"enable='{enable}'"
                };
                if (s.StrokeWidth > 0)
                {
                    int scaledStroke = Math.Max(1, (int)(s.StrokeWidth * fontScale));
                    parts.Add($"borderw={scaledStroke}");
                    parts.Add($"bordercolor={sc}");
                }

                parts.Add($"shadowx={(int)(2 * fontScale)}");
                parts.Add($"shadowy={(int)(2 * fontScale)}");
                parts.Add($"shadowcolor={shadow}");

                if (!s.BackgroundColor.TrimStart('#').StartsWith("00"))
                {
                    string bg = ToFfmpegColor(s.BackgroundColor, 1.0);
                    int scaledBoxBorder = Math.Max(1, (int)(6 * fontScale));
                    parts.Add("box=1");
                    parts.Add($"boxcolor={bg}");
                    parts.Add($"boxborderw={scaledBoxBorder}");
                }

                filters.Add("drawtext=" + string.Join(":", parts));
            }

            return string.Join(",", filters);
        }
        private static string ResolveFontPath(string fontFamily, string fontWeight, bool isItalic)
        {
            bool isBold = fontWeight == "Bold" || fontWeight == "ExtraBold";
            if (fontFamily.Equals("Arial", StringComparison.OrdinalIgnoreCase))
            {
                if (isBold && isItalic) return @"C\:/Windows/Fonts/arialbi.ttf"; // 粗斜體
                if (isBold) return @"C\:/Windows/Fonts/arialbd.ttf";             // 粗體
                if (isItalic) return @"C\:/Windows/Fonts/ariali.ttf";            // 斜體
                return @"C\:/Windows/Fonts/arial.ttf";                           // 一般
            }
            if (fontFamily.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase))
            {
                if (isBold && isItalic) return @"C\:/Windows/Fonts/timesbi.ttf";
                if (isBold) return @"C\:/Windows/Fonts/timesbd.ttf";
                if (isItalic) return @"C\:/Windows/Fonts/timesi.ttf";
                return @"C\:/Windows/Fonts/times.ttf";
            }
            if (fontFamily.Equals("Consolas", StringComparison.OrdinalIgnoreCase))
            {
                if (isBold && isItalic) return @"C\:/Windows/Fonts/consolaz.ttf";
                if (isBold) return @"C\:/Windows/Fonts/consolab.ttf";
                if (isItalic) return @"C\:/Windows/Fonts/consolai.ttf";
                return @"C\:/Windows/Fonts/consola.ttf";
            }
            if (fontFamily.Equals("微軟正黑體", StringComparison.OrdinalIgnoreCase))
            {
                if (fontWeight == "Light") return @"C\:/Windows/Fonts/msjhl.ttc"; // 細體
                if (isBold) return @"C\:/Windows/Fonts/msjhbd.ttc";               // 粗體
                return @"C\:/Windows/Fonts/msjh.ttc";                             // 一般
            }
            if (fontFamily.Equals("新細明體", StringComparison.OrdinalIgnoreCase))
            {
                return @"C\:/Windows/Fonts/mingliu.ttc";
            }
            if (fontFamily.Equals("標楷體", StringComparison.OrdinalIgnoreCase))
            {
                return @"C\:/Windows/Fonts/kaiu.ttf";
            }
            return @"C\:/Windows/Fonts/msjh.ttc";
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

    //  主視窗
    public partial class MainWindow : Window
    {
        // ── 調色盤 ColorConfirmed handler 快取（確保 -= 能取消同一個 instance）
        private Action<Color>? _handlerFontColor;
        private Action<Color>? _handlerShadowColor;
        private Action<Color>? _handlerBorderColor;
        private Action<Color>? _handlerBgColor;

        // ── 顏色字串欄位（原本是 XAML TextBlock，現在改為純 string 儲存）
        private string _fontColorHex   = "#FFFFFFFF";
        private string _shadowColorHex = "#FF000000";
        private string _borderColorHex = "#FF000000";
        private string _bgColorHex     = "#00000000";

        // ── 基本狀態
        private string currentVideoPath     = "";
        private double currentVideoDuration = 0;
        private string pendingSubtitleText  = "";
        private bool   currentVideoHasAudio = true;  // 無聲影片修正：記錄影片是否含有音訊

        // 字卡清單（新系統，對應時間軸字卡軌）
        private List<SubtitleStyle> subtitleList = new();

        // 多軌常數與 Canvas 陣列（SubtitleTrackCanvas0/1/2 定義於 XAML）
        private const int SUBTITLE_TRACK_COUNT = 3;
        private Canvas[] SubtitleTrackCanvases => new Canvas[] { SubtitleTrackCanvas0, SubtitleTrackCanvas1, SubtitleTrackCanvas2 };

        // 多軌拖曳：記錄拖曳開始時的來源軌道（-1 = 未拖曳）
        private int draggedSubtitleSourceTrack = -1;

        // 時間軸字卡軌：目前選取的字卡 Border
        private Border? selectedSubtitleCard = null;

        // Overlay Canvas 字卡對應表
        private Dictionary<SubtitleStyle, Border> overlayBorderMap = new();

        // Overlay 拖曳狀態
        private Border?        draggedOverlayBorder = null;
        private SubtitleStyle? draggedOverlayStyle  = null;
        private Point          dragOffset;
        private bool           isDraggingOverlay    = false;

        // 播放頭計時器
        private System.Windows.Threading.DispatcherTimer playheadTimer;
        private System.Windows.Threading.DispatcherTimer autoScrollTimerInstance;
        private const double PIXELS_PER_SECOND = 20;
        private DateTime lastTickTime = DateTime.Now;     // 用來計算現實中過了幾秒
        private double timelineCurrentSeconds = 0;        // 時間軸的虛擬時鐘 (紅線的真正位置)
        
        // 記錄目前是否正在拖曳游標
        private bool isDraggingPlayhead  = false;
        private bool wasPlayingBeforeDrag = false;

        // 時間軸平移
        private TranslateTransform timelineTransform = new TranslateTransform();
        private double timelineOffsetX = 0;

        // ── 影像片段拖曳狀態
        private bool isDraggingTrackItem = false;
        private Grid? draggedTrackItemUI = null;
        private ITimelineTrackItem? draggedTrackItemData = null;
        private double trackDragStartMouseX = 0;
        private double trackDragStartLeft = 0;
        private CommandHistory commandHistory = new CommandHistory();

        private List<VideoSegmentData>? preDragVideoOrder;
        private Dictionary<Guid, double>? preDragVideoStarts;

        private List<SubtitleStyle>? preDragSubtitleOrder;
        private Dictionary<SubtitleStyle, double>? preDragSubtitleStarts;

        // 影像片段資料結構
        public class VideoSegmentData : ITimelineTrackItem
        {
            public Guid   Id             { get; set; } = Guid.NewGuid(); // 唯一識別碼
            public double TimelineStart  { get; set; }                   // 在時間軸上的起始秒數 (Canvas Left)
            public double InternalOffset { get; set; }                   // 影片內容的起始點 (從原始影片第幾秒開始撥)
            public double Duration       { get; set; }                   // 片段持續長度
            public Grid   UIElement      { get; set; }                   // 對應的 UI 物件 (藍色框框)
            public double TimelineStartSeconds
            {
                get => TimelineStart; set => TimelineStart = value;
            }
            public double TimelineDurationSeconds
            {
                get => Duration; set => Duration = value;
            }
        }
        private List<VideoSegmentData> videoSegments = new List<VideoSegmentData>();
        private VideoSegmentData? selectedSegment    = null;

        // 使用「捷徑」屬性，讓舊代碼可以唯讀目前的數值（解決 CS0200 的讀取部分）
        private double trimStartSeconds => selectedSegment?.TimelineStart ?? 0;
        private double trimEndSeconds   => (selectedSegment?.TimelineStart + selectedSegment?.Duration) ?? currentVideoDuration;
        
        public enum EditorTool { Select, Scissors }
        private EditorTool currentTool = EditorTool.Select;

        public MainWindow()
        {
            InitializeComponent();
            InitializePlayheadTimer(); 

            this.KeyDown += MainWindow_PreviewKeyDown;
           
            VideoTrackCanvas.MouseDown    += (s, e) => ClearSelection();  // 點擊軌道空白處取消選取
            foreach (var c in SubtitleTrackCanvases) c.MouseDown += (s, e) => ClearSelection();

            TimeRulerCanvas.Background     = Brushes.Transparent;
            VideoTrackCanvas.Background    = Brushes.Transparent;

            // 時間軸拖曳（播放頭）— 原版邏輯，SubtitleTrackCanvases 取代單一 SubtitleTrackCanvas
            foreach (var canvas in new Canvas[] { TimeRulerCanvas, VideoTrackCanvas }
                         .Concat(SubtitleTrackCanvases).ToArray())
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
        private void SyncVideoPlayerToTimeline(double tlSeconds)
        {
            timelineCurrentSeconds = tlSeconds;

            var segment = videoSegments.FirstOrDefault(s =>
                tlSeconds >= s.TimelineStartSeconds &&
                tlSeconds < s.TimelineStartSeconds + s.TimelineDurationSeconds);

            if (segment != null)
            {
                double targetVideoPos = segment.InternalOffset + (tlSeconds - segment.TimelineStartSeconds);
                VideoPlayer.Position = TimeSpan.FromSeconds(targetVideoPos);
                VideoPlayer.Opacity = 1; // 🌟 修正：用透明度代替 Visibility
            }
            else
            {
                VideoPlayer.Opacity = 0; // 🌟 修正：用透明度代替 Visibility
            }

            double x = tlSeconds * PIXELS_PER_SECOND;
            PlayheadLine.X1 = x;
            PlayheadLine.X2 = x;
            UpdateSubtitleOverlay(tlSeconds);
        }
        //  計時器
        private void InitializePlayheadTimer()
        {
            playheadTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
            playheadTimer.Tick += PlayheadTimer_Tick;

            autoScrollTimerInstance = new() { Interval = TimeSpan.FromMilliseconds(30) };
            autoScrollTimerInstance.Tick += autoScrollTimerInstance_Tick;
        }

        private void PlayheadTimer_Tick(object sender, EventArgs e)
        {
            if (VideoPlayer.Source == null) return;

            // 1. 真實世界時間推進 (這就是我們的大會計時鐘)
            double nowSeconds = (DateTime.Now - lastTickTime).TotalSeconds;
            lastTickTime = DateTime.Now;

            if (isDraggingPlayhead || isDraggingTrackItem) return;

            // 讓虛擬時間軸穩穩地往前走
            timelineCurrentSeconds += nowSeconds;

            // 檢查是否播到整部片的最尾端
            double maxTimelineEnd = videoSegments.Count > 0 ? videoSegments.Max(s => s.TimelineStartSeconds + s.TimelineDurationSeconds) : 0;
            if (timelineCurrentSeconds >= maxTimelineEnd && maxTimelineEnd > 0)
            {
                timelineCurrentSeconds = maxTimelineEnd;
                playheadTimer.Stop();
                VideoPlayer.Pause();
                SyncVideoPlayerToTimeline(timelineCurrentSeconds); // 讓 UI 停在最後一格
                return;
            }

            // 2. 判斷目前的紅線落在哪個片段裡？
            var currentSegment = videoSegments.FirstOrDefault(s =>
                timelineCurrentSeconds >= s.TimelineStartSeconds &&
                timelineCurrentSeconds < s.TimelineStartSeconds + s.TimelineDurationSeconds);

            if (currentSegment != null)
            {
                // 【狀態 A：在影片片段內】
                if (VideoPlayer.Opacity == 0)
                {
                    VideoPlayer.Opacity = 1; // 跨過空隙了，恢復顯示！
                    VideoPlayer.Play();
                }

                // 計算目前影片「應該」在哪個位置
                double expectedVideoPos = currentSegment.InternalOffset + (timelineCurrentSeconds - currentSegment.TimelineStartSeconds);

                // 如果播放器進度跟預期差超過 0.2 秒 (例如剛跳過空隙，或系統卡頓)，強制它跟上
                if (Math.Abs(VideoPlayer.Position.TotalSeconds - expectedVideoPos) > 0.2)
                {
                    VideoPlayer.Position = TimeSpan.FromSeconds(expectedVideoPos);
                    VideoPlayer.Play();
                }
            }
            else
            {
                // 【狀態 B：在空隙中】
                if (VideoPlayer.Opacity != 0)
                {
                    VideoPlayer.Opacity = 0; // 改用 Opacity 隱藏，避免 WPF 崩潰
                    VideoPlayer.Pause();     // 暫停影片，等紅線走完空隙
                }
            }

            // 3. 更新游標與字卡 UI
            double x = timelineCurrentSeconds * PIXELS_PER_SECOND;
            PlayheadLine.X1 = x;
            PlayheadLine.X2 = x;
            UpdateSubtitleOverlay(timelineCurrentSeconds);
        }

        private void autoScrollTimerInstance_Tick(object sender, EventArgs e)
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

        //  播放頭拖曳
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
                    autoScrollTimerInstance.Start();

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
            if (isDraggingPlayhead)
            {
                isDraggingPlayhead = false;

                if (sender is UIElement element)
                {
                    element.ReleaseMouseCapture();
                }
                autoScrollTimerInstance.Stop();
                if (wasPlayingBeforeDrag)
                {
                    VideoPlayer.Play();
                    playheadTimer.Start();
                }
                wasPlayingBeforeDrag = false;
            }
        }
        private void UpdatePlayheadPosition(double mouseX)
        {
            double duration = currentVideoDuration;
            if (duration <= 0) return;

            double maxMouseX = duration * PIXELS_PER_SECOND;
            double safeMouseX = Math.Max(0, Math.Min(mouseX, maxMouseX));

            // 計算目標秒數後，直接呼叫我們的新方法
            double targetSeconds = safeMouseX / PIXELS_PER_SECOND;
            SyncVideoPlayerToTimeline(targetSeconds);
        }
        private void UpdateVideoVisibility(double currentTimelineSeconds)
        {
            if (VideoPlayer == null) return;

            bool isInsideAnySegment = false;
            foreach (var segment in videoSegments)
            {
                if (currentTimelineSeconds >= segment.TimelineStartSeconds &&
                    currentTimelineSeconds <= segment.TimelineStartSeconds + segment.TimelineDurationSeconds)
                {
                    isInsideAnySegment = true;
                    break;
                }
            }
            VideoPlayer.Visibility = isInsideAnySegment ? Visibility.Visible : Visibility.Hidden;
        }
        //  ScrollViewer 滾輪
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
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox focusedTextBox)
            {
                if (!focusedTextBox.IsMouseOver)
                {
                    MainRootGrid.Focus();
                    Keyboard.ClearFocus();
                }
            }
        }
        //  鍵盤快速鍵
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key actualKey = e.Key;
            if (actualKey == Key.ImeProcessed) actualKey = e.ImeProcessedKey;
            if (actualKey == Key.System) actualKey = e.SystemKey;

            if (actualKey == Key.F1) { MainTabControl.SelectedIndex = 0; e.Handled = true; return; }
            if (actualKey == Key.F2) { MainTabControl.SelectedIndex = 1; e.Handled = true; return; }
            if (actualKey == Key.F3) { MainTabControl.SelectedIndex = 2; e.Handled = true; return; }

            bool isTypingInTextBox = Keyboard.FocusedElement is TextBox;

            if (!isTypingInTextBox)
            {
                if (actualKey == Key.V) { SetEditorTool(EditorTool.Select); e.Handled = true; return; }
                if (actualKey == Key.C) { SetEditorTool(EditorTool.Scissors); e.Handled = true; return; }

                if (actualKey == Key.Delete)
                {
                    BtnDelete_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }
            }
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (actualKey == Key.Z)
                {
                    commandHistory.Undo();
                    e.Handled = true;
                    return;
                }
                if (actualKey == Key.Y)
                {
                    commandHistory.Redo();
                    e.Handled = true;
                    return;
                }
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

        //  匯入 / 輸出
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
                foreach (var c in SubtitleTrackCanvases) c.Width = w;
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
            // 確保把大會計的碼錶與虛擬時間全部歸零！
            timelineCurrentSeconds = 0;
            lastTickTime = DateTime.Now;
            VideoPlayer.Position = TimeSpan.Zero;
            VideoPlayer.Play();
            // 確保有啟動計時器
            if (playheadTimer != null)
            {
                playheadTimer.Start();
            }
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;
            currentVideoDuration    = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            PlayheadLine.Visibility = Visibility.Visible;
            PlayheadLine.X1 = PlayheadLine.X2 = 0;
            PlayheadLine.Y1 = 0; PlayheadLine.Y2 = 190;
            playheadTimer.Start();
            currentVideoHasAudio = VideoPlayer.HasAudio;  // 無聲影片修正：偵測音訊軌是否存在
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentVideoPath))
            { MessageBox.Show("請先匯入影片！", "錯誤"); return; }

            try
            {
                var exportWin = new ExportWindow(currentVideoDuration) { Owner = this };
                if (exportWin.ShowDialog() != true) return;

                double canvasW = SubtitleOverlayCanvas.ActualWidth;
                double canvasH = SubtitleOverlayCanvas.ActualHeight;
                double videoW = VideoPlayer.NaturalVideoWidth > 0 ? VideoPlayer.NaturalVideoWidth : 1920;
                double videoH = VideoPlayer.NaturalVideoHeight > 0 ? VideoPlayer.NaturalVideoHeight : 1080;
                double exportW = exportWin.OutputWidth > 0 ? exportWin.OutputWidth : videoW;
                double exportH = exportWin.OutputHeight > 0 ? exportWin.OutputHeight : videoH;

                var subtitleText = subtitleList.Count > 0
                    ? SubtitleFilterBuilder.Build(subtitleList, currentVideoDuration, canvasW, canvasH, exportW, exportH)
                    : string.Empty;
                var segmentsToExport = videoSegments
                    .OrderBy(s => s.TimelineStartSeconds)
                    .Select(s => new VideoSegmentExportData(
                        s.TimelineStartSeconds,
                        s.InternalOffset,
                        s.TimelineDurationSeconds
                    ))
                    .ToList();
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
                    SubtitleText = subtitleText,
                    Segments = segmentsToExport
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

        //  播放器控制
        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            lastTickTime = DateTime.Now; // 🌟 極度重要：重新對準真實時間

            // 檢查目前紅線是不是在片段內，在的話才叫影片播放
            var currentSegment = videoSegments.FirstOrDefault(s =>
                timelineCurrentSeconds >= s.TimelineStartSeconds &&
                timelineCurrentSeconds < s.TimelineStartSeconds + s.TimelineDurationSeconds);

            if (currentSegment != null)
            {
                VideoPlayer.Play();
            }

            playheadTimer.Start(); // 啟動時間軸
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Pause();
            playheadTimer.Stop();
        }

        // ══════════════════════════════════════
        //  字卡設定面板 ── 調色盤按鈕事件
        // ══════════════════════════════════════

        private void BtnPickFontColor_Click(object sender, RoutedEventArgs e)
            => OpenColorPicker(PopupFontColor, PickerFontColor, ref _fontColorHex, FontColorSwatch, ref _handlerFontColor);

        private void BtnPickShadowColor_Click(object sender, RoutedEventArgs e)
            => OpenColorPicker(PopupShadowColor, PickerShadowColor, ref _shadowColorHex, ShadowColorSwatch, ref _handlerShadowColor);

        private void BtnPickBorderColor_Click(object sender, RoutedEventArgs e)
            => OpenColorPicker(PopupBorderColor, PickerBorderColor, ref _borderColorHex, BorderColorSwatch, ref _handlerBorderColor);

        private void BtnPickBgColor_Click(object sender, RoutedEventArgs e)
            => OpenColorPicker(PopupBgColor, PickerBgColor, ref _bgColorHex, BgColorSwatch, ref _handlerBgColor);

        /// <summary>
        /// 開啟調色盤 Popup。套用後更新色塊背景與對應的 hex 字串欄位。
        /// </summary>
        private void OpenColorPicker(System.Windows.Controls.Primitives.Popup popup,
                                     ColorPickerPopup picker,
                                     ref string hexField,
                                     Border swatch,
                                     ref Action<Color>? storedHandler)
        {
            // 先抓當前值（ref 不能在 lambda 內直接捕捉，存成區域變數）
            string currentHex = hexField;

            Color? init = TryParseColorFromHex(currentHex);
            if (init.HasValue) picker.SetColorInternal(init.Value);

            if (storedHandler != null)
                picker.ColorConfirmed -= storedHandler;

            // 用 setter action 迴避 ref 無法進 lambda 的限制
            Action<string> setHex = GetHexSetter(swatch, ref hexField);

            storedHandler = (Color c) =>
            {
                // WPF BrushConverter 使用 #AARRGGBB 格式
                string hex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                setHex(hex);
                swatch.Background = new SolidColorBrush(c);
                popup.IsOpen      = false;
                RefreshMiniPreview();
            };

            picker.ColorConfirmed += storedHandler;
            popup.IsOpen = true;
        }

        // ref 欄位無法進 lambda，透過比對 swatch 物件取得對應的 setter
        private Action<string> GetHexSetter(Border swatch, ref string _)
        {
            if (swatch == FontColorSwatch)   return v => _fontColorHex   = v;
            if (swatch == ShadowColorSwatch) return v => _shadowColorHex = v;
            if (swatch == BorderColorSwatch) return v => _borderColorHex = v;
            if (swatch == BgColorSwatch)     return v => _bgColorHex     = v;
            return _ => { };
        }

        private static Color? TryParseColorFromHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.Trim().TrimStart('#');
            try
            {
                if (hex.Length == 6)
                    // #RRGGBB → 不透明
                    return Color.FromArgb(255,
                        Convert.ToByte(hex[0..2], 16),
                        Convert.ToByte(hex[2..4], 16),
                        Convert.ToByte(hex[4..6], 16));
                if (hex.Length == 8)
                    // #AARRGGBB（WPF 標準，BrushConverter 使用的格式）
                    return Color.FromArgb(
                        Convert.ToByte(hex[0..2], 16),
                        Convert.ToByte(hex[2..4], 16),
                        Convert.ToByte(hex[4..6], 16),
                        Convert.ToByte(hex[6..8], 16));
            }
            catch { }
            return null;
        }

        // ── 仍保留 SelectionChanged / CheckBox / Slider 用於字體選項的即時預覽
        private void StyleControl_Changed(object sender, SelectionChangedEventArgs e) => RefreshMiniPreview();

        private void StyleControl_TextChanged(object sender, TextChangedEventArgs e) => RefreshMiniPreview();

        private void StyleControl_CheckChanged(object sender, RoutedEventArgs e) => RefreshMiniPreview();

        private void StyleControl_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblStrokeValue != null)
                LblStrokeValue.Text = ((int)SliderStroke.Value).ToString();
            RefreshMiniPreview();
        }

        private void RefreshMiniPreview()
        {
            if (TxtMiniPreview == null || MiniPreviewBorder == null) return;
            var style = ReadStyleFromUI();
            style.Text = string.IsNullOrWhiteSpace(TxtSubtitle?.Text) ? "字卡預覽" : TxtSubtitle.Text;
            ApplyStyleToTextBlock(TxtMiniPreview, MiniPreviewBorder, style, scaleDown: true);
        }

        //  字卡設定面板 ── 讀取 UI
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
                FontColor       = _fontColorHex,
                ShadowColor     = _shadowColorHex,
                StrokeWidth     = SliderStroke?.Value   ?? 0,
                StrokeColor     = _borderColorHex,
                BackgroundColor = _bgColorHex,
                Position        = (ComboPosition?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "底部置中（Bottom Center）",
                StartSeconds    = startSec,
                DurationSeconds = durSec,
            };
        }


        //  字卡設定面板 ── 套用字卡

        private void BtnAddText_Click(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.Source == null || !VideoPlayer.NaturalDuration.HasTimeSpan)
            { MessageBox.Show("請先匯入影片！", "提示"); return; }

            if (string.IsNullOrWhiteSpace(TxtSubtitle.Text))
            { MessageBox.Show("請先輸入字卡內容！", "提示"); return; }

            var style  = ReadStyleFromUI();
            style.Text = TxtSubtitle.Text;

            // 字卡從時間軸游標位置開始
            style.StartSeconds = timelineCurrentSeconds;

            // 自動選擇：找出在游標時間點上「沒有字卡」的軌道（最小 index 優先）
            int targetTrack = -1;
            for (int i = 0; i < SUBTITLE_TRACK_COUNT; i++)
            {
                bool occupied = subtitleList.Any(s => s.TrackIndex == i &&
                    style.StartSeconds < s.StartSeconds + s.DurationSeconds &&
                    style.StartSeconds + style.DurationSeconds > s.StartSeconds);
                if (!occupied) { targetTrack = i; break; }
            }
            if (targetTrack == -1)
            {
                MessageBox.Show("目前游標時間點三條字卡軌都已有字卡，\n請先移動游標到空白處再新增。", "提示");
                return;
            }
            style.TrackIndex = targetTrack;

            if (TxtSubStartTime != null)
                TxtSubStartTime.Text = style.StartSeconds.ToString("F1");

            var cmd = new AddSubtitleCommand(subtitleList, style, () => {
                RedrawSubtitleCards();
                RebuildOverlayCards();
            });
            commandHistory.ExecuteCommand(cmd);

            pendingSubtitleText = style.Text;
            TxtSubtitle.Text    = "";
        }

        // 更新所選字卡樣式
        private void BtnUpdateStyle_Click(object sender, RoutedEventArgs e)
        {
            if (selectedSubtitleCard == null)
            { MessageBox.Show("請先在時間軸上點選一張字卡！", "提示"); return; }

            var tag = selectedSubtitleCard.Tag as SubtitleStyle;
            if (tag == null) return;

            var newStyle = ReadStyleFromUI();
            newStyle.Text            = tag.Text;
            newStyle.StartSeconds    = tag.StartSeconds;
            newStyle.DurationSeconds = tag.DurationSeconds;
            newStyle.TrackIndex      = tag.TrackIndex;  // 保留原軌道

            int idx = subtitleList.IndexOf(tag);
            if (idx >= 0)
            {
                var cmd = new UpdateSubtitleCommand(subtitleList, tag, newStyle, idx, () => {
                    RedrawSubtitleCards();
                    RebuildOverlayCards();
                });
                commandHistory.ExecuteCommand(cmd);
            }

            MessageBox.Show("字卡樣式已更新！", "完成");
        }

        //  字卡 Overlay（疊在影片上，可拖曳）
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
        private void RedrawSubtitleCards()
        {
            foreach (var canvas in SubtitleTrackCanvases)
                canvas.Children.Clear();
            selectedSubtitleCard = null;

            var trackColors = new[]
            {
                Color.FromRgb(80,  50, 130),  // 軌 1：紫
                Color.FromRgb(40,  90, 140),  // 軌 2：藍紫
                Color.FromRgb(50, 110,  80),  // 軌 3：綠
            };
            var trackBorderColors = new[]
            {
                Color.FromRgb(40, 25, 70),
                Color.FromRgb(20, 50, 80),
                Color.FromRgb(25, 60, 40),
            };

            foreach (var s in subtitleList)
            {
                double leftX  = s.StartSeconds * PIXELS_PER_SECOND;
                double width  = Math.Max(s.DurationSeconds * PIXELS_PER_SECOND, 10);
                int    ti     = Math.Clamp(s.TrackIndex, 0, SUBTITLE_TRACK_COUNT - 1);
                var    canvas = SubtitleTrackCanvases[ti];

                var container = new Grid
                {
                    Width      = width,
                    Height     = 32,
                    Tag        = s,
                    Cursor     = Cursors.SizeAll,
                    Background = Brushes.Transparent
                };

                var card = new Border
                {
                    Background          = new SolidColorBrush(trackColors[ti]),
                    CornerRadius        = new CornerRadius(4),
                    BorderBrush         = new SolidColorBrush(trackBorderColors[ti]),
                    BorderThickness     = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag                 = s,
                    ToolTip             = $"[軌{ti + 1}] {s.Text}\n{s.StartSeconds:F1}s → {s.StartSeconds + s.DurationSeconds:F1}s"
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

                var leftHandle = new Thumb
                {
                    Width               = 8,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Cursor              = Cursors.SizeWE,
                    Background          = Brushes.White,
                    Opacity             = 0
                };
                leftHandle.DragDelta   += UnifiedLeftHandle_DragDelta;
                leftHandle.DragStarted += (_, _) => { VideoPlayer.Pause(); playheadTimer.Stop(); };

                var rightHandle = new Thumb
                {
                    Width               = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Cursor              = Cursors.SizeWE,
                    Background          = Brushes.White,
                    Opacity             = 0
                };
                rightHandle.DragDelta   += UnifiedRightHandle_DragDelta;
                rightHandle.DragStarted += (_, _) => { VideoPlayer.Pause(); playheadTimer.Stop(); };

                container.Children.Add(card);
                container.Children.Add(leftHandle);
                container.Children.Add(rightHandle);

                container.MouseDown         += TrackItem_MouseDown;
                container.MouseMove         += TrackItem_MouseMove;
                container.MouseLeftButtonUp += TrackItem_MouseUp;

                Canvas.SetLeft(container, leftX);
                Canvas.SetTop(container,  4);
                canvas.Children.Add(container);
            }
        }
        // 點擊字卡時，把樣式填回 UI
        private void LoadStyleToUI(SubtitleStyle s)
        {
            TxtSubtitle.Text        = s.Text;
            TxtFontSize.Text        = s.FontSize.ToString();
            SliderStroke.Value      = s.StrokeWidth;
            ChkItalic.IsChecked     = s.IsItalic;
            ChkUnderline.IsChecked  = s.IsUnderline;
            TxtSubStartTime.Text    = s.StartSeconds.ToString("F1");
            TxtSubDuration.Text     = s.DurationSeconds.ToString("F1");
            
            // 更新字串欄位並同步色塊背景
            void ApplyColor(ref string field, Border swatch, string hex)
            {
                field = hex;
                var c = TryParseColorFromHex(hex);
                if (c.HasValue) swatch.Background = new SolidColorBrush(c.Value);
            }
            ApplyColor(ref _fontColorHex,   FontColorSwatch,   s.FontColor);
            ApplyColor(ref _shadowColorHex, ShadowColorSwatch, s.ShadowColor);
            ApplyColor(ref _borderColorHex, BorderColorSwatch, s.StrokeColor);
            ApplyColor(ref _bgColorHex,     BgColorSwatch,     s.BackgroundColor);

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

        // 影像軌繪製
        private void AddVideoToTimeline(double durationInSeconds)
        {
            double totalWidth = durationInSeconds * PIXELS_PER_SECOND;

            // 1. 初始化畫布與寬度
             VideoTrackCanvas.Width     = totalWidth + 100;
            foreach (var c in SubtitleTrackCanvases) c.Width = totalWidth + 100;
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
            VideoTrackCanvas.Children.Add(segmentGrid);
        }

        private Grid CreateSegmentUI(VideoSegmentData data)
        {
            Grid container = new Grid
            {
                Width = data.Duration * PIXELS_PER_SECOND,
                Height = 35,
                Tag = data,
                Cursor = Cursors.SizeAll
            };

            var rect = new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Fill = new SolidColorBrush(Color.FromRgb(0, 122, 204)),

                Stroke = new SolidColorBrush(Color.FromRgb(0, 60, 110)),
                StrokeThickness = 1,

                RadiusX = 3,
                RadiusY = 3
            };
            container.Children.Add(rect);

            var leftHandle = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.SizeWE,
                Background = Brushes.White,
                Opacity = 0
            };
            leftHandle.DragDelta += UnifiedLeftHandle_DragDelta;
            leftHandle.DragStarted += (s, e) =>
            {
                VideoPlayer.Pause();
                playheadTimer.Stop();
            };

            var rightHandle = new Thumb
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.SizeWE,
                Background = Brushes.White,
                Opacity = 0
            };
            rightHandle.DragDelta += UnifiedRightHandle_DragDelta;
            rightHandle.DragStarted += (s, e) =>
            {
                VideoPlayer.Pause();
                playheadTimer.Stop();
            };

            container.Children.Add(leftHandle);
            container.Children.Add(rightHandle);

            // 統一綁定共用的拖曳事件
            container.MouseDown += TrackItem_MouseDown;
            container.MouseMove += TrackItem_MouseMove;
            container.MouseLeftButtonUp += TrackItem_MouseUp;

            // 設定初始位置
            Canvas.SetLeft(container, data.TimelineStart * PIXELS_PER_SECOND);
            Canvas.SetTop(container, 45);

            return container;
        }
        // 處理左側拖動：非破壞性，只移動標記，可以隨時還原       
        private void VideoTrackCanvas_MouseDown(object sender, MouseButtonEventArgs e) => ClearSelection();
        private void SplitSegment(VideoSegmentData segment, double splitPointSeconds)
        {
            // 【新增】：防呆保護。確保切割點在片段中間，左右至少保留 0.5 秒的長度
            if (splitPointSeconds <= segment.TimelineStart + 0.5 ||
                splitPointSeconds >= segment.TimelineStart + segment.Duration - 0.5)
            {
                MessageBox.Show("切割點太靠近邊緣，請選擇片段中間的位置下刀！", "提示");
                return;
            }
            var cmd = new SplitVideoCommand(
                videoSegments,
                VideoTrackCanvas,
                segment,
                splitPointSeconds,
                PIXELS_PER_SECOND,
                CreateSegmentUI, // 傳遞產生 UI 的方法
                ClearSelection   // 傳遞清除選取框的方法
            );

            // 交由歷史紀錄管理員執行
            commandHistory.ExecuteCommand(cmd);

            // 影片跳轉保持在指令外部執行，因為這屬於播放器即時預覽行為，不需納入 Undo 歷史
            VideoPlayer.Position = TimeSpan.FromSeconds(splitPointSeconds);
        }

        private void ClearSelection()
        {
            Keyboard.ClearFocus();
            this.Focus();
            selectedSubtitleCard = null;

            foreach (var child in VideoTrackCanvas.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var inner in grid.Children)
                    {
                        if (inner is System.Windows.Shapes.Rectangle r)
                        {
                            r.Stroke = new SolidColorBrush(Color.FromRgb(0, 60, 110));
                            r.StrokeThickness = 1;
                        }
                        if (inner is Thumb t) t.Opacity = 0;
                    }
                }
            }

            var trackBorderColors = new[]
            {
                Color.FromRgb(40, 25, 70),
                Color.FromRgb(20, 50, 80),
                Color.FromRgb(25, 60, 40),
            };
            for (int ti = 0; ti < SUBTITLE_TRACK_COUNT; ti++)
            {
                foreach (var child in SubtitleTrackCanvases[ti].Children)
                {
                    if (child is Grid grid)
                    {
                        foreach (var inner in grid.Children)
                        {
                            if (inner is Border b)
                            {
                                b.BorderBrush     = new SolidColorBrush(trackBorderColors[ti]);
                                b.BorderThickness = new Thickness(1);
                            }
                            if (inner is Thumb t) t.Opacity = 0;
                        }
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
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            // 1. 優先刪除選取的「字卡」
            if (selectedSubtitleCard != null)
            {
                if (selectedSubtitleCard.Tag is SubtitleStyle s)
                {
                    int idx = subtitleList.IndexOf(s);
                    var cmd = new DeleteSubtitleCommand(subtitleList, s, idx, () => {
                        selectedSubtitleCard = null; // 清空選取狀態
                        RedrawSubtitleCards();
                        RebuildOverlayCards();
                    });
                    commandHistory.ExecuteCommand(cmd);
                }
                return;
            }

            // 2. 【新增】：刪除選取的「影片片段」
            if (selectedSegment != null)
            {
                // 確保不會把最後一個片段刪掉（如果是團隊需求，可以拿掉這個限制）
                // if (videoSegments.Count <= 1)
                // { MessageBox.Show("必須至少保留一個影片片段！", "提示"); return; }

                var cmd = new DeleteVideoSegmentCommand(videoSegments, VideoTrackCanvas, selectedSegment, ClearSelection);
                commandHistory.ExecuteCommand(cmd);

                selectedSegment = null; // 清空選取狀態
                return;
            }

            // 3. 如果畫面上什麼都沒選取，才詢問是否要「清空全部」
            if (string.IsNullOrEmpty(currentVideoPath))
            { MessageBox.Show("目前沒有載入任何影片片段。", "提示"); return; }

            var r = MessageBox.Show("確定要從時間軸移除整部影片嗎？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes)
            {
                commandHistory.Clear();
                VideoTrackCanvas.Children.Clear();
                foreach (var c in SubtitleTrackCanvases) c.Children.Clear();
                subtitleList.Clear();
                videoSegments.Clear();
                currentVideoPath = "";
                currentVideoDuration = 0;
                timelineOffsetX = 0;
                timelineTransform.X = 0;
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
                playheadTimer.Stop();
                PlayheadLine.Visibility = Visibility.Collapsed;
                SubtitleOverlayCanvas.Children.Clear();
                overlayBorderMap.Clear();
            }
        }
        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            commandHistory.Undo();
        }
        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            commandHistory.Redo();
        }
        //  影像剪輯面板
        private void BtnTrim_Click(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.Source == null || selectedSegment == null) return;

            if (!double.TryParse(TxtStartTime.Text, out double st) ||
                !double.TryParse(TxtEndTime.Text,   out double et))
            { MessageBox.Show("請輸入有效的時間！", "錯誤"); return; }

            if (st < 0 || et <= st || et > currentVideoDuration)
            { MessageBox.Show($"時間不合法（總長：{currentVideoDuration:F1}秒）", "錯誤"); return; }

            StoreTrimSettings(st, et);
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

        //  匯出輔助
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

            var args = FfmpegArgumentBuilder.Build(currentVideoPath, settings, currentVideoHasAudio);  // 無聲影片修正：告知 Builder 是否有音訊

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
        //  統一的時間軸拖曳邏輯 (滑鼠點擊、移動、放開)
        private void TrackItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid container || container.Tag is not ITimelineTrackItem itemData) return;

            // 剪刀工具處理 (限定影像軌)
            if (currentTool == EditorTool.Scissors && itemData is VideoSegmentData segment)
            {
                double clickX = e.GetPosition(VideoTrackCanvas).X;
                SplitSegment(segment, clickX / PIXELS_PER_SECOND);
                e.Handled = true;
                return;
            }

            ClearSelection();

            isDraggingTrackItem  = true;
            draggedTrackItemUI   = container;
            draggedTrackItemData = itemData;
            trackDragStartMouseX = e.GetPosition(this).X;
            trackDragStartLeft   = Canvas.GetLeft(container);

            if (itemData is VideoSegmentData)
            {
                preDragVideoOrder  = new List<VideoSegmentData>(videoSegments);
                preDragVideoStarts = videoSegments.ToDictionary(s => s.Id, s => s.TimelineStartSeconds);
            }
            else if (itemData is SubtitleStyle sub0)
            {
                draggedSubtitleSourceTrack = sub0.TrackIndex;
                preDragSubtitleOrder  = new List<SubtitleStyle>(subtitleList);
                preDragSubtitleStarts = subtitleList.ToDictionary(s => s, s => s.TimelineStartSeconds);
            }

            if (itemData is VideoSegmentData video)
            {
                selectedSegment = video;
                var rect = container.Children.OfType<System.Windows.Shapes.Rectangle>().FirstOrDefault();
                if (rect != null) { rect.Stroke = Brushes.White; rect.StrokeThickness = 2; }
                foreach (var t in container.Children.OfType<Thumb>()) t.Opacity = 0.5;

                TxtStartTime.Text = video.TimelineStartSeconds.ToString("F1");
                TxtEndTime.Text   = (video.TimelineStartSeconds + video.TimelineDurationSeconds).ToString("F1");
            }
            else if (itemData is SubtitleStyle subtitle)
            {
                var card = container.Children.OfType<Border>().FirstOrDefault();
                if (card != null) { card.BorderBrush = Brushes.White; card.BorderThickness = new Thickness(2); selectedSubtitleCard = card; }
                foreach (var t in container.Children.OfType<Thumb>()) t.Opacity = 0.6;
                LoadStyleToUI(subtitle);
            }

            container.CaptureMouse();
            e.Handled = true;
        }
        private void TrackItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingTrackItem || draggedTrackItemUI == null || draggedTrackItemData == null) return;

            double delta = e.GetPosition(this).X - trackDragStartMouseX;
            double newLeft = trackDragStartLeft + delta;

            // 預設邊界：左邊至少為 0 (不能拖到時間軸外面)
            double minLeft = 0;
            // 右邊界放寬，允許拖曳到現有影片的最尾端之後
            double maxLeft = Math.Max(currentVideoDuration, videoSegments.Count > 0 ? videoSegments.Max(s => s.TimelineStartSeconds + s.Duration) : 0) * PIXELS_PER_SECOND;

            // 移除原本的防撞邏輯，允許自由穿梭重疊
            newLeft = Math.Clamp(newLeft, minLeft, maxLeft);

            Canvas.SetLeft(draggedTrackItemUI, newLeft);
            double newStartSeconds = newLeft / PIXELS_PER_SECOND;

            // 統一更新共用介面資料
            draggedTrackItemData.TimelineStartSeconds = newStartSeconds;

            // 針對個別物件更新面板文字框
            if (draggedTrackItemData is VideoSegmentData videoData)
            {
                TxtStartTime.Text = newStartSeconds.ToString("F1");
                TxtEndTime.Text = (newStartSeconds + videoData.TimelineDurationSeconds).ToString("F1");
            }
            else if (draggedTrackItemData is SubtitleStyle subtitle)
            {
                if (TxtSubStartTime != null) TxtSubStartTime.Text = newStartSeconds.ToString("F1");
            }
        }
        private void TrackItem_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDraggingTrackItem || draggedTrackItemUI == null || draggedTrackItemData == null) return;

            isDraggingTrackItem = false;
            draggedTrackItemUI.ReleaseMouseCapture();

            double finalLeft   = Canvas.GetLeft(draggedTrackItemUI);
            bool actuallyMoved = Math.Abs(finalLeft - trackDragStartLeft) > 1.0;

            if (actuallyMoved)
            {
                double newStartSeconds = finalLeft / PIXELS_PER_SECOND;

                if (draggedTrackItemData is VideoSegmentData)
                {
                    ResolveVideoOverlaps();
                    VideoPlayer.Position = TimeSpan.FromSeconds(newStartSeconds);
                    if (preDragVideoOrder != null && preDragVideoStarts != null)
                    {
                        var postOrder  = new List<VideoSegmentData>(videoSegments);
                        var postStarts = videoSegments.ToDictionary(s => s.Id, s => s.TimelineStartSeconds);
                        var cmd = new MoveVideoTrackCommand(videoSegments, preDragVideoOrder, preDragVideoStarts, postOrder, postStarts, RefreshVideoTrackUI);
                        commandHistory.ExecuteCommand(cmd);
                    }
                }
                else if (draggedTrackItemData is SubtitleStyle subtitle)
                {
                    // 跨軌偵測：依滑鼠 Y 座標判斷目標軌道
                    int targetTrack = draggedSubtitleSourceTrack;
                    Point mousePos  = e.GetPosition(this);
                    for (int ti = 0; ti < SUBTITLE_TRACK_COUNT; ti++)
                    {
                        var cv        = SubtitleTrackCanvases[ti];
                        var winOrigin = this.PointFromScreen(cv.PointToScreen(new Point(0, 0)));
                        if (mousePos.Y >= winOrigin.Y - 8 && mousePos.Y <= winOrigin.Y + cv.ActualHeight + 8)
                        {
                            targetTrack = ti;
                            break;
                        }
                    }

                    // 跨軌：只更新 TrackIndex，資料仍在同一個 subtitleList
                    if (targetTrack != draggedSubtitleSourceTrack)
                        subtitle.TrackIndex = targetTrack;

                    ResolveSubtitleOverlaps();

                    // 原版的 Undo/Redo 指令，完整保留
                    if (preDragSubtitleOrder != null && preDragSubtitleStarts != null)
                    {
                        var postOrder  = new List<SubtitleStyle>(subtitleList);
                        var postStarts = subtitleList.ToDictionary(s => s, s => s.TimelineStartSeconds);
                        var cmd = new MoveSubtitleTrackCommand(subtitleList, preDragSubtitleOrder, preDragSubtitleStarts, postOrder, postStarts, RefreshSubtitleTrackUI);
                        commandHistory.ExecuteCommand(cmd);
                    }

                    // 跨軌後重繪，確保字卡出現在正確的 Canvas 軌道上
                    RedrawSubtitleCards();

                    var card = draggedTrackItemUI.Children.OfType<Border>().FirstOrDefault();
                    if (card != null)
                        card.ToolTip = $"[軌{targetTrack + 1}] {subtitle.Text}\n{subtitle.TimelineStartSeconds:F1}s → {subtitle.TimelineStartSeconds + subtitle.TimelineDurationSeconds:F1}s";
                }
            }

            draggedTrackItemUI         = null;
            draggedTrackItemData       = null;
            draggedSubtitleSourceTrack = -1;
            e.Handled = true;
        }
        private void ResolveVideoOverlaps()
        {
            videoSegments = videoSegments.OrderBy(s => s.TimelineStartSeconds + (s.TimelineDurationSeconds / 2.0)).ToList();

            double nextValidStartSec = 0;
            foreach (var seg in videoSegments)
            {
                if (seg.TimelineStartSeconds < nextValidStartSec)
                {
                    seg.TimelineStartSeconds = nextValidStartSec;
                    if (seg.UIElement != null)
                    {
                        Canvas.SetLeft(seg.UIElement, seg.TimelineStartSeconds * PIXELS_PER_SECOND);
                    }
                }
                nextValidStartSec = seg.TimelineStartSeconds + seg.TimelineDurationSeconds;
            }

            double maxEndSeconds = nextValidStartSec;
            if (maxEndSeconds > currentVideoDuration)
            {
                double newWidth = maxEndSeconds * PIXELS_PER_SECOND + 200;
                if (newWidth > VideoTrackCanvas.Width)
                {
                    VideoTrackCanvas.Width = newWidth;
                    foreach (var c in SubtitleTrackCanvases) c.Width = newWidth;
                    TimeRulerCanvas.Width = newWidth;
                    TimelineContentStack.Width = newWidth;
                    DrawTimeRuler(maxEndSeconds);
                }
            }

            if (selectedSegment != null)
            {
                TxtStartTime.Text = selectedSegment.TimelineStartSeconds.ToString("F1");
                TxtEndTime.Text = (selectedSegment.TimelineStartSeconds + selectedSegment.TimelineDurationSeconds).ToString("F1");
            }
        }
        private void ResolveSubtitleOverlaps()
        {
            // 對每條軌道分別排序並解決重疊，資料仍在同一個 subtitleList
            for (int ti = 0; ti < SUBTITLE_TRACK_COUNT; ti++)
            {
                var trackItems = subtitleList.Where(s => s.TrackIndex == ti)
                                             .OrderBy(s => s.StartSeconds + s.DurationSeconds / 2.0)
                                             .ToList();
                double nextValid = 0;
                foreach (var sub in trackItems)
                {
                    if (sub.StartSeconds < nextValid)
                        sub.StartSeconds = nextValid;
                    nextValid = sub.StartSeconds + sub.DurationSeconds;
                }
            }

            double maxEnd = subtitleList.Count > 0
                ? subtitleList.Max(s => s.StartSeconds + s.DurationSeconds) : 0;
            if (maxEnd > currentVideoDuration)
            {
                double newWidth = maxEnd * PIXELS_PER_SECOND + 200;
                if (newWidth > VideoTrackCanvas.Width)
                {
                    VideoTrackCanvas.Width     = newWidth;
                    foreach (var c in SubtitleTrackCanvases) c.Width = newWidth;
                    TimeRulerCanvas.Width      = newWidth;
                    TimelineContentStack.Width = newWidth;
                    DrawTimeRuler(maxEnd);
                }
            }

            // 更新所有字卡 UI 位置
            foreach (var canvas in SubtitleTrackCanvases)
                foreach (var child in canvas.Children)
                    if (child is Grid g && g.Tag is SubtitleStyle st)
                        Canvas.SetLeft(g, st.StartSeconds * PIXELS_PER_SECOND);

            if (selectedSubtitleCard?.Tag is SubtitleStyle sel && TxtSubStartTime != null)
                TxtSubStartTime.Text = sel.StartSeconds.ToString("F1");
        }
        private void UnifiedLeftHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || thumb.Parent is not Grid container) return;
            if (container.Tag is not ITimelineTrackItem itemData) return;

            double currentLeft = Canvas.GetLeft(container);
            double deltaX = e.HorizontalChange;

            if (itemData is VideoSegmentData videoBounds)
            {
                double deltaT = deltaX / PIXELS_PER_SECOND;
                if (videoBounds.InternalOffset + deltaT < 0)
                {
                    deltaT = -videoBounds.InternalOffset;
                    deltaX = deltaT * PIXELS_PER_SECOND;
                }
            }

            double newLeft = currentLeft + deltaX;
            double newWidth = container.Width - deltaX;

            if (itemData is VideoSegmentData videoDataCol)
            {
                double minLeft = 0;
                foreach (var other in videoSegments)
                {
                    if (other == videoDataCol) continue;
                    if (other.TimelineStartSeconds <= videoDataCol.TimelineStartSeconds)
                    {
                        double otherRight = (other.TimelineStartSeconds + other.TimelineDurationSeconds) * PIXELS_PER_SECOND;
                        if (otherRight > minLeft) minLeft = otherRight;
                    }
                }
                if (newLeft < minLeft)
                {
                    newLeft = minLeft;
                    newWidth = currentLeft + container.Width - newLeft; // 重新計算寬度
                }
            }

            if (newWidth < 10 || newLeft < 0) return;
            double actualDeltaSeconds = (newLeft - currentLeft) / PIXELS_PER_SECOND;

            Canvas.SetLeft(container, newLeft);
            container.Width = newWidth;

            itemData.TimelineStartSeconds = newLeft / PIXELS_PER_SECOND;
            itemData.TimelineDurationSeconds = newWidth / PIXELS_PER_SECOND;

            if (itemData is VideoSegmentData videoData)
            {
                videoData.InternalOffset += actualDeltaSeconds; // 影片起點跟著左把手移動

                TxtStartTime.Text = videoData.TimelineStartSeconds.ToString("F1");
                TxtEndTime.Text = (videoData.TimelineStartSeconds + videoData.TimelineDurationSeconds).ToString("F1");
                SyncVideoPlayerToTimeline(videoData.TimelineStartSeconds);
            }
            else if (itemData is SubtitleStyle subtitle)
            {
                if (TxtSubStartTime != null) TxtSubStartTime.Text = subtitle.TimelineStartSeconds.ToString("F1");
                if (TxtSubDuration != null) TxtSubDuration.Text = subtitle.TimelineDurationSeconds.ToString("F1");
                SyncVideoPlayerToTimeline(subtitle.TimelineStartSeconds);
            }
        }
        private void UnifiedRightHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || thumb.Parent is not Grid container) return;
            if (container.Tag is not ITimelineTrackItem itemData) return;

            double deltaX = e.HorizontalChange;

            if (itemData is VideoSegmentData videoBounds)
            {
                double deltaT = deltaX / PIXELS_PER_SECOND;
                if (videoBounds.InternalOffset + videoBounds.Duration + deltaT > currentVideoDuration)
                {
                    // 限制最多只能拉到原影片的結尾
                    deltaT = currentVideoDuration - (videoBounds.InternalOffset + videoBounds.Duration);
                    deltaX = deltaT * PIXELS_PER_SECOND;
                }
            }

            double newWidth = container.Width + deltaX;
            double currentLeft = Canvas.GetLeft(container);
            double maxEnd = currentVideoDuration * PIXELS_PER_SECOND;

            if (itemData is VideoSegmentData videoCol)
            {
                foreach (var other in videoSegments)
                {
                    if (other == videoCol) continue;
                    if (other.TimelineStartSeconds >= videoCol.TimelineStartSeconds)
                    {
                        double otherLeft = other.TimelineStartSeconds * PIXELS_PER_SECOND;
                        if (otherLeft < maxEnd) maxEnd = otherLeft;
                    }
                }
            }

            if ((currentLeft + newWidth) > maxEnd)
            {
                newWidth = maxEnd - currentLeft;
            }

            if (newWidth < 10) return;

            container.Width = newWidth;
            itemData.TimelineDurationSeconds = newWidth / PIXELS_PER_SECOND;

            if (itemData is VideoSegmentData videoData)
            {
                TxtEndTime.Text = (videoData.TimelineStartSeconds + videoData.TimelineDurationSeconds).ToString("F1");
                SyncVideoPlayerToTimeline(videoData.TimelineStartSeconds + videoData.TimelineDurationSeconds - 0.01);
            }
            else if (itemData is SubtitleStyle subtitle)
            {
                if (TxtSubDuration != null) TxtSubDuration.Text = subtitle.TimelineDurationSeconds.ToString("F1");
                SyncVideoPlayerToTimeline(subtitle.TimelineStartSeconds + subtitle.TimelineDurationSeconds - 0.01);
            }
        }
        public void RefreshVideoTrackUI()
        {
            foreach (var seg in videoSegments)
                if (seg.UIElement != null)
                    Canvas.SetLeft(seg.UIElement, seg.TimelineStartSeconds * PIXELS_PER_SECOND);

            if (selectedSegment != null)
            {
                TxtStartTime.Text = selectedSegment.TimelineStartSeconds.ToString("F1");
                TxtEndTime.Text = (selectedSegment.TimelineStartSeconds + selectedSegment.TimelineDurationSeconds).ToString("F1");
            }
        }
        public void RefreshSubtitleTrackUI()
        {
            foreach (var canvas in SubtitleTrackCanvases)
                foreach (var child in canvas.Children)
                    if (child is Grid container && container.Tag is SubtitleStyle style)
                        Canvas.SetLeft(container, style.StartSeconds * PIXELS_PER_SECOND);

            if (selectedSubtitleCard?.Tag is SubtitleStyle sel && TxtSubStartTime != null)
                TxtSubStartTime.Text = sel.StartSeconds.ToString("F1");
        }
    }
    public interface IEditorCommand
    {
        void Execute();
        void Undo();
    }
    public class CommandHistory
    {
        private Stack<IEditorCommand> undoStack = new();
        private Stack<IEditorCommand> redoStack = new();

        public void ExecuteCommand(IEditorCommand command)
        {
            command.Execute();
            undoStack.Push(command);
            redoStack.Clear(); // 執行新動作時，清空重做堆疊
        }

        public void Undo()
        {
            if (undoStack.Count > 0)
            {
                var command = undoStack.Pop();
                command.Undo();
                redoStack.Push(command);
            }
        }

        public void Redo()
        {
            if (redoStack.Count > 0)
            {
                var command = redoStack.Pop();
                command.Execute();
                undoStack.Push(command);
            }
        }

        public void Clear()
        {
            undoStack.Clear();
            redoStack.Clear();
        }
    }
    public class AddSubtitleCommand : IEditorCommand
    {
        private List<SubtitleStyle> _list;
        private SubtitleStyle _subtitle;
        private Action _refreshUI;
        public AddSubtitleCommand(List<SubtitleStyle> list, SubtitleStyle subtitle, Action refreshUI)
        {
            _list = list;
            _subtitle = subtitle;
            _refreshUI = refreshUI;
        }
        public void Execute() { _list.Add(_subtitle); _refreshUI(); }
        public void Undo() { _list.Remove(_subtitle); _refreshUI(); }
    }
    // 刪除影片片段的專用指令 (支援 Undo / Redo)
    public class DeleteVideoSegmentCommand : IEditorCommand
    {
        private List<MainWindow.VideoSegmentData> _videoSegments;
        private Canvas _videoTrackCanvas;
        private MainWindow.VideoSegmentData _segmentToDelete;
        private Action _clearSelection;

        public DeleteVideoSegmentCommand(
            List<MainWindow.VideoSegmentData> videoSegments,
            Canvas videoTrackCanvas,
            MainWindow.VideoSegmentData segmentToDelete,
            Action clearSelection)
        {
            _videoSegments = videoSegments;
            _videoTrackCanvas = videoTrackCanvas;
            _segmentToDelete = segmentToDelete;
            _clearSelection = clearSelection;
        }

        public void Execute()
        {
            // 執行刪除前先清空選取框
            _clearSelection();

            // 從清單與畫布中移除該片段
            _videoSegments.Remove(_segmentToDelete);
            if (_segmentToDelete.UIElement != null)
            {
                _videoTrackCanvas.Children.Remove(_segmentToDelete.UIElement);
            }
        }

        public void Undo()
        {
            _clearSelection();

            // 復原時加回清單與畫布 (UI位置會依照原本的 Canvas.Left 自動對齊)
            _videoSegments.Add(_segmentToDelete);
            if (_segmentToDelete.UIElement != null)
            {
                _videoTrackCanvas.Children.Add(_segmentToDelete.UIElement);
            }
        }
    }
    public class SplitVideoCommand : IEditorCommand
    {
        private List<MainWindow.VideoSegmentData> _videoSegments;
        private Canvas _videoTrackCanvas;
        private MainWindow.VideoSegmentData _originalSegment;
        private MainWindow.VideoSegmentData _newSegment;

        private double _originalDuration;
        private double _splitPointSeconds;
        private double _pixelsPerSecond;

        private Func<MainWindow.VideoSegmentData, Grid> _createUIFunc;
        private Action _clearSelection;

        public SplitVideoCommand(
            List<MainWindow.VideoSegmentData> videoSegments,
            Canvas videoTrackCanvas,
            MainWindow.VideoSegmentData originalSegment,
            double splitPointSeconds,
            double pixelsPerSecond,
            Func<MainWindow.VideoSegmentData, Grid> createUIFunc,
            Action clearSelection)
        {
            _videoSegments = videoSegments;
            _videoTrackCanvas = videoTrackCanvas;
            _originalSegment = originalSegment;
            _splitPointSeconds = splitPointSeconds;
            _pixelsPerSecond = pixelsPerSecond;
            _createUIFunc = createUIFunc;
            _clearSelection = clearSelection;

            // 記錄分割前的原始長度，以便 Undo 時還原
            _originalDuration = originalSegment.Duration;
        }

        public void Execute()
        {
            _clearSelection(); // 執行前先清空選取狀態，避免 UI 殘留白框

            // 如果是第一次執行，計算並產生新的片段資料與 UI
            if (_newSegment == null)
            {
                double relativeSplit = _splitPointSeconds - _originalSegment.TimelineStart;
                _newSegment = new MainWindow.VideoSegmentData
                {
                    TimelineStart = _splitPointSeconds,
                    InternalOffset = _originalSegment.InternalOffset + relativeSplit,
                    Duration = _originalSegment.Duration - relativeSplit
                };

                // 透過委派呼叫 MainWindow 裡面的 CreateSegmentUI 方法來產生方塊
                _newSegment.UIElement = _createUIFunc(_newSegment);
            }

            // 1. 修改原片段長度
            _originalSegment.Duration = _splitPointSeconds - _originalSegment.TimelineStart;
            if (_originalSegment.UIElement != null)
                _originalSegment.UIElement.Width = _originalSegment.Duration * _pixelsPerSecond;

            // 2. 加入新片段到資料清單與畫布中
            if (!_videoSegments.Contains(_newSegment))
                _videoSegments.Add(_newSegment);

            if (!_videoTrackCanvas.Children.Contains(_newSegment.UIElement))
                _videoTrackCanvas.Children.Add(_newSegment.UIElement);
        }

        public void Undo()
        {
            _clearSelection();

            // 1. 復原原片段的原始長度
            _originalSegment.Duration = _originalDuration;
            if (_originalSegment.UIElement != null)
                _originalSegment.UIElement.Width = _originalDuration * _pixelsPerSecond;

            // 2. 將分割出去的新片段從資料清單與畫布中移除
            _videoSegments.Remove(_newSegment);
            _videoTrackCanvas.Children.Remove(_newSegment.UIElement);
        }
    }
    public class MoveVideoTrackCommand : IEditorCommand
    {
        private List<MainWindow.VideoSegmentData> _videoSegments;
        private List<MainWindow.VideoSegmentData> _oldOrder;
        private Dictionary<Guid, double> _oldStartTimes;
        private List<MainWindow.VideoSegmentData> _newOrder;
        private Dictionary<Guid, double> _newStartTimes;
        private Action _refreshUI;

        public MoveVideoTrackCommand(
            List<MainWindow.VideoSegmentData> videoSegments,
            List<MainWindow.VideoSegmentData> oldOrder, Dictionary<Guid, double> oldStartTimes,
            List<MainWindow.VideoSegmentData> newOrder, Dictionary<Guid, double> newStartTimes,
            Action refreshUI)
        {
            _videoSegments = videoSegments;
            _oldOrder = oldOrder; _oldStartTimes = oldStartTimes;
            _newOrder = newOrder; _newStartTimes = newStartTimes;
            _refreshUI = refreshUI;
        }

        public void Execute() => ApplyState(_newOrder, _newStartTimes);
        public void Undo() => ApplyState(_oldOrder, _oldStartTimes);

        private void ApplyState(List<MainWindow.VideoSegmentData> order, Dictionary<Guid, double> startTimes)
        {
            _videoSegments.Clear();
            _videoSegments.AddRange(order);
            foreach (var seg in _videoSegments)
            {
                if (startTimes.TryGetValue(seg.Id, out double st))
                    seg.TimelineStartSeconds = st;
            }
            _refreshUI(); // 重新對齊 UI
        }
    }
    public class MoveSubtitleTrackCommand : IEditorCommand
    {
        private List<SubtitleStyle> _subtitleList;
        private List<SubtitleStyle> _oldOrder;
        private Dictionary<SubtitleStyle, double> _oldStartTimes;
        private List<SubtitleStyle> _newOrder;
        private Dictionary<SubtitleStyle, double> _newStartTimes;
        private Action _refreshUI;

        public MoveSubtitleTrackCommand(
            List<SubtitleStyle> subtitleList,
            List<SubtitleStyle> oldOrder, Dictionary<SubtitleStyle, double> oldStartTimes,
            List<SubtitleStyle> newOrder, Dictionary<SubtitleStyle, double> newStartTimes,
            Action refreshUI)
        {
            _subtitleList = subtitleList;
            _oldOrder = oldOrder; _oldStartTimes = oldStartTimes;
            _newOrder = newOrder; _newStartTimes = newStartTimes;
            _refreshUI = refreshUI;
        }

        public void Execute() => ApplyState(_newOrder, _newStartTimes);
        public void Undo() => ApplyState(_oldOrder, _oldStartTimes);

        private void ApplyState(List<SubtitleStyle> order, Dictionary<SubtitleStyle, double> startTimes)
        {
            _subtitleList.Clear();
            _subtitleList.AddRange(order);
            foreach (var sub in _subtitleList)
            {
                if (startTimes.TryGetValue(sub, out double st))
                    sub.TimelineStartSeconds = st;
            }
            _refreshUI(); // 重新對齊 UI
        }
    }
    public class DeleteSubtitleCommand : IEditorCommand
    {
        private List<SubtitleStyle> _list;
        private SubtitleStyle _subtitle;
        private int _index;
        private Action _refreshUI;
        public DeleteSubtitleCommand(List<SubtitleStyle> list, SubtitleStyle subtitle, int index, Action refreshUI)
        {
            _list = list;
            _subtitle = subtitle;
            _index = index;
            _refreshUI = refreshUI;
        }
        public void Execute() { _list.Remove(_subtitle); _refreshUI(); }
        public void Undo()
        {
            if (_index >= 0 && _index <= _list.Count) _list.Insert(_index, _subtitle);
            else _list.Add(_subtitle);
            _refreshUI();
        }
    }
    public class UpdateSubtitleCommand : IEditorCommand
    {
        private List<SubtitleStyle> _list;
        private SubtitleStyle _oldStyle;
        private SubtitleStyle _newStyle;
        private int _index;
        private Action _refreshUI;
        public UpdateSubtitleCommand(List<SubtitleStyle> list, SubtitleStyle oldStyle, SubtitleStyle newStyle, int index, Action refreshUI)
        {
            _list = list;
            _oldStyle = oldStyle;
            _newStyle = newStyle;
            _index = index;
            _refreshUI = refreshUI;
        }
        public void Execute() { _list[_index] = _newStyle; _refreshUI(); }
        public void Undo() { _list[_index] = _oldStyle; _refreshUI(); }
    }
}
