using LibVLCSharp.Shared;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Windows;

namespace WpfVLCVidepLanPOC
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            ChangeProcessAffinity();
            base.OnStartup(e);
        }


        /// <summary>
        /// Setting the processor affinity for the application
        /// </summary>
        private static void ChangeProcessAffinity()
        {
            bool setAffinity = false;
            string changeProcessAffinity = ConfigurationManager.AppSettings["ChangeAffinity"];
            if ((string.IsNullOrEmpty(changeProcessAffinity)) || !(bool.TryParse(changeProcessAffinity, out setAffinity)))
            {
                setAffinity = false;
            }

            if (setAffinity)
            {
                var logicalCoreCount = Environment.ProcessorCount;

                int processAffinity = Convert.ToInt32(Math.Pow(2, logicalCoreCount / 2));

                Process.GetCurrentProcess().ProcessorAffinity = (System.IntPtr)processAffinity - 1;
            }
        }
    }
}
