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

        public ExportWindow()
        {
            InitializeComponent();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
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
                // 確保如果輸入框是空的，也有預設值
                FinalBitrate = string.IsNullOrWhiteSpace(TxtBitrate.Text) ? "4000" : TxtBitrate.Text;
            }
            else
            {
                FinalBitrate = "自動";
            }

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
