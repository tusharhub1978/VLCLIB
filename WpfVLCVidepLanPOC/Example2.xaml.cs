using DirectX.Capture;
using LibVLCSharp.Shared;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace WpfVLCVidepLanPOC
{
    public partial class Example2 : Window
    {
        LibVLC _libVLC;
        MediaPlayer _mediaPlayer;
        MediaPlayer _mediaPlayerRecord;
        StringBuilder mLogger = new StringBuilder();

        const string kdefaultTranscode = "#transcode{vcodec=h264,vb=1500,fps=25,scale=0,acodec=none,ab=128,channels=2,threads=4,deinterlace=true,high-priority=true}";
        int mIterations = 1;
        int mDuration = 10;
        int mCurrentIteration = 0;

        string mTranscodeValue = kdefaultTranscode;
        DispatcherTimer _VideoIterationTimer;

        public Example2()
        {
            InitializeComponent();

            Core.Initialize();

            //var label = new Label
            //{                
            //    Content = "TEST",
            //    HorizontalAlignment = HorizontalAlignment.Right,
            //    VerticalAlignment = VerticalAlignment.Bottom,
            //    Foreground = new SolidColorBrush(Colors.Red)
            //};
            //test.Children.Add(label);

            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayerRecord = new MediaPlayer(_libVLC);

            _libVLC.Log += _libVLC_Log;

            _mediaPlayer.EnableHardwareDecoding = true;

            // we need the VideoView to be fully loaded before setting a MediaPlayer on it.
            VideoView.Loaded += (sender, e) => VideoView.MediaPlayer = _mediaPlayer;
            Unloaded += Example2_Unloaded;

            Filters filters = new Filters();
            FilterCollection filterCollection = filters.VideoInputDevices;

            foreach(Filter filter in filterCollection)
            {
                cmbCaptureCard.Items.Add(filter.Name);
            }
        }        

        int nLogMessages = 0;
        private void _libVLC_Log(object sender, LogEventArgs e)
        {
            // Redirect log output to the console
            string logMsg = $"[{e.Level}] {e.Module}:{e.Message}";
            //Console.WriteLine(logMsg);

            ++nLogMessages;
            mLogger.AppendLine(logMsg);

            if(nLogMessages % 100 == 0)
            {
                this.Dispatcher.Invoke(() => {
                    txtLogger.Text = txtLogger.Text + mLogger.ToString();
                    txtLogger.ScrollToEnd();
                    mLogger.Clear();
                });
            }
        }

        private void Example2_Unloaded(object sender, RoutedEventArgs e)
        {
            if(_mediaPlayerRecord != null)
            {
                if(_mediaPlayerRecord.IsPlaying)
                {
                    _mediaPlayerRecord.Stop();
                }
                _mediaPlayerRecord.Dispose();
            }

            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _libVLC.Dispose();
        }

        void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (VideoView.MediaPlayer.IsPlaying)
            {
                VideoView.MediaPlayer.Stop();
            }
            if (_mediaPlayerRecord.IsPlaying)
            {
                _mediaPlayerRecord.Stop();
            }
            lblStatus.Content = "Status: STOPPED";
            btnPlay.IsEnabled = true;
            btnStop.IsEnabled = false;
            btnSnapshot.IsEnabled = false;

            txtLogger.Text = txtLogger.Text + mLogger.ToString();
            txtLogger.ScrollToEnd();

            mLogger.Clear();
        }

        private void RecordTimerCallback(object sender, EventArgs e)
        {
            try
            {                // stop timer
                _VideoIterationTimer.Stop();

                StopButton_Click(null, null);

                if (mCurrentIteration < mIterations)                
                {
                    Thread.Sleep(1 * 1000);
                    // this will send the Start Command
                    PlayButton_Click(null,null);
                }
                else
                {
                    mCurrentIteration = 0;
                }
                txtCurrentIteraion.Text = mCurrentIteration.ToString();
            }
            catch (Exception inException)
            {
                MessageBox.Show(inException.Message);
            }
        }

        void PlayButton_Click(object inSender, RoutedEventArgs inRoutedEventArgs)
        {
            try
            {
                mLogger.Clear();
                txtLogger.Text = "";

                if (!VideoView.MediaPlayer.IsPlaying && (chkStrategy2.IsChecked.Value == false || cmbCaptureMode.SelectedIndex == 0 || cmbCaptureMode.SelectedIndex == 2))
                {
                    // For Play bakc Media file
                    //using (var media = new Media(_libVLC, new Uri("http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4")))

                    // For Capture live feed from Camera device
                    using (var media = new Media(_libVLC, "dshow:// ", FromType.FromLocation))
                    {
                        media.AddOption($":dshow-vdev={cmbCaptureCard.Text}");
                        if (string.Compare(cmbCaptureCard.Text, "Integrated Camera") == 0)
                        {
                            media.AddOption(":dshow-config");
                            media.AddOption(":dshow-fps=10 ");
                        }
                        else 
                        {
                            media.AddOption(":dshow-fps=30 ");
                        }
                        media.AddOption(":no-audio");
                        media.AddOption(":ignore-config");

                        media.AddOption(":live-caching=300");
                        media.AddOption(":dshow-aspect-ratio=4:3");

                        var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                        string destination = Path.Combine(currentDirectory, GenerateNewFileName());

                        if(cmbCaptureMode.SelectedIndex ==0)
                        {
                            // Only preview of the feed
                            media.AddOption(":sout=#duplicate{dst=display}");
                        }
                        else if ((cmbCaptureMode.SelectedIndex == 1) && (!chkStrategy2.IsChecked.Value))
                        {
                            // Only recording mp4 using h264
                            media.AddOption(":sout=" + mTranscodeValue + ":std{access=file,dst=" + destination + "}");
                            //media.AddOption(":sout=#transcode{vcodec=h264,vb=1500,fps=25,scale=0,acodec=none,ab=128,channels=2,threads=4,deinterlace=true,high-priority=true}:std{access=file,dst=" + destination + "}");
                        }
                        else if ((cmbCaptureMode.SelectedIndex == 2) && (!chkStrategy2.IsChecked.Value))
                        {
                            // Live feed pre-view and recording mp4 using h264
                            media.AddOption(":sout=" + mTranscodeValue + ":duplicate{dst=display,dst=std{access=file{no-overwrite},dst=" + destination + "}");
                            //media.AddOption(":sout=#transcode{vcodec=h264,vb=1500,fps=25,scale=0,acodec=none,ab=48,channels=2,threads=4,deinterlace=true,high-priority=true}:duplicate{dst=display,dst=std{access=file{no-overwrite},dst=" + destination + "}");
                        }

                        media.AddOption(":no-sout-all");
                        media.AddOption(":sout-keep");
                        if (chkHWAcceleration.IsChecked.Value == true)
                        {
                            media.AddOption(":avcodec - hw = d3d11va");
                        }

                        if (!VideoView.MediaPlayer.Play(media))
                        {
                            MessageBox.Show("Error in Playing...");
                        }
                    }
                }

                if (!_mediaPlayerRecord.IsPlaying && (chkStrategy2.IsChecked.Value == true && (cmbCaptureMode.SelectedIndex == 1 || cmbCaptureMode.SelectedIndex == 2)))
                {
                    using (var media = new Media(_libVLC, "dshow:// ", FromType.FromLocation))
                    {
                        media.AddOption($":dshow-vdev={cmbCaptureCard.Text}");
                        if (string.Compare(cmbCaptureCard.Text, "Integrated Camera") == 0)
                        {
                            if (cmbCaptureMode.SelectedIndex == 1)
                            {
                                media.AddOption(":dshow-config");
                            }
                            media.AddOption(":dshow-fps=10 ");
                        }
                        else
                        {
                            media.AddOption(":dshow-fps=30 ");
                        }

                        media.AddOption(":live-caching=300");
                        media.AddOption(":dshow-aspect-ratio=4:3");

                        var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                        string destination = Path.Combine(currentDirectory, GenerateNewFileName());

                        if (cmbCaptureMode.SelectedIndex == 1 || cmbCaptureMode.SelectedIndex == 2)
                        {
                            // Only recording mp4 using h264
                            media.AddOption(":sout=" + mTranscodeValue + ":std{access=file,dst=" + destination + "}");
                            //media.AddOption(":sout=#transcode{vcodec=h264,vb=1500,fps=25,scale=0,acodec=none,ab=128,channels=2,threads=4,deinterlace=true,high-priority=true}:std{access=file,dst=" + destination + "}");
                            media.AddOption(":no-sout-all");
                            media.AddOption(":sout-keep");
                            if(chkHWAcceleration.IsChecked.Value ==  true)
                            {
                                media.AddOption(":avcodec - hw = d3d11va");
                            }
                            if (!_mediaPlayerRecord.Play(media))
                            {
                                MessageBox.Show("Error in recording...");
                            }
                        }
                    }
                }

                lblStatus.Content = "Status: PLAYING";
                btnPlay.IsEnabled = false;
                btnStop.IsEnabled = true;
                btnSnapshot.IsEnabled = true;

                if(cmbCaptureMode.SelectedIndex != 3)
                {
                    if (_VideoIterationTimer == null)
                    {
                        _VideoIterationTimer = new DispatcherTimer();
                        _VideoIterationTimer.Interval = new TimeSpan(0, 0, mDuration);
                        _VideoIterationTimer.Tick += RecordTimerCallback;
                        _VideoIterationTimer.Start();
                    }
                    else
                    {
                        _VideoIterationTimer.Interval = new TimeSpan(0, 0, mDuration);
                        _VideoIterationTimer.Start();
                    }
                    mCurrentIteration++;
                    txtCurrentIteraion.Text = mCurrentIteration.ToString();
                }
                else
                {
                    if (!VideoView.MediaPlayer.IsPlaying)
                    {
                        using (var media = new Media(_libVLC, new Uri(@"C:\\Temp\\09-02-21_12-06-28.wmv")))
                        {
                            if(!VideoView.MediaPlayer.Play(media))
                            {
                                MessageBox.Show("Error in recording...");
                            }
                        }
                    }
                }
            }
            catch (Exception inException)
            {
                MessageBox.Show(inException.Message);
            }
        }

        private static string GenerateNewFileName(string inExtension = "mp4")
        {
            DateTime dtNow = DateTime.Now;
            string newFileName = dtNow.ToString("dd-MM-yy_hh-mm-ss");

            return $"{newFileName}.{inExtension}";
        }

        static int ConvertToNumber(string inStringValue)
        {
            int nResult = 0;

            if(!int.TryParse(inStringValue, out nResult))
            {
                nResult = 0;
            }

            return nResult;
        }

        protected override void OnClosed(EventArgs e)
        {
            VideoView.Dispose();
        }

        private void TxtIterations_TextChanged(object sender, TextChangedEventArgs e)
        {
            mIterations = ConvertToNumber(txtIterations.Text);
            if (mIterations == 0)
            {
                mIterations = 1;
            }
        }

        private void TxtDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            mDuration = ConvertToNumber(txtDuration.Text);
            if (mDuration == 0)
            {
                mDuration = 10;
            }
        }

        private void BtnAbort_Click(object sender, RoutedEventArgs e)
        {
            _VideoIterationTimer.Stop();
            StopButton_Click(null, null);
            btnPlay.IsEnabled = true;
            btnStop.IsEnabled = false;
            btnSnapshot.IsEnabled = false;
            mCurrentIteration = 0;
            txtCurrentIteraion.Text = mCurrentIteration.ToString();
            lblStatus.Content = "Status: STOPPED";
        }

        private void TxtTranscode_TextChanged(object sender, TextChangedEventArgs e)
        {
            string value = txtTranscode.Text;
            if (!string.IsNullOrEmpty(value) && !string.IsNullOrWhiteSpace(value))
            {
                mTranscodeValue = $"{value}";
                mTranscodeValue = "#transcode{" + mTranscodeValue + "}";
            }
            else
            {
                mTranscodeValue = kdefaultTranscode;
            }
        }

        private void BtnSnapshot_Click(object sender, RoutedEventArgs e)
        {
            bool success = false;
            try
            {

                var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string destination = Path.Combine(currentDirectory, GenerateNewFileName(".png"));

                if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
                {
                    success = _mediaPlayer.TakeSnapshot(1, destination, 1024, 1280);
                }
                else if (_mediaPlayerRecord != null && _mediaPlayerRecord.IsPlaying)
                {
                    success = _mediaPlayerRecord.TakeSnapshot(1, destination, 1024, 1280);
                }
            }
            catch(Exception inException)
            {
                MessageBox.Show(inException.Message);
            }

            if(!success)
            {
                MessageBox.Show("Snapshot failed");
            }
        }
    }
}
