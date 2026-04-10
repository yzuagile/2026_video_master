using Microsoft.Win32; // 為了使用 OpenFileDialog
using System;
using System.Windows;

namespace framework // ⚠️注意：如果你的專案名稱不同，請把這裡改成你的專案名稱
{
    public partial class MainWindow : Window
    {
        private string currentVideoPath = "";

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
                VideoPlayer.Play(); // 讀取後自動播放
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
            MessageBox.Show("準備啟動 FFmpeg 引擎...\n即將把字卡與影片進行底層合成！", "輸出準備");
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
            MessageBox.Show($"已記錄字卡內容：「{textToApply}」。\n\n(目前為純記錄，後續將把此參數傳遞給 FFmpeg)", "系統訊息");
        }

        // ================= 分頁功能：影像剪輯 =================

        // 按鈕：設定剪輯標記
        private void BtnTrim_Click(object sender, RoutedEventArgs e)
        {
            // 抓取使用者輸入的開始與結束時間
            string startTime = TxtStartTime.Text;
            string endTime = TxtEndTime.Text;

            MessageBox.Show($"已記錄剪輯指令：\n保留從第 {startTime} 秒 到 第 {endTime} 秒的片段。\n\n(後續輸出時會將此參數交給 FFmpeg 進行裁切)", "剪輯設定");
        }
    }
}