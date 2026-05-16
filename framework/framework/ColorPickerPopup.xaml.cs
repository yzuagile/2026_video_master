using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace framework
{
    /// <summary>
    /// 完全自製的 WPF 調色盤 Popup。
    /// 使用方式：
    ///   1. 在需要的地方建立 ColorPickerPopup，設定 InitialColor
    ///   2. 監聽 ColorConfirmed 事件取得套用後的顏色
    ///   3. 將此 Control 放入 Popup 或直接嵌入面板
    /// </summary>
    public partial class ColorPickerPopup : UserControl
    {
        // ── 公開事件：使用者按下「套用」時觸發
        public event Action<Color>? ColorConfirmed;

        // ── 公開屬性：外部設定初始顏色
        public Color InitialColor
        {
            set => SetColorInternal(value);
        }

        // ── HSV + Alpha 內部狀態
        private double _h = 0;    // 0–360
        private double _s = 0;    // 0–1
        private double _v = 1;    // 0–1
        private double _a = 1;    // 0–1（透明度）

        // ── 拖曳狀態
        private bool _draggingSv    = false;
        private bool _draggingHue   = false;
        private bool _draggingAlpha = false;

        // ── 防止 TextChanged 迴圈
        private bool _suppressTextEvents = false;

        // ── 常用色票
        private static readonly string[] PresetSwatches =
        {
            "#FFFFFFFF", "#000000FF", "#FF4444FF", "#FF8C00FF",
            "#FFD700FF", "#44DD88FF", "#44AAFFFF", "#AA44FFFF",
            "#FF44AAFF", "#888888FF", "#444444FF", "#FF0000CC",
            "#0000FFCC", "#FFFF0099", "#FFFFFF00",
        };

        // ═══════════════════════════════════════
        //  建構子
        // ═══════════════════════════════════════
        public ColorPickerPopup()
        {
            InitializeComponent();
            this.Loaded += (_, _) =>
            {
                BuildSwatches();
                RefreshAll();
            };
            this.SizeChanged += (_, _) => RefreshAll();
        }
        
        // ═══════════════════════════════════════
        //  外部設定顏色
        // ═══════════════════════════════════════
        public void SetColorInternal(Color c)
        {
            RgbToHsv(c.R, c.G, c.B, out _h, out _s, out _v);
            _a = c.A / 255.0;
            RefreshAll();
        }

        // ═══════════════════════════════════════
        //  SV 色盤事件
        // ═══════════════════════════════════════
        private void SvArea_MouseDown(object s, MouseButtonEventArgs e)
        {
            _draggingSv = true;
            ((UIElement)s).CaptureMouse();
            UpdateSvFromMouse(e.GetPosition(SvCanvas));
        }

        private void SvArea_MouseMove(object s, MouseEventArgs e)
        {
            if (_draggingSv && e.LeftButton == MouseButtonState.Pressed)
                UpdateSvFromMouse(e.GetPosition(SvCanvas));
        }

        private void SvArea_MouseUp(object s, MouseButtonEventArgs e)
        {
            _draggingSv = false;
            ((UIElement)s).ReleaseMouseCapture();
        }

        private void UpdateSvFromMouse(Point p)
        {
            double w = SvCanvas.ActualWidth;
            double h = SvCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            _s = Math.Clamp(p.X / w, 0, 1);
            _v = Math.Clamp(1 - p.Y / h, 0, 1);
            RefreshAll();
        }

        // ═══════════════════════════════════════
        //  色相 Slider 事件
        // ═══════════════════════════════════════
        private void HueBar_MouseDown(object s, MouseButtonEventArgs e)
        {
            _draggingHue = true;
            ((UIElement)s).CaptureMouse();
            UpdateHueFromMouse(e.GetPosition(HueCanvas));
        }

        private void HueBar_MouseMove(object s, MouseEventArgs e)
        {
            if (_draggingHue && e.LeftButton == MouseButtonState.Pressed)
                UpdateHueFromMouse(e.GetPosition(HueCanvas));
        }

        private void HueBar_MouseUp(object s, MouseButtonEventArgs e)
        {
            _draggingHue = false;
            ((UIElement)s).ReleaseMouseCapture();
        }

        private void UpdateHueFromMouse(Point p)
        {
            double w = HueCanvas.ActualWidth;
            if (w <= 0) return;
            _h = Math.Clamp(p.X / w, 0, 1) * 360;
            RefreshAll();
        }

        // ═══════════════════════════════════════
        //  透明度 Slider 事件
        // ═══════════════════════════════════════
        private void AlphaBar_MouseDown(object s, MouseButtonEventArgs e)
        {
            _draggingAlpha = true;
            ((UIElement)s).CaptureMouse();
            UpdateAlphaFromMouse(e.GetPosition(AlphaCanvas));
        }

        private void AlphaBar_MouseMove(object s, MouseEventArgs e)
        {
            if (_draggingAlpha && e.LeftButton == MouseButtonState.Pressed)
                UpdateAlphaFromMouse(e.GetPosition(AlphaCanvas));
        }

        private void AlphaBar_MouseUp(object s, MouseButtonEventArgs e)
        {
            _draggingAlpha = false;
            ((UIElement)s).ReleaseMouseCapture();
        }

        private void UpdateAlphaFromMouse(Point p)
        {
            double w = AlphaCanvas.ActualWidth;
            if (w <= 0) return;
            _a = Math.Clamp(p.X / w, 0, 1);
            RefreshAll();
        }

        // ═══════════════════════════════════════
        //  RGB 文字輸入
        // ═══════════════════════════════════════
        private void RgbInput_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextEvents) return;
            if (TxtR == null || TxtG == null || TxtB == null) return;
            if (!int.TryParse(TxtR.Text, out int r)) return;
            if (!int.TryParse(TxtG.Text, out int g)) return;
            if (!int.TryParse(TxtB.Text, out int b)) return;
            r = Math.Clamp(r, 0, 255);
            g = Math.Clamp(g, 0, 255);
            b = Math.Clamp(b, 0, 255);
            RgbToHsv((byte)r, (byte)g, (byte)b, out _h, out _s, out _v);
            RefreshAll();
        }

        private void AlphaInput_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextEvents) return;
            if (!int.TryParse(TxtA.Text, out int a)) return;
            _a = Math.Clamp(a, 0, 100) / 100.0;
            RefreshAll();
        }

        // ═══════════════════════════════════════
        //  Hex 文字輸入
        // ═══════════════════════════════════════
        private void HexInput_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextEvents) return;
            string hex = TxtHex.Text.Trim().TrimStart('#');
            Color? parsed = TryParseHex(hex);
            if (parsed.HasValue)
            {
                RgbToHsv(parsed.Value.R, parsed.Value.G, parsed.Value.B,
                          out _h, out _s, out _v);
                _a = parsed.Value.A / 255.0;
                RefreshAll(skipHexUpdate: true);
            }
        }

        // ═══════════════════════════════════════
        //  套用按鈕
        // ═══════════════════════════════════════
        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ColorConfirmed?.Invoke(CurrentColor);
        }

        // ═══════════════════════════════════════
        //  核心渲染：RefreshAll
        // ═══════════════════════════════════════
        private void RefreshAll(bool skipHexUpdate = false)
        {
            if (!IsLoaded) return;

            Color pure = HsvToColor(_h, 1, 1);
            Color cur  = CurrentColor;

            double w  = SvCanvas.ActualWidth;
            double h  = SvCanvas.ActualHeight;
            double hw = HueCanvas.ActualWidth;
            double aw = AlphaCanvas.ActualWidth;

            // ── SV 色盤背景
            SvBase.Width  = Math.Max(w, 0);
            SvBase.Height = Math.Max(h, 0);
            SvBase.Fill   = new SolidColorBrush(pure);

            SvWhite.Width  = Math.Max(w, 0);
            SvWhite.Height = Math.Max(h, 0);
            SvWhite.Fill   = new LinearGradientBrush(
                Colors.White, Color.FromArgb(0, 255, 255, 255),
                new Point(0, 0.5), new Point(1, 0.5));

            SvBlack.Width  = Math.Max(w, 0);
            SvBlack.Height = Math.Max(h, 0);
            SvBlack.Fill   = new LinearGradientBrush(
                Color.FromArgb(0, 0, 0, 0), Colors.Black,
                new Point(0.5, 0), new Point(0.5, 1));

            // ── SV 游標位置
            double cx = _s * w - 7;
            double cy = (1 - _v) * h - 7;
            Canvas.SetLeft(SvCursor, cx);
            Canvas.SetTop(SvCursor,  cy);

            // ── 色相 thumb 位置
            Canvas.SetLeft(HueThumb, _h / 360.0 * hw - 9);

            // ── 透明度漸層
            AlphaGradRect.Fill = new LinearGradientBrush(
                Color.FromArgb(0, cur.R, cur.G, cur.B),
                Color.FromArgb(255, cur.R, cur.G, cur.B),
                new Point(0, 0.5), new Point(1, 0.5));
            Canvas.SetLeft(AlphaThumb, _a * aw - 9);

            // ── 預覽方塊
            PreviewColorOverlay.Background = new SolidColorBrush(cur);

            // ── 文字欄位（防止迴圈）
            _suppressTextEvents = true;

            var (r, g, b) = HsvToRgb(_h, _s, _v);
            TxtR.Text = r.ToString();
            TxtG.Text = g.ToString();
            TxtB.Text = b.ToString();
            TxtA.Text = ((int)Math.Round(_a * 100)).ToString();

            if (!skipHexUpdate)
                TxtHex.Text = $"{r:X2}{g:X2}{b:X2}{(int)Math.Round(_a * 255):X2}";

            _suppressTextEvents = false;
        }

        // ═══════════════════════════════════════
        //  色票建立
        // ═══════════════════════════════════════
        private void BuildSwatches()
        {
            SwatchPanel.Children.Clear();
            foreach (var hex in PresetSwatches)
            {
                Color? c = TryParseHex(hex.TrimStart('#'));
                if (c == null) continue;

                var border = new Border
                {
                    Width           = 22, Height = 22,
                    CornerRadius    = new CornerRadius(4),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    BorderThickness = new Thickness(1),
                    Margin          = new Thickness(0, 0, 4, 4),
                    Cursor          = Cursors.Hand,
                    ToolTip         = $"#{c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}{c.Value.A:X2}",
                    Background      = new SolidColorBrush(c.Value)
                };

                // 棋盤格底層（透明色用）
                var check = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background   = new DrawingBrush
                    {
                        TileMode         = TileMode.Tile,
                        Viewport         = new Rect(0, 0, 8, 8),
                        ViewportUnits    = BrushMappingMode.Absolute,
                        Drawing          = BuildCheckerDrawing()
                    },
                };

                var panel = new Grid();
                panel.Children.Add(check);

                var captured = c.Value;
                border.MouseDown += (_, _) =>
                {
                    RgbToHsv(captured.R, captured.G, captured.B,
                              out _h, out _s, out _v);
                    _a = captured.A / 255.0;
                    RefreshAll();
                };

                SwatchPanel.Children.Add(border);
            }
        }

        private static DrawingGroup BuildCheckerDrawing()
        {
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Brushes.LightGray,
                null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            var wg = new GeometryGroup();
            wg.Children.Add(new RectangleGeometry(new Rect(0, 0, 4, 4)));
            wg.Children.Add(new RectangleGeometry(new Rect(4, 4, 4, 4)));
            dg.Children.Add(new GeometryDrawing(Brushes.White, null, wg));
            return dg;
        }

        // ═══════════════════════════════════════
        //  工具函式
        // ═══════════════════════════════════════
        private Color CurrentColor
        {
            get
            {
                var (r, g, b) = HsvToRgb(_h, _s, _v);
                return Color.FromArgb((byte)Math.Round(_a * 255), r, g, b);
            }
        }

        private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
        {
            if (s == 0)
            {
                byte gray = (byte)(v * 255);
                return (gray, gray, gray);
            }
            h /= 60;
            int   i  = (int)Math.Floor(h) % 6;
            double f = h - Math.Floor(h);
            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);

            (double rr, double gg, double bb) = i switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q),
            };
            return ((byte)(rr * 255), (byte)(gg * 255), (byte)(bb * 255));
        }

        private static Color HsvToColor(double h, double s, double v)
        {
            var (r, g, b) = HsvToRgb(h, s, v);
            return Color.FromRgb(r, g, b);
        }

        private static void RgbToHsv(byte r, byte g, byte b,
                                      out double h, out double s, out double v)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            v = max;
            s = max == 0 ? 0 : delta / max;

            if (delta == 0)      { h = 0; return; }
            if (max == rd)       h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd)  h = 60 * ((bd - rd) / delta + 2);
            else                 h = 60 * ((rd - gd) / delta + 4);

            if (h < 0) h += 360;
        }

        private static Color? TryParseHex(string hex)
        {
            hex = hex.TrimStart('#').ToUpperInvariant();
            try
            {
                if (hex.Length == 6)
                {
                    byte rr = Convert.ToByte(hex[0..2], 16);
                    byte gg = Convert.ToByte(hex[2..4], 16);
                    byte bb = Convert.ToByte(hex[4..6], 16);
                    return Color.FromArgb(255, rr, gg, bb);
                }
                if (hex.Length == 8)
                {
                    byte rr = Convert.ToByte(hex[0..2], 16);
                    byte gg = Convert.ToByte(hex[2..4], 16);
                    byte bb = Convert.ToByte(hex[4..6], 16);
                    byte aa = Convert.ToByte(hex[6..8], 16);
                    return Color.FromArgb(aa, rr, gg, bb);
                }
            }
            catch { /* 格式錯誤忽略 */ }
            return null;
        }
    }
}
