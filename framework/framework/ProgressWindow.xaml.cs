using System;
using System.Windows;
using System.Diagnostics;

namespace framework
{
    /// <summary>
    /// ProgressWindow.xaml 的互動邏輯
    /// </summary>
    public partial class ProgressWindow : Window
    {
        private Stopwatch _elapsedTimer;
        private DateTime _startTime;
        private bool _isCancelled = false;

        public bool IsCancelled => _isCancelled;

        public ProgressWindow()
        {
            InitializeComponent();
            _elapsedTimer = new Stopwatch();
            _startTime = DateTime.Now;
        }

        /// <summary>
        /// 更新進度條和相關信息
        /// </summary>
        public void UpdateProgress(int currentValue, int maxValue, string status, string details = "")
        {
            if (currentValue < 0) currentValue = 0;
            if (currentValue > maxValue) currentValue = maxValue;

            ProgressBar.Maximum = maxValue;
            ProgressBar.Value = currentValue;

            int percentage = maxValue > 0 ? (currentValue * 100) / maxValue : 0;
            TxtProgress.Text = $"{percentage}%";
            TxtStatus.Text = status;
            TxtDetails.Text = details;

            // 更新時間信息
            if (!_elapsedTimer.IsRunning && currentValue > 0)
            {
                _elapsedTimer.Start();
            }

            if (_elapsedTimer.IsRunning)
            {
                TimeSpan elapsed = _elapsedTimer.Elapsed;
                TxtTimeElapsed.Text = $"已用時間：{(int)elapsed.TotalSeconds} 秒";

                // 計算估計剩餘時間
                if (percentage > 0 && percentage < 100)
                {
                    double secondsPerPercent = elapsed.TotalSeconds / percentage;
                    double remainingSeconds = secondsPerPercent * (100 - percentage);
                    int remainingSecondsInt = (int)remainingSeconds;
                    TxtTimeRemaining.Text = $"估計剩餘：{remainingSecondsInt} 秒";
                }
                else if (percentage == 100)
                {
                    TxtTimeRemaining.Text = "即將完成...";
                }
            }
        }

        /// <summary>
        /// 標記為完成狀態
        /// </summary>
        public void MarkAsComplete(string finalStatus = "匯出完成！")
        {
            _elapsedTimer.Stop();
            ProgressBar.Value = ProgressBar.Maximum;
            TxtProgress.Text = "100%";
            TxtStatus.Text = finalStatus;
            TxtTimeRemaining.Text = $"總耗時：{(int)_elapsedTimer.Elapsed.TotalSeconds} 秒";
            BtnCancel.Content = "關閉";
        }

        /// <summary>
        /// 顯示錯誤狀態
        /// </summary>
        public void MarkAsError(string errorMessage)
        {
            _elapsedTimer.Stop();
            TxtStatus.Text = "匯出失敗！";
            TxtDetails.Text = $"錯誤：{errorMessage}";
            BtnCancel.Content = "關閉";
            ProgressBar.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (BtnCancel.Content.ToString() == "取消")
            {
                _isCancelled = true;
                BtnCancel.IsEnabled = false;
                TxtStatus.Text = "正在取消...";
            }
            else
            {
                this.Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _elapsedTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
