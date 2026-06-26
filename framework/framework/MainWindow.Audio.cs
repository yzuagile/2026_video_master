using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace framework
{
    public partial class MainWindow
    {
        // ── 音訊資料模型 ──────────────────────────────────────────────────────
        public class AudioSegmentData : ITimelineTrackItem
        {
            public Guid   Id            { get; set; } = Guid.NewGuid();
            public int    TrackIndex    { get; set; } = 0;  // 0 = 影片音訊軌, 1 = 外部音訊軌
            public double TimelineStart  { get; set; }
            public double InternalOffset { get; set; }
            public double Duration       { get; set; }
            public Grid?  UIElement      { get; set; }

            public double TimelineStartSeconds
            {
                get => TimelineStart; set => TimelineStart = value;
            }
            public double TimelineDurationSeconds
            {
                get => Duration; set => Duration = value;
            }
        }

        // ── 欄位 ─────────────────────────────────────────────────────────────
        private List<AudioSegmentData> audioSegments  = new(); // 音訊軌 0：影片內嵌音訊
        private List<AudioSegmentData> audioSegments1 = new(); // 音訊軌 1：外部音訊檔

        private AudioSegmentData? selectedAudioSegment = null; // 統一選取，由 TrackIndex 決定所屬軌道

        private MediaPlayer audioPreviewPlayer  = new(); // 音訊軌 0 播放器
        private MediaPlayer audioPreviewPlayer1 = new(); // 音訊軌 1 播放器

        private string? currentExternalAudioPath = null;

        private List<AudioSegmentData>?       preDragAudioOrder;
        private Dictionary<Guid, double>?     preDragAudioStarts;

        // ── 輔助屬性與方法 ────────────────────────────────────────────────────
        private Canvas[] AudioTrackCanvases =>
            new Canvas[] { AudioTrackCanvas, AudioTrackCanvas1 };

        private List<AudioSegmentData> GetAudioSegments(int trackIndex) =>
            trackIndex == 0 ? audioSegments : audioSegments1;

        private MediaPlayer GetAudioPlayer(int trackIndex) =>
            trackIndex == 0 ? audioPreviewPlayer : audioPreviewPlayer1;

        // ── UI 建立 ───────────────────────────────────────────────────────────
        private Grid CreateAudioSegmentUI(AudioSegmentData data)
        {
            Color fillColor   = data.TrackIndex == 0
                ? Color.FromRgb(46, 139, 87)   // 綠色：影片音訊軌
                : Color.FromRgb(180, 100, 30);  // 橘色：外部音訊軌
            Color strokeColor = data.TrackIndex == 0
                ? Color.FromRgb(20, 60, 40)
                : Color.FromRgb(90, 50, 15);

            var container = new Grid
            {
                Width  = data.Duration * PIXELS_PER_SECOND,
                Height = 35,
                Tag    = data,
                Cursor = Cursors.SizeAll
            };

            container.Children.Add(new System.Windows.Shapes.Rectangle
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Fill            = new SolidColorBrush(fillColor),
                Stroke          = new SolidColorBrush(strokeColor),
                StrokeThickness = 1,
                RadiusX = 3, RadiusY = 3
            });

            AddAudioHandle(container, HorizontalAlignment.Left);
            AddAudioHandle(container, HorizontalAlignment.Right);

            container.MouseDown         += TrackItem_MouseDown;
            container.MouseMove         += TrackItem_MouseMove;
            container.MouseLeftButtonUp += TrackItem_MouseUp;

            Canvas.SetLeft(container, data.TimelineStart * PIXELS_PER_SECOND);
            Canvas.SetTop(container, 0);
            return container;
        }

        private void AddAudioHandle(Grid container, HorizontalAlignment align)
        {
            var t = new Thumb
            {
                Width  = 8, HorizontalAlignment = align,
                Cursor = Cursors.Arrow, Background = Brushes.White,
                Opacity = 0, IsHitTestVisible = false
            };
            if (align == HorizontalAlignment.Left)
                t.DragDelta += UnifiedLeftHandle_DragDelta;
            else
                t.DragDelta += UnifiedRightHandle_DragDelta;
            t.DragStarted   += UnifiedHandle_DragStarted;
            t.DragCompleted += UnifiedHandle_DragCompleted;
            container.Children.Add(t);
        }

        // ── 播放同步 ──────────────────────────────────────────────────────────
        private void SyncAudioPlayerToTimeline(double tlSeconds)
        {
            SyncAudioPlayerCore(tlSeconds, audioSegments,  audioPreviewPlayer);
            SyncAudioPlayerCore(tlSeconds, audioSegments1, audioPreviewPlayer1);
        }

        private void SyncAudioPlayerCore(double tlSeconds,
            List<AudioSegmentData> segments, MediaPlayer player)
        {
            var current = segments.FirstOrDefault(a =>
                tlSeconds >= a.TimelineStartSeconds &&
                tlSeconds <  a.TimelineStartSeconds + a.TimelineDurationSeconds);

            if (current != null)
            {
                double expected = current.InternalOffset + (tlSeconds - current.TimelineStartSeconds);
                if (Math.Abs(player.Position.TotalSeconds - expected) > 0.2)
                    player.Position = TimeSpan.FromSeconds(expected);
                if (playheadTimer.IsEnabled) player.Play();
            }
            else
            {
                player.Pause();
            }
        }

        // ── 重整 UI ───────────────────────────────────────────────────────────
        public void RefreshAudioTrackUI()
        {
            RefreshAudioTrackUICore(audioSegments);
            RefreshAudioTrackUICore(audioSegments1);

            if (selectedAudioSegment != null)
            {
                TxtStartTime.Text = selectedAudioSegment.TimelineStartSeconds.ToString("F1");
                TxtEndTime.Text   = (selectedAudioSegment.TimelineStartSeconds
                                     + selectedAudioSegment.TimelineDurationSeconds).ToString("F1");
            }
        }

        private void RefreshAudioTrackUICore(List<AudioSegmentData> segments)
        {
            foreach (var seg in segments)
                if (seg.UIElement != null)
                {
                    Canvas.SetLeft(seg.UIElement, seg.TimelineStartSeconds * PIXELS_PER_SECOND);
                    seg.UIElement.Width = Math.Max(10, seg.TimelineDurationSeconds * PIXELS_PER_SECOND);
                }
        }

        // ── 重疊處理 ──────────────────────────────────────────────────────────
        private void ResolveAudioOverlaps()
        {
            ResolveAudioOverlapsCore(audioSegments);
            ResolveAudioOverlapsCore(audioSegments1);

            if (selectedAudioSegment != null)
            {
                TxtStartTime.Text = selectedAudioSegment.TimelineStartSeconds.ToString("F1");
                TxtEndTime.Text   = (selectedAudioSegment.TimelineStartSeconds
                                     + selectedAudioSegment.TimelineDurationSeconds).ToString("F1");
            }
        }

        private void ResolveAudioOverlapsCore(List<AudioSegmentData> segments)
        {
            segments.Sort((a, b) =>
                (a.TimelineStartSeconds + a.TimelineDurationSeconds / 2.0)
                    .CompareTo(b.TimelineStartSeconds + b.TimelineDurationSeconds / 2.0));

            double next = 0;
            foreach (var seg in segments)
            {
                if (seg.TimelineStartSeconds < next)
                {
                    seg.TimelineStartSeconds = next;
                    if (seg.UIElement != null)
                        Canvas.SetLeft(seg.UIElement, seg.TimelineStartSeconds * PIXELS_PER_SECOND);
                }
                next = seg.TimelineStartSeconds + seg.TimelineDurationSeconds;
            }

            if (next > currentVideoDuration)
            {
                double newWidth = next * PIXELS_PER_SECOND + 200;
                if (newWidth > AudioTrackCanvas.Width)
                {
                    VideoTrackCanvas.Width     = newWidth;
                    foreach (var c in AudioTrackCanvases) c.Width = newWidth;
                    foreach (var c in SubtitleTrackCanvases) c.Width = newWidth;
                    TimeRulerCanvas.Width      = newWidth;
                    TimelineContentStack.Width = newWidth;
                    DrawTimeRuler(next);
                }
            }
        }

        // ── 分割 ─────────────────────────────────────────────────────────────
        private void SplitAudioSegment(AudioSegmentData segment, double splitPointSeconds)
        {
            if (splitPointSeconds <= segment.TimelineStart + 0.5 ||
                splitPointSeconds >= segment.TimelineStart + segment.Duration - 0.5)
            {
                MessageBox.Show("切割點太靠近邊緣，請選擇片段中間的位置下刀！", "提示");
                return;
            }

            var segs   = GetAudioSegments(segment.TrackIndex);
            var canvas = AudioTrackCanvases[segment.TrackIndex];

            var cmd = new SplitAudioCommand(
                segs, canvas, segment,
                splitPointSeconds, PIXELS_PER_SECOND,
                CreateAudioSegmentUI, ClearSelection);
            commandHistory.ExecuteCommand(cmd);

            GetAudioPlayer(segment.TrackIndex).Position = TimeSpan.FromSeconds(splitPointSeconds);
        }

        // ── 匯入外部音訊（音訊軌 1）─────────────────────────────────────────
        private void BtnImportAudio_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "音訊檔案|*.mp3;*.wav;*.aac;*.m4a;*.ogg;*.flac|所有檔案|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            currentExternalAudioPath = dlg.FileName;

            audioPreviewPlayer1.Stop();
            audioPreviewPlayer1.Close();
            audioPreviewPlayer1.MediaOpened -= AudioPlayer1_MediaOpened;
            audioPreviewPlayer1.MediaOpened += AudioPlayer1_MediaOpened;
            audioPreviewPlayer1.Open(new Uri(currentExternalAudioPath));
        }

        private void AudioPlayer1_MediaOpened(object? sender, EventArgs e)
        {
            audioPreviewPlayer1.MediaOpened -= AudioPlayer1_MediaOpened;
            if (!audioPreviewPlayer1.NaturalDuration.HasTimeSpan) return;

            double dur = audioPreviewPlayer1.NaturalDuration.TimeSpan.TotalSeconds;

            Dispatcher.Invoke(() =>
            {
                AudioTrackCanvas1.Children.Clear();
                audioSegments1.Clear();
                if (selectedAudioSegment?.TrackIndex == 1)
                    selectedAudioSegment = null;

                double needed = dur * PIXELS_PER_SECOND + 100;
                if (needed > AudioTrackCanvas.Width)
                {
                    VideoTrackCanvas.Width     = needed;
                    foreach (var c in AudioTrackCanvases) c.Width = needed;
                    foreach (var c in SubtitleTrackCanvases) c.Width = needed;
                    TimeRulerCanvas.Width      = needed;
                    TimelineContentStack.Width = needed;
                    DrawTimeRuler(Math.Max(dur, currentVideoDuration));
                }

                var seg = new AudioSegmentData
                {
                    TrackIndex     = 1,
                    TimelineStart  = 0,
                    InternalOffset = 0,
                    Duration       = dur
                };
                seg.UIElement = CreateAudioSegmentUI(seg);
                audioSegments1.Add(seg);
                AudioTrackCanvas1.Children.Add(seg.UIElement);
            });
        }
    }
}
