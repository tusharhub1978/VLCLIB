using DirectX.Capture;
using LibVLCSharp.Shared;
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace WpfVLCVidepLanPOC
{
    public partial class Example2 : Window, INotifyPropertyChanged
    {
        LibVLC _libVLC;
        MediaPlayer _mediaPlayer;
        MediaPlayer _mediaPlayerRecord;
        StringBuilder mLogger = new StringBuilder();

        string _filename = "";
        const string kdefTranscodeText = "venc=x264{keyint=10},vcodec=h264,vb=1500,fps=25,scale=0,acodec=none,ab=128,channels=2,threads=4,high-priority=true";

        //private string kdefaultTranscode = 
        // #transcode{vcodec=mp4v,vb=1500,fps=25,scale=0,acodec=none,ab=128,channels=2,threads=4,high-priority=true}
        int mIterations = 1;
        int mDuration = 10;
        int mCurrentIteration = 0;

        string mTranscodeValue;

        DispatcherTimer _VideoIterationTimer;
        DispatcherTimer _VideoPlaybackTimer;
        private bool userIsDraggingSlider = false;

        private string _VideoTimeString;
        public string VideoTimeString
        {
            get { return _VideoTimeString; }
            set { _VideoTimeString = value; raisePropertyChanged("VideoTimeString"); }
        }

        private double _VideoCurrentTime;
        public double VideoCurrentTime
        {
            get { return _VideoCurrentTime; }
            set { _VideoCurrentTime = value; raisePropertyChanged("VideoCurrentTime"); }
        }
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
            mTranscodeValue = "#transcode{" + kdefTranscodeText + "}";

            txtTranscode.Text = kdefTranscodeText;
            _libVLC = new LibVLC(new string[] {
                "--no-snapshot-preview",
                "--no-osd",
                "--avcodec-hw=d3d11va",
                "--no-video-title",
                "--no-audio"});

            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayerRecord = new MediaPlayer(_libVLC);

            _libVLC.Log += _libVLC_Log;

            _mediaPlayer.EnableHardwareDecoding = true;

            // we need the VideoView to be fully loaded before setting a MediaPlayer on it.
            VideoView.Loaded += (sender, e) => VideoView.MediaPlayer = _mediaPlayer;
            Unloaded += Example2_Unloaded;

            Filters filters = new Filters();
            FilterCollection filterCollection = filters.VideoInputDevices;

            foreach (Filter filter in filterCollection)
            {
                cmbCaptureCard.Items.Add(filter.Name);
            }

            btnReverse.Content = "[ << ]";
            btnForward.Content = "[ >> ]";
            this.DataContext = this;
        }

        private object lockObject = new object();

        int nLogMessages = 0;
        private void _libVLC_Log(object sender, LogEventArgs e)
        {
            // Redirect log output to the console
            string logMsg = $"[{e.Level}] {e.Module}:{e.Message}";
            //Console.WriteLine(logMsg);

            ++nLogMessages;
            lock (lockObject)
            {
                mLogger.AppendLine(logMsg);
            }

            if (nLogMessages % 10 == 0)
            {
                this.Dispatcher.Invoke(() =>
                {
                    lock (lockObject)
                    {
                        string logMessages = txtLogger.Text + mLogger.ToString();
                        txtLogger.Text = logMessages;
                        mLogger.Clear();
                    }
                    txtLogger.ScrollToEnd();
                });
            }
        }

        private void WrireLogsToFile(string inFilePath)
        {
            using (StreamWriter sw = new StreamWriter(inFilePath, false, Encoding.UTF8, 65536))
            {
                sw.WriteLine(mLogger.ToString());
            }
        }

        private void Example2_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayerRecord != null)
            {
                if (_mediaPlayerRecord.IsPlaying)
                {
                    _mediaPlayerRecord.Stop();
                }
                _mediaPlayerRecord.Dispose();
            }

            _libVLC.Log -= _libVLC_Log;
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _libVLC.Dispose();
        }

        void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (VideoView.MediaPlayer == null) return;

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


            this.Dispatcher.Invoke(() =>
            {
                lock (lockObject)
                {
                    string logMessages = txtLogger.Text + mLogger.ToString();
                    txtLogger.Text = logMessages;
                    mLogger.Clear();
                }
                txtLogger.ScrollToEnd();
            });
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
                    PlayButton_Click(null, null);
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
                lock (lockObject)
                {
                    mLogger.Clear();
                }

                if (!VideoView.MediaPlayer.IsPlaying && (chkStrategy2.IsChecked.Value == false || cmbCaptureMode.SelectedIndex == 0 || cmbCaptureMode.SelectedIndex == 2))
                {
                    // For Playback Media file
                    //using (var media = new Media(_libVLCPreview, new Uri("http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4")))

                    // For Capture live feed from Camera device
                    using (var media = new Media(_libVLC, "dshow:// ", FromType.FromLocation))
                    {
                        media.AddOption($":dshow-vdev={cmbCaptureCard.Text}");
                        if (string.Compare(cmbCaptureCard.Text, "Integrated Camera") == 0)
                        {
                            media.AddOption(":dshow-config");
                            media.AddOption(":dshow-fps=10");
                        }
                        else
                        {
                            media.AddOption(":dshow-fps=30");
                        }
                        media.AddOption(":no-audio");
                        media.AddOption(":live-caching=300");
                        media.AddOption(":dshow-aspect-ratio=5:4");
                        media.AddOption(":dshow-size=1280x1024");
                        media.AddOption(":dshow-adev=none");
                        media.AddOption(":avcodec-hw=d3d11va");
                        var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                        string destination = Path.Combine(currentDirectory, GenerateNewFileName());

                        if (cmbCaptureMode.SelectedIndex == 0)
                        {
                            // Only preview of the feed
                            //media.AddOption(":sout=#duplicate{dst=display}");
                        }
                        else if ((cmbCaptureMode.SelectedIndex == 1) && (!chkStrategy2.IsChecked.Value))
                        {
                            // Only recording mp4 using h264
                            media.AddOption(":sout=" + mTranscodeValue + ":std{access=file,dst=" + destination + ",mux=ts}");
                            //media.AddOption(":sout=#transcode{vcodec=h264,vb=1500,fps=25,scale=0,acodec=none,ab=128,channels=2,threads=4,deinterlace=true,high-priority=true}:std{access=file,dst=" + destination + "}");
                        }
                        else if ((cmbCaptureMode.SelectedIndex == 2) && (!chkStrategy2.IsChecked.Value))
                        {
                            // Live feed pre-view and recording mp4 using h264
                            media.AddOption(":sout=" + mTranscodeValue + ":duplicate{dst=display,dst=std{access=file{no-overwrite},dst=" + destination + ",mux=ts}}");
                            //media.AddOption(":sout=#transcode{vcodec=h264,vb=1500,fps=25,scale=0,acodec=none,ab=48,channels=2,threads=4,deinterlace=true,high-priority=true}:duplicate{dst=display,dst=std{access=file{no-overwrite},dst=" + destination + "}");
                        }

                        if (chkHWAcceleration.IsChecked.Value == true)
                        {
                            media.AddOption(":avcodec-hw=d3d11va");
                        }

                        if (!VideoView.MediaPlayer.Play(media))
                        {
                            MessageBox.Show("Error in Playing...");
                        }
                        // Introduce delay between Preview and Recording (especially in case of SimWare Virtual Camera [All-IN-ONE])
                        Thread.Sleep(500);
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
                            media.AddOption(":dshow-fps=10");
                        }
                        else
                        {
                            media.AddOption(":dshow-fps=30");
                        }

                        media.AddOption(":no-audio");
                        media.AddOption(":live-caching=300");
                        media.AddOption(":dshow-aspect-ratio=5:4");
                        media.AddOption(":dshow-size=1280x1024");
                        media.AddOption(":dshow-adev=none");
                        media.AddOption(":avcodec-hw=d3d11va");

                        var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                        string destination = Path.Combine(currentDirectory, GenerateNewFileName());

                        if (cmbCaptureMode.SelectedIndex == 1 || cmbCaptureMode.SelectedIndex == 2)
                        {
                            // Only recording mp4 using h264
                            string sTranscodeString = ":sout=" + mTranscodeValue + ":std{access=file,dst=" + destination + ",mux=ts}";
                            media.AddOption(sTranscodeString);
                            //media.AddOption(":sout=#transcode{vcodec=h264,vb=1500,fps=25,scale=0,acodec=none,ab=128,channels=2,threads=4,deinterlace=true,high-priority=true}:std{access=file,dst=" + destination + "}");
                            if (chkHWAcceleration.IsChecked.Value == true)
                            {
                                media.AddOption(":avcodec-hw=d3d11va");
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

                if (cmbCaptureMode.SelectedIndex != 3)
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
                        VideoView.MediaPlayer.Playing -= MediaPlayer_Playing;
                        VideoView.MediaPlayer.Stopped -= MediaPlayer_Stopped;

                        //string filePath = Path.Combine(AssemblyDirectory, "Attempt1.mkv");
                        //string filePath = Path.Combine(AssemblyDirectory, "AttemptVideo_2.wmv");
                        //string filePath = Path.Combine(AssemblyDirectory, "AttemptVideo_3366c905-2ab1-4d24-ab67-0f7a6eb981d3.mp4");

                        string filePath = _filename;
                        Uri uriFilePath = new Uri(filePath);
                        using (var media = new Media(_libVLC, uriFilePath))
                        {
                            //C:\Temp\AttemptVideo_ab4678de-89fc-4baa-bf87-08fe158ee438.mp4
                            media.AddOption(":file-caching=300");
                            media.AddOption(":avcodec-fast");
                            //media.AddOption(":demux=avformat");
                            media.AddOption(":no-avcodec-corrupted");

                            if (!VideoView.MediaPlayer.Play(media))
                            {
                                MessageBox.Show("Error in recording...");
                            }

                            if (_VideoPlaybackTimer != null)
                            {
                                _VideoPlaybackTimer.Stop();
                            }

                            _VideoPlaybackTimer = new DispatcherTimer();
                            _VideoPlaybackTimer.Interval = TimeSpan.FromSeconds(1);
                            _VideoPlaybackTimer.Tick += _VideoPlayback_Tick; ;

                            VideoView.MediaPlayer.Playing += MediaPlayer_Playing;
                            VideoView.MediaPlayer.Stopped += MediaPlayer_Stopped;
                            //VideoView.MediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                        }
                    }
                }
            }
            catch (Exception inException)
            {
                MessageBox.Show(inException.Message);
            }
        }

        private void MediaPlayer_Stopped(object sender, EventArgs e)
        {
            _VideoPlaybackTimer.Stop();

            this.Dispatcher.Invoke(() =>
            {
                if (cmbCaptureMode.SelectedIndex == 3)
                {
                    lblStatus.Content = "Status: STOPPED";
                    btnPlay.IsEnabled = true;
                    btnStop.IsEnabled = false;
                    btnSnapshot.IsEnabled = true;
                }

            }, DispatcherPriority.Send);
        }

        private void _VideoPlayback_Tick(object sender, EventArgs e)
        {
            if (!userIsDraggingSlider && VideoView.MediaPlayer.IsPlaying)
            {
                this.Dispatcher.Invoke(() =>
                {
                    this.VideoSlider.Value = VideoView.MediaPlayer.Time / 1000;
                    this.VideoCurrentTime = VideoView.MediaPlayer.Time / 1000;
                    this.VideoTimeString = $"{this.VideoCurrentTime} : {this.VideoSlider.Maximum}";
                }, DispatcherPriority.Send);
            }
        }

        private void MediaPlayer_TimeChanged(object sender, MediaPlayerTimeChangedEventArgs e)
        {
        }

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {

                this.VideoSlider.Minimum = 0;
                if (VideoView.MediaPlayer.Media.Duration > 0)
                    this.VideoSlider.Maximum = VideoView.MediaPlayer.Media.Duration / 1000;
                else if (VideoView.MediaPlayer.Length > 0)
                    this.VideoSlider.Maximum = VideoView.MediaPlayer.Length / 1000;

                this.VideoSlider.Interval = 1000;

                if (_VideoPlaybackTimer != null && !_VideoPlaybackTimer.IsEnabled)
                {
                    _VideoPlaybackTimer.Start();

                }
                MessageBox.Show($"Duration: {VideoView.MediaPlayer.Media.Duration}, Length {VideoView.MediaPlayer.Length}");
            });
        }

        private static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
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

            if (!int.TryParse(inStringValue, out nResult))
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
                mTranscodeValue = "#transcode{" + kdefTranscodeText + "}";
            }
        }

        private void BtnSnapshot_Click(object sender, RoutedEventArgs e)
        {
            bool success = false;
            try
            {
                mLogger.AppendLine("***Before TakeSnapshot***");
                var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string destination = Path.Combine(currentDirectory, GenerateNewFileName(".png"));

                if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
                {
                    success = _mediaPlayer.TakeSnapshot(0, destination, 1280, 1024);
                }
                else if (_mediaPlayerRecord != null && _mediaPlayerRecord.IsPlaying)
                {
                    success = _mediaPlayerRecord.TakeSnapshot(0, destination, 1280, 1024);
                }
            }
            catch (Exception inException)
            {
                MessageBox.Show(inException.Message);
            }

            if (!success)
            {
                MessageBox.Show("Snapshot failed");
            }
            mLogger.AppendLine("***After TakeSnapshot***");
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ExecuteFullScreen();
        }

        bool _isFullScreen = false;
        WindowState oldWinState;
        WindowStyle oldWinStyle;

        private void ExecuteFullScreen()
        {
            _isFullScreen = !_isFullScreen;

            if (_isFullScreen)
            {
                stackPanel.Visibility = Visibility.Collapsed;
                gridSplitter.Visibility = Visibility.Collapsed;

                txtLogger.Visibility = Visibility.Collapsed;

                mainGrid.RowDefinitions.Remove(txtLoggerRowDef);
                mainGrid.ColumnDefinitions.Remove(StackPanelColDef);

                oldWinState = WindowState;
                oldWinStyle = WindowStyle;
                WindowState = WindowState.Maximized;
                WindowStyle = WindowStyle.None;
            }
            else
            {
                stackPanel.Visibility = Visibility.Visible;
                gridSplitter.Visibility = Visibility.Visible;

                txtLogger.Visibility = Visibility.Visible;

                mainGrid.RowDefinitions.Insert(1, txtLoggerRowDef);
                mainGrid.ColumnDefinitions.Insert(0, StackPanelColDef);

                WindowState = oldWinState;
                WindowStyle = oldWinStyle;
            }
        }

        private void makePopupFullScreen(Popup popup, FrameworkElement startFromElement)
        {
            for (var parent = startFromElement;
                 parent != null;
                 parent = VisualTreeHelper.GetParent(parent) as FrameworkElement)
            {
                if (parent.GetType().Name == "PopupRoot")
                {
                    parent.Width = System.Windows.SystemParameters.PrimaryScreenWidth;
                    parent.Height = System.Windows.SystemParameters.PrimaryScreenHeight;
                }
            }
        }

        private int GetSeekPercent()
        {
            int nPercent = 10;
            string seekPerecnt = txtSeekPercent.Text.Trim();
            if (string.IsNullOrEmpty(seekPerecnt))
            {
                txtSeekPercent.Text = "10";
                seekPerecnt = "10";
            }

            if (!Int32.TryParse(seekPerecnt, out nPercent))
            {
                nPercent = 10;
            }

            return nPercent;
        }

        private void Reverse_Click(object sender, RoutedEventArgs e)
        {
            int nPercent = GetSeekPercent();

            long length = this._mediaPlayer.Length;
            long timeMilleSeconds = length / nPercent;

            long prevPosition = this._mediaPlayer.Time - timeMilleSeconds;
            if (prevPosition >= 0)
            {
                this._mediaPlayer.Time = prevPosition;
            }
            else
            {
                this._mediaPlayer.Time = 0;
            }
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            int nPercent = GetSeekPercent();

            long length = this._mediaPlayer.Length;
            long timeMilleSeconds = length / nPercent;

            long nextPosition = this._mediaPlayer.Time + timeMilleSeconds;
            if (nextPosition <= length)
            {
                this._mediaPlayer.Time = nextPosition;
            }
            else
            {
                this._mediaPlayer.Time = length;
            }
        }

        private void VideoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            return;
            //if (cmbCaptureMode.SelectedIndex == 3)
            //{
            //    double newValueInSeconds = Math.Round((double)e.NewValue, 0);

            //    long timeMilleSeconds = (long)newValueInSeconds * 1000;

            //    if (VideoView.MediaPlayer.IsPlaying)
            //    {
            //        if (timeMilleSeconds < VideoView.MediaPlayer.Length && timeMilleSeconds < VideoView.MediaPlayer.Media.Duration)
            //        {
            //            VideoView.MediaPlayer.Time = timeMilleSeconds;
            //        }
            //        else
            //        {
            //            MessageBox.Show($"Seek value is {timeMilleSeconds}, Current value is : {VideoView.MediaPlayer.Time}, Video length : {VideoView.MediaPlayer.Length}, Media Duration : {VideoView.MediaPlayer.Media.Duration}");
            //        }
            //    }
            //}
        }

        #region-----------------------------INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        private void raisePropertyChanged(string propertyName)
        {
            System.ComponentModel.PropertyChangedEventHandler propertyChanged = this.PropertyChanged;
            if ((propertyChanged != null))
            {
                propertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        #endregion-----------------------------INotifyPropertyChanged Implementation

        private void VideoSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            userIsDraggingSlider = false;
        }

        private void VideoSlider_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (cmbCaptureMode.SelectedIndex == 3)
            {
                double newValueInSeconds = Math.Round((double)this.VideoSlider.Value, 0);

                long timeMilleSeconds = (long)newValueInSeconds * 1000;

                if (VideoView.MediaPlayer.IsPlaying)
                {
                    if (timeMilleSeconds < VideoView.MediaPlayer.Length && timeMilleSeconds < VideoView.MediaPlayer.Media.Duration)
                    {
                        //MessageBox.Show($"Seek value is {timeMilleSeconds}, Current value is : {VideoView.MediaPlayer.Time}, Video length : {VideoView.MediaPlayer.Length}, Media Duration : {VideoView.MediaPlayer.Media.Duration}");
                        VideoView.MediaPlayer.Time = timeMilleSeconds;
                    }
                    else
                    {
                        MessageBox.Show($"Seek value is {timeMilleSeconds}, Current value is : {VideoView.MediaPlayer.Time}, Video length : {VideoView.MediaPlayer.Length}, Media Duration : {VideoView.MediaPlayer.Media.Duration}");
                    }
                }
            }
        }

        private void VideoSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            userIsDraggingSlider = true;
        }

        private void txtFileName_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filename = txtFileName.Text;
        }
    }
}
