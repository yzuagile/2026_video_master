using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace framework
{
    /// <summary>
    /// ExportWindow.xaml 的互動邏輯
    /// </summary>
    public enum VideoFormat { MP4, MKV, MOV }
    public partial class ExportWindow : Window
    {
        // 定義屬性讓 MainWindow 可以讀取結果
        public VideoFormat SelectedFormat { get; private set; }
        public string FinalBitrate { get; private set; } = "";

        private double videoDurationSeconds; // 影片持續時間（秒）

        public ExportWindow(double durationSeconds = 0)
        {
            InitializeComponent();
            videoDurationSeconds = durationSeconds;
            this.Loaded += ExportWindow_Loaded;
        }

        private void ExportWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateSizeEstimate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出視窗初始化失敗：{ex.Message}\n{ex.StackTrace}", "錯誤");
            }
        }

        private void UpdateSizeEstimate()
        {
            // 如果控制項尚未建立，先跳過，等 Loaded 之後再更新一次
            if (LblSizeEstimate == null || TxtBitrate == null || RadioLow == null || RadioMedium == null || RadioHigh == null || RadioCustom == null)
            {
                return;
            }

            if (videoDurationSeconds <= 0)
            {
                LblSizeEstimate.Text = "預估檔案大小：無法計算（無影片資訊）";
                return;
            }

            double bitrateKbps = 0;
            if (RadioLow.IsChecked == true)
            {
                bitrateKbps = 1000; // 低品質
            }
            else if (RadioMedium.IsChecked == true)
            {
                bitrateKbps = 2000; // 中品質
            }
            else if (RadioHigh.IsChecked == true)
            {
                bitrateKbps = 4000; // 高品質
            }
            else if (RadioCustom.IsChecked == true)
            {
                if (double.TryParse(TxtBitrate.Text, out double customBitrate))
                {
                    bitrateKbps = customBitrate;
                }
                else
                {
                    LblSizeEstimate.Text = "預估檔案大小：碼率無效";
                    return;
                }
            }

            // 估算檔案大小 (MB) = (碼率 * 持續時間 * 8) / (1024 * 1024)
            double sizeMB = (bitrateKbps * videoDurationSeconds * 8) / (1024 * 1024);
            double minSize = sizeMB * 0.8; // 假設範圍 ±20%
            double maxSize = sizeMB * 1.2;

            LblSizeEstimate.Text = $"預估檔案大小：{minSize:F1} - {maxSize:F1} MB";
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 確保 formatStr 永遠不會是 null
                string formatStr = (ComboFormat.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "MP4";

                // 使用 TryParse 進行安全轉換
                if (Enum.TryParse(formatStr, out VideoFormat resultFormat))
                {
                    SelectedFormat = resultFormat;
                }

                // 取得碼率邏輯
                if (RadioCustom.IsChecked == true)
                {
                    // 驗證碼率
                    if (!double.TryParse(TxtBitrate.Text, out double customBitrate) || customBitrate <= 0)
                    {
                        MessageBox.Show("請輸入有效的碼率（正數）！", "錯誤");
                        return;
                    }
                    FinalBitrate = TxtBitrate.Text;
                }
                else
                {
                    // 根據選擇設定具體碼率
                    if (RadioLow.IsChecked == true)
                    {
                        FinalBitrate = "1000";
                    }
                    else if (RadioMedium.IsChecked == true)
                    {
                        FinalBitrate = "2000";
                    }
                    else if (RadioHigh.IsChecked == true)
                    {
                        FinalBitrate = "4000";
                    }
                }

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出確認失敗：{ex.Message}\n{ex.StackTrace}", "錯誤");
            }
        }

        private void RadioQuality_Checked(object sender, RoutedEventArgs e)
        {
            UpdateSizeEstimate();
        }

        private void TxtBitrate_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (RadioCustom.IsChecked == true)
            {
                UpdateSizeEstimate();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
