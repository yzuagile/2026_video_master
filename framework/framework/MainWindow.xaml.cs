using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace framework
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    
        // 按鈕：設定剪輯標記 (新增的功能)
        private void BtnTrim_Click(object sender, RoutedEventArgs e)
        {
            // 抓取使用者輸入的開始與結束時間
            string startTime = TxtStartTime.Text;
            string endTime = TxtEndTime.Text;

            MessageBox.Show($"已記錄剪輯指令：\n保留從第 {startTime} 秒 到 第 {endTime} 秒的片段。\n\n(後續輸出時會將此參數交給 FFmpeg 進行裁切)", "剪輯設定");
        }
    }

        
}