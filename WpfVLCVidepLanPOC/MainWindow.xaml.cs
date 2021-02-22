using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfVLCVidepLanPOC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Example1Btn.Click += Example1Btn_Click;
            Example2Btn.Click += Example2Btn_Click;
        }

        void Example1Btn_Click(object sender, RoutedEventArgs e)
        {
            var window = new Example1();
            window.Show();
        }

        void Example2Btn_Click(object sender, RoutedEventArgs e)
        {
            var window = new Example2();
            window.Show();
        }
    }
}
