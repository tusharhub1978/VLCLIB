using Framework.Application.Logging;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Framework.Application.Presentation
{

    public class VideoResizeArgs
    {
        public VideoResizeArgs(bool inFullScreen, VideoCaptureModes inCaptureMode)
        {
            this.FullscreenMode = inFullScreen;
            this.CaptureMode = inCaptureMode;
        }

        public bool FullscreenMode { get; private set; }
        public VideoCaptureModes CaptureMode { get; private set; }
    }


    public class VideoPanelVLC : Control, IDisposable
    {
        private static ILogger sLogger = LoggerFactory.GetLogger(typeof(VideoPanelVLC));

        private const string kVideoGridName = "PART_videoGrid";
        private const string kVideoControl = "PART_VideoControl";
        private const string kMediaProgressPanelName = "PART_progressBarPanel";
        private const string kTopPanel = "PART_VideoTopPanel";

        // Lock object for locking and synchronizing the Video Stop Action
        private object VideoStopLockObject = new object();
        
        // Flag to determine if the Video Playback (VideFile) is already stopped
        private bool IsAlreadyStopped = false;

        private CommandBinding cbStopRecording;
        private CommandBinding cbPlay;
        private CommandBinding cbFullScreen;
        private CommandBinding cbGrabScreenshot;
        private CommandBinding cbGotoFrame;
        private CommandBinding cbSetPointingMode;

        private string[] kVLCOptions = new string[] {
            "--no-snapshot-preview",
            "--no-osd",
            ":avcodec-hw=d3d11va",
            "--no-video-title",
            "--no-audio" };

        private Guid _uniqueIdentifier = Guid.NewGuid();

        public Guid UniqueIdentifier
        {
            get { return _uniqueIdentifier; }
        }
        LibVLC _libVLC;

        private Grid _VideoGrid { get; set; }

        private VideoView _VideoControl;
        private VideoView VideoControl
        {
            get
            {
                return _VideoControl;
            }
            set
            {
                if (_VideoControl != null)
                {
                    _VideoControl.SizeChanged -= VideoControl_SizeChanged;
                }

                _VideoControl = value;

                if (_VideoControl != null)
                {
                    _VideoControl.SizeChanged += VideoControl_SizeChanged;
                }
            }
        }


        StringBuilder errorLog = new StringBuilder();

        public LibVLCSharp.Shared.MediaPlayer MediaPlayer
        {
            get { return ((VLCWrapper)this.VideoWrapper)._MediaPlayer; }
        }

        static VideoPanelVLC()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(VideoPanelVLC), new FrameworkPropertyMetadata(typeof(VideoPanelVLC)));
        }


        public VideoPanelVLC()
        {
            Core.Initialize();
            sLogger.Log("VLC Core.Initialize...", Category.Debug);

            cbStopRecording = new CommandBinding(VideoPanelCommands.StopRecordingCommand, ExecuteStopRecording, CanExecuteStopRecording);
            this.CommandBindings.Add(cbStopRecording);

            cbPlay = new CommandBinding(VideoPanelCommands.PlayCommand, ExecutePlay, CanExecutePlay);
            this.CommandBindings.Add(cbPlay);

            cbFullScreen = new CommandBinding(VideoPanelCommands.FullScreenCommand, ExecuteFullScreen, CanExecuteFullScreen);
            this.CommandBindings.Add(cbFullScreen);

            cbGrabScreenshot = new CommandBinding(VideoPanelCommands.GrabScreenshotCommand, ExecuteGrabScreenshot, CanExecuteGrabScreenshot);
            this.CommandBindings.Add(cbGrabScreenshot);

            cbGotoFrame = new CommandBinding(VideoPanelCommands.GotoFrameCommand, ExecuteGotoFrame, CanExecuteGotoFrame);
            this.CommandBindings.Add(cbGotoFrame);

            cbSetPointingMode = new CommandBinding(VideoPanelCommands.SetPointingModeCommand, ExecuteSetPointingMode, CanExecuteSetPointingMode);
            this.CommandBindings.Add(cbSetPointingMode);

            this.Loaded += VideoPanelVLC_Loaded;
            this.Unloaded += VideoPanelVLC_Unloaded;
        }

        private void VideoPanelVLC_Unloaded(object sender, RoutedEventArgs e)
        {
            sLogger.Log($"VideoPanelVLC_Unloaded: {_uniqueIdentifier}", Category.Debug);
            //Call stop when Unloaded
            this.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (this.VideoCaptureMode == VideoCaptureModes.ViewFile)
                        {
                            this.VideoAction = VideoActions.Stop;
                            CommandManager.InvalidateRequerySuggested();
                            this.VideoControl.Visibility = Visibility.Hidden;
                            this.VideoControl.InvalidateVisual();
                            this.VideoControl.UpdateLayout();
                            this.VideoControl.Visibility = Visibility.Visible;
                        }
                    }),
                    DispatcherPriority.Send,
                    null
                );
        }

        private void VideoPanelVLC_Loaded(object sender, RoutedEventArgs e)
        {
            sLogger.Log($"VideoPanelVLC_Loaded: {_uniqueIdentifier}", Category.Debug);


        }

        #region ====================================================== DEPENDENCY PROPERTIES

        #region ---------------------------------------------------------------- VideoCaptureMode

        public static readonly DependencyProperty VideoCaptureModeProperty =
                           DependencyProperty.Register("VideoCaptureMode",
                                                       typeof(VideoCaptureModes),
                                                       typeof(VideoPanelVLC),
                                                       new PropertyMetadata(VideoCaptureModes.None,
                                                                            new PropertyChangedCallback(OnVideoCaptureModeChanged),
                                                                            new CoerceValueCallback(CoerceVideoCaptureMode)));

        public VideoCaptureModes VideoCaptureMode
        {
            get
            {
                return (VideoCaptureModes)GetValue(VideoCaptureModeProperty);
            }
            set
            {
                SetValue(VideoCaptureModeProperty, value);
            }
        }

        private static void OnVideoCaptureModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {

            VideoPanelVLC vp = d as VideoPanelVLC;
            VideoCaptureModes current = (VideoCaptureModes)e.NewValue;


        }

        private static object CoerceVideoCaptureMode(DependencyObject d, object baseValue)
        {
            VideoPanelVLC vp = d as VideoPanelVLC;
            VideoCaptureModes current = (VideoCaptureModes)baseValue;
            if (vp.VideoAction != VideoActions.Stop)
            {
                // Playing...
                // cant change the mode
                current = vp.VideoCaptureMode;
            }

            return current;
        }

        #endregion ------------------------------------------------------------- VideoCaptureMode

        #region ---------------------------------------------------------------- VideoAction


        public static readonly DependencyProperty VideoActionProperty =
                           DependencyProperty.Register("VideoAction",
                                                       typeof(VideoActions),
                                                       typeof(VideoPanelVLC),
                                                       new PropertyMetadata(VideoActions.Stop,
                                                                            new PropertyChangedCallback(OnVideoActionChanged),
                                                                            new CoerceValueCallback(CoerceVideoAction)));

        public VideoActions VideoAction
        {
            get
            {
                return (VideoActions)this.GetValue(VideoActionProperty);
            }
            set
            {
                this.SetValue(VideoActionProperty, value);
            }
        }

        private static void OnVideoActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoPanelVLC vp = d as VideoPanelVLC;

            VideoActions newValue = (VideoActions)e.NewValue;
            VideoActions oldValue = (VideoActions)e.OldValue;

            switch (newValue)
            {
                case VideoActions.Play:
                    {
                        if (oldValue == VideoActions.Pause)
                        {
                            vp.resumePlay();
                        }
                        if (oldValue == VideoActions.Stop)
                        {
                            vp.start();
                        }
                    }
                    break;
                case VideoActions.Pause:

                    if (oldValue == VideoActions.Play)
                    {
                        vp.pausePlay();
                    }

                    break;

                case VideoActions.Stop:
                    if (oldValue == VideoActions.Play ||
                        oldValue == VideoActions.Pause)
                    {
                        vp.stop();
                    }

                    break;

                default:
                    break;

            }
        }

        private static object CoerceVideoAction(DependencyObject d, object value)
        {
            VideoPanelVLC vp = d as VideoPanelVLC;
            VideoActions current = (VideoActions)value;

            if (vp.VideoCaptureMode == VideoCaptureModes.ViewFile)
            {
                if (current == VideoActions.Play)
                {
                    if (!File.Exists(System.IO.Path.Combine(vp.VideoOutputPath, vp.VideoFileNameWithExt)))      // Check file exist
                        current = vp.VideoAction;           // Not allowed to change to Play
                }
            }
            else if (vp.VideoCaptureMode != VideoCaptureModes.None)
            {
                if (current == VideoActions.Play)
                {
                    //if ((vp.CamIndex >= 0) &&                    // Valid camera index
                    //    (vp.FrameScaningFPS > 0) &&        // Valid refresh interval
                    //    (vp.VideoCaptureMode == VideoCaptureModes.ViewOnly ? true : true))
                    //{ }
                    //else
                    //current = vp.VideoAction;
                }

            }

            return current;
        }

        #endregion ------------------------------------------------------------- VideoAction


        #region ---------------------------------------------------------------- VideoFileFormat

        public string VideoFileFormat
        {
            get { return (string)GetValue(VideoFileFormatProperty); }
            set { SetValue(VideoFileFormatProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VideoFileFormat.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VideoFileFormatProperty =
            DependencyProperty.Register("VideoFileFormat", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata(".wmv"));

        #endregion ------------------------------------------------------------- VideoFileFormat

        #region ---------------------------------------------------------------- SnapshotFormat

        public ImageFormat SnapshotFormat
        {
            get { return (ImageFormat)GetValue(SnapshotFormatProperty); }
            set { SetValue(SnapshotFormatProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SnapshotFormat.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SnapshotFormatProperty =
            DependencyProperty.Register("SnapshotFormat", typeof(ImageFormat), typeof(VideoPanelVLC), new PropertyMetadata(ImageFormat.Jpeg));

        #endregion ------------------------------------------------------------- SnapshotFormat

        #region ---------------------------------------------------------------- VideoDevice

        public string VideoDevice
        {
            get { return (string)GetValue(VideoDeviceProperty); }
            set { SetValue(VideoDeviceProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VideoDevice.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VideoDeviceProperty =
            DependencyProperty.Register("VideoDevice",
                                        typeof(string),
                                        typeof(VideoPanelVLC),
                                        new PropertyMetadata(new PropertyChangedCallback(VideoDeviceChanged)));

        private static void VideoDeviceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoPanelVLC vp = d as VideoPanelVLC;
            if (vp.VideoWrapper != null)
            {
                vp.VideoWrapper.ChangeVideoDevice((string)e.NewValue);
            }

        }

        #endregion ------------------------------------------------------------- VideoDevice

        #region ---------------------------------------------------------------- VirtualCameraName

        public string VirtualCameraName
        {
            get { return (string)GetValue(VirtualCameraNameProperty); }
            set { SetValue(VirtualCameraNameProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VirtualCameraName.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VirtualCameraNameProperty =
            DependencyProperty.Register("VirtualCameraName",
                                        typeof(string),
                                        typeof(VideoPanelVLC),
                                        new PropertyMetadata(new PropertyChangedCallback(VirtualCameraNameChanged)));

        private static void VirtualCameraNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        }

        #endregion ------------------------------------------------------------- VideoDevice

        #region ---------------------------------------------------------------- AudioDevice

        public string AudioDevice
        {
            get { return (string)GetValue(AudioDeviceProperty); }
            set { SetValue(AudioDeviceProperty, value); }
        }

        // Using a DependencyProperty as the backing store for AudioDevice.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AudioDeviceProperty =
            DependencyProperty.Register("AudioDevice", typeof(string), typeof(VideoPanelVLC),
            new PropertyMetadata(new PropertyChangedCallback(AudioDeviceChanged)));

        private static void AudioDeviceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoPanelVLC vp = d as VideoPanelVLC;
            if (vp.VideoWrapper != null)
                vp.VideoWrapper.ChangeAudioDevice((string)e.NewValue);
        }

        #endregion ------------------------------------------------------------- AudioDevice


        #region ---------------------------------------------------------------- VideoLength

        public TimeSpan VideoLength
        {
            get { return (TimeSpan)GetValue(VideoLengthProperty); }
            set { SetValue(VideoLengthProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VideoLength.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VideoLengthProperty =
            DependencyProperty.Register("VideoLength", typeof(TimeSpan), typeof(VideoPanelVLC), new PropertyMetadata(TimeSpan.FromDays(1)));


        #endregion ------------------------------------------------------------- VideoLength

        #region ---------------------------------------------------------------- CurrentTime

        public TimeSpan CurrentTime
        {
            get { return (TimeSpan)GetValue(CurrentTimeProperty); }
            set { SetValue(CurrentTimeProperty, value); }
        }

        // Using a DependencyProperty as the backing store for CurrentTime.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CurrentTimeProperty =
            DependencyProperty.Register("CurrentTime",
                                        typeof(TimeSpan),
                                        typeof(VideoPanelVLC),
                                        new PropertyMetadata(new TimeSpan(0), onCurrentTimeChanged));

        private static void onCurrentTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        }


        #endregion ------------------------------------------------------------- CurrentTime


        #region ---------------------------------------------------------------- SeekStarts

        public bool SeekStarts
        {
            get { return (bool)GetValue(SeekStartsProperty); }
            set { SetValue(SeekStartsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for CurrentTime.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SeekStartsProperty =
            DependencyProperty.Register("SeekStarts", typeof(bool), typeof(VideoPanelVLC), new PropertyMetadata(false, new PropertyChangedCallback(OnSeekStartsChanged)));

        private static void OnSeekStartsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            bool seekStarts = (bool)e.NewValue;
            VideoPanelVLC vp = d as VideoPanelVLC;

            if (vp.VideoAction == VideoActions.Stop)
                return;
        }

        #endregion ------------------------------------------------------------- SeekStarts


        #region ---------------------------------------------------------------- SeekToSeconds

        public double SeekToSeconds
        {
            get { return (double)GetValue(SeekToSecondsProperty); }
            set { SetValue(SeekToSecondsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for CurrentTime.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SeekToSecondsProperty =
            DependencyProperty.Register("SeekToSeconds", typeof(double), typeof(VideoPanelVLC), new PropertyMetadata(0.0, new PropertyChangedCallback(OnSeekToSecondsChanged), OnSeekToSecondsCoerce));

        private static object OnSeekToSecondsCoerce(DependencyObject d, object baseValue)
        {
            try
            {
                VideoPanelVLC vp = d as VideoPanelVLC;

                double newValueInSeconds = Math.Round((double)baseValue, 0);
                if (vp.VideoCaptureMode == VideoCaptureModes.ViewFile)
                {
                    if (vp.MediaPlayer != null & vp.MediaPlayer.Media != null)
                    {
                        long timeMilleSeconds = (long)newValueInSeconds * 1000;
                        if (timeMilleSeconds < vp.MediaPlayer.Length && timeMilleSeconds < vp.MediaPlayer.Media.Duration)
                        {
                            vp.MediaPlayer.Time = timeMilleSeconds;
                            vp.CurrentTime = TimeSpan.FromMilliseconds(timeMilleSeconds);
                            sLogger.Log($"Seek to seconds set: {timeMilleSeconds} >= Length {vp.MediaPlayer.Length}", Category.Debug);
                        }
                        else
                        {
                            sLogger.Log($"Seek to seconds skipped: {timeMilleSeconds} >= Length {vp.MediaPlayer.Length}", Category.Debug);
                        }
                    }
                }
            }
            catch (Exception inException)
            {
                sLogger.Log("Error in Seek to seconds", Category.Exception, inException);
            }

            return -1.0;
        }

        private static void OnSeekToSecondsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        }

        #endregion ------------------------------------------------------------- SeekToSeconds


        #region ---------------------------------------------------------------- VideoOutputPath

        public string VideoOutputPath
        {
            get { return (string)GetValue(VideoOutputPathProperty); }
            set { SetValue(VideoOutputPathProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VideoDirectory.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VideoOutputPathProperty =
            DependencyProperty.Register("VideoOutputPath", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata(string.Empty));


        #endregion ------------------------------------------------------------- VideoOutputPath

        #region ---------------------------------------------------------------- VideoFileNameWithExt DependencyProperty

        public static readonly DependencyProperty VideoFileNameWithExtProperty =
                           DependencyProperty.Register("VideoFileNameWithExt",
                                                       typeof(string),
                                                       typeof(VideoPanelVLC),
                                                       new PropertyMetadata("", new PropertyChangedCallback(OnVideoFileNameWithExtChanged)));

        public string VideoFileNameWithExt
        {
            get
            {
                return (string)GetValue(VideoFileNameWithExtProperty);
            }
            set
            {
                SetValue(VideoFileNameWithExtProperty, value);
            }
        }

        private static void OnVideoFileNameWithExtChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {

            VideoPanelVLC vp = d as VideoPanelVLC;
            CommandManager.InvalidateRequerySuggested();
            if (vp.VideoAction == VideoActions.Play ||
               vp.VideoAction == VideoActions.Pause)
            {
                vp.stop();
            }

        }

        #endregion ---------------------------------------------------------------- VideoFileNameWithExt DependencyProperty

        #region ---------------------------------------------------------------- ScreenShotPath DependencyProperty

        public string ScreenShotOutputPath
        {
            get { return (string)GetValue(ScreenShotOutputPathProperty); }
            set { SetValue(ScreenShotOutputPathProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SnapshotPath.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ScreenShotOutputPathProperty =
            DependencyProperty.Register("ScreenShotOutputPath", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata(""));

        #endregion ---------------------------------------------------------------- ScreenShotPath DependencyProperty

        #region ---------------------------------------------------------------- ScreenShotFilePrifix DependencyProperty

        public string ScreenShotFilePrifix
        {
            get { return (string)GetValue(ScreenShotFilePrifixProperty); }
            set { SetValue(ScreenShotFilePrifixProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ScreenShotFilePrifix.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ScreenShotFilePrifixProperty =
            DependencyProperty.Register("ScreenShotFilePrifix", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata("ScreenShot"));

        #endregion ---------------------------------------------------------------- ScreenShotPath DependencyProperty

        #region ---------------------------------------------------------------- VideoCaptureFormat

        public string VideoCaptureFormat
        {
            get { return (string)GetValue(VideoCaptureFormatProperty); }
            set { SetValue(VideoCaptureFormatProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VideoCaptureFormat.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VideoCaptureFormatProperty =
            DependencyProperty.Register("VideoCaptureFormat", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata("1280x1024 NV12, 12 bit"));

        #endregion ------------------------------------------------------------- VideoCaptureFormat

        #region ---------------------------------------------------------------- FrameRate

        public double FrameRate
        {
            get { return (double)GetValue(FrameRateProperty); }
            set { SetValue(FrameRateProperty, value); }
        }

        // Using a DependencyProperty as the backing store for FrameRate.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty FrameRateProperty =
            DependencyProperty.Register("FrameRate",
                                        typeof(double),
                                        typeof(VideoPanelVLC),
                                        new PropertyMetadata(24.0, new PropertyChangedCallback(FrameRateChanged)));

        private static void FrameRateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            //VideoPanelVLC vp = d as VideoPanelVLC;

            //if (vp.VideoWrapper.IsPreviewing)
            //{
            //    if (vp._IsPreviewing)
            //    {
            //        vp.VideoWrapper.StartPreview(vp.createVideoParameters(), null);
            //    }
            //}
        }


        #endregion ------------------------------------------------------------- FrameRate

        #region ---------------------------------------------------------------- FrameSize
        public System.Drawing.Size FrameSize
        {
            get { return (System.Drawing.Size)GetValue(FrameSizeProperty); }
            set { SetValue(FrameSizeProperty, value); }
        }

        // Using a DependencyProperty as the backing store for FrameSize.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty FrameSizeProperty =
            DependencyProperty.Register("FrameSize",
                                        typeof(System.Drawing.Size),
                                        typeof(VideoPanelVLC),
                                        new PropertyMetadata(new System.Drawing.Size(1280, 1024), new PropertyChangedCallback(FrameSizeChanged)));

        private static void FrameSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoPanelVLC vp = d as VideoPanelVLC;

            if (vp.VideoWrapper.IsPreviewing)
            {

                if (vp._IsPreviewing)
                {
                    vp.VideoWrapper.StartPreview(vp.createVideoParameters(), null);
                }
            }
        }


        #endregion ------------------------------------------------------------- FrameSize

        #region ---------------------------------------------------------------- BitRate



        public int BitRate
        {
            get { return (int)GetValue(BitRateProperty); }
            set { SetValue(BitRateProperty, value); }
        }

        // Using a DependencyProperty as the backing store for FrameSize.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty BitRateProperty =
            DependencyProperty.Register("BitRate",
                                        typeof(int),
                                        typeof(VideoPanelVLC),
                                        new PropertyMetadata(1500, new PropertyChangedCallback(BitRateChanged)));

        private static void BitRateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoPanelVLC vp = d as VideoPanelVLC;
        }


        #endregion ------------------------------------------------------------- BitRate

        #region ---------------------------------------------------------------- IsRecording


        public bool IsRecording
        {
            private set { SetValue(IsRecordingProperty, value); }
            get { return (bool)GetValue(IsRecordingProperty); }
        }

        // Using a DependencyProperty as the backing store for IsRecording.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsRecordingProperty =
            DependencyProperty.Register("IsRecording", typeof(bool), typeof(VideoPanelVLC), new PropertyMetadata(false));

        // public readonly DependencyProperty IsRecordingProperty = IsRecordingPropertyKey.DependencyProperty;


        #endregion ------------------------------------------------------------- IsRecording

        #region ---------------------------------------------------------------- IsFullScreen
        public bool IsFullScreen
        {
            set { this.SetValue(IsFullScreenProperty, value); }
            get { return (bool)this.GetValue(IsFullScreenProperty); }
        }

        // Using a DependencyProperty as the backing store for IsFullScreen.  This enables animation, styling, binding, etc...
        public static readonly DependencyPropertyKey IsFullScreenPropertyKey =
            DependencyProperty.RegisterReadOnly("IsFullScreen", typeof(bool), typeof(VideoPanelVLC), new PropertyMetadata(false));

        public readonly DependencyProperty IsFullScreenProperty = IsFullScreenPropertyKey.DependencyProperty;


        #endregion ------------------------------------------------------------- IsFullScreen



        #region ---------------------------------------------------------------- VLCLibCommonOptions

        public string VLCLibCommonOptions
        {
            get { return (string)GetValue(VLCLibCommonOptionsProperty); }
            set { SetValue(VLCLibCommonOptionsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VLCLibCommonOptions.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VLCLibCommonOptionsProperty =
            DependencyProperty.Register("VLCLibCommonOptions", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata(string.Empty));

        #endregion ------------------------------------------------------------- VLCLibCommonOptions

        #region ---------------------------------------------------------------- VLCMediaPreviewOptions

        public string VLCMediaPreviewOptions
        {
            get { return (string)GetValue(VLCMediaPreviewOptionsProperty); }
            set { SetValue(VLCMediaPreviewOptionsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VLCMediaPreviewOptions.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VLCMediaPreviewOptionsProperty =
            DependencyProperty.Register("VLCMediaPreviewOptions", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata(string.Empty));

        #endregion ------------------------------------------------------------- VLCMediaPreviewOptions

        #region ---------------------------------------------------------------- VLCMediaRecordOptions

        public string VLCMediaRecordOptions
        {
            get { return (string)GetValue(VLCMediaRecordOptionsProperty); }
            set { SetValue(VLCMediaRecordOptionsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VLCMediaRecordOptions.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VLCMediaRecordOptionsProperty =
            DependencyProperty.Register("VLCMediaRecordOptions", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata(string.Empty));

        #endregion ------------------------------------------------------------- VLCMediaRecordOptions

        #region ---------------------------------------------------------------- VLCTranscodeString

        public string VLCTranscodeString
        {
            get { return (string)GetValue(VLCTranscodeStringProperty); }
            set { SetValue(VLCTranscodeStringProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VLCTranscodeString.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VLCTranscodeStringProperty =
            DependencyProperty.Register("VLCTranscodeString", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata(string.Empty));

        #endregion ------------------------------------------------------------- VLCTranscodeString

        #region ---------------------------------------------------------------- VLCMediaPlaybackOptions

        public string VLCMediaPlaybackOptions
        {
            get { return (string)GetValue(VLCMediaPlaybackOptionsProperty); }
            set { SetValue(VLCMediaPlaybackOptionsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VLCMediaPlaybackOptions.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VLCMediaPlaybackOptionsProperty =
            DependencyProperty.Register("VLCMediaPlaybackOptions", typeof(string), typeof(VideoPanelVLC), new PropertyMetadata(string.Empty));

        #endregion ------------------------------------------------------------- VLCMediaPlaybackOptions


        #region ---------------------------------------------------------------- IsIDSTemplate

        /// <summary>
        /// Property to define if IDS Template is to be used
        /// </summary>
        public bool IsIDSTemplate
        {
            set { this.SetValue(IsIDSTemplateProperty, value); }
            get { return (bool)this.GetValue(IsIDSTemplateProperty); }
        }

        /// <summary>
        /// Using a DependencyProperty as the backing store for IsIDSTemplate.  This enables animation, styling, binding, etc... 
        /// </summary>
        public static readonly DependencyProperty IsIDSTemplateProperty =
            DependencyProperty.Register("IsIDSTemplate", typeof(bool), typeof(VideoPanelVLC), new PropertyMetadata(false));

        #endregion ------------------------------------------------------------- IsIDSTemplate

        #region ---------------------------------------------------------------- IsNormalTemplate

        /// <summary>
        /// Property to define if IDS Template is to be used
        /// </summary>
        public bool IsNormalTemplate
        {
            set { this.SetValue(IsNormalTemplateProperty, value); }
            get { return (bool)this.GetValue(IsNormalTemplateProperty); }
        }

        /// <summary>
        /// Using a DependencyProperty as the backing store for IsNormalTemplate.  This enables animation, styling, binding, etc... 
        /// </summary>
        public static readonly DependencyProperty IsNormalTemplateProperty =
            DependencyProperty.Register("IsNormalTemplate", typeof(bool), typeof(VideoPanelVLC), new PropertyMetadata(false));

        #endregion ------------------------------------------------------------- IsNormalTemplate


        #region ---------------------------------------------------------------- VideoPointingMode DependencyProperty


        public VideoPointingModes VideoPointingMode
        {
            get { return (VideoPointingModes)GetValue(VideoPointingModeProperty); }
            set { SetValue(VideoPointingModeProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VideoPointingMode.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VideoPointingModeProperty =
            DependencyProperty.Register("VideoPointingMode", typeof(VideoPointingModes), typeof(VideoPanelVLC), new PropertyMetadata(VideoPointingModes.None, new PropertyChangedCallback(VideoPointingModeChanged)));

        private static void VideoPointingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Not Implemented.
        }

        #endregion ---------------------------------------------------------------- VideoPointingMode DependencyProperty

        #region ---------------------------------------------------------------- PointerCommand DependencyProperty

        public ICommand PointerCommand
        {
            get { return (ICommand)GetValue(PointerCommandProperty); }
            set { SetValue(PointerCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for PointerCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PointerCommandProperty =
            DependencyProperty.Register("PointerCommand", typeof(ICommand), typeof(VideoPanelVLC), new PropertyMetadata(null));

        #endregion ---------------------------------------------------------------- PointerCommand DependencyProperty

        #region ---------------------------------------------------------------- VideoResizeCommand DependencyProperty

        public ICommand VideoResizeCommand
        {
            get { return (ICommand)GetValue(VideoResizeCommandProperty); }
            set { SetValue(VideoResizeCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VideoResizeCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VideoResizeCommandProperty =
            DependencyProperty.Register("VideoResizeCommand", typeof(ICommand), typeof(VideoPanelVLC), new PropertyMetadata(null));

        #endregion ---------------------------------------------------------------- VideoResizeCommand DependencyProperty


        #region ---------------------------------------------------------------- ControlPanelTemplate DependencyProperty

        public DataTemplate ControlPanelTemplate
        {
            get { return (DataTemplate)GetValue(ControlPanelTemplateProperty); }
            set { SetValue(ControlPanelTemplateProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ControlPanelTemplate.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ControlPanelTemplateProperty =
            DependencyProperty.Register("ControlPanelTemplate", typeof(DataTemplate), typeof(VideoPanelVLC), new PropertyMetadata(null));

        #endregion ---------------------------------------------------------------- ControlPanelTemplate DependencyProperty

        #region ---------------------------------------------------------------- ControlPanelTemplateSelector DependencyProperty

        public DataTemplateSelector ControlPanelTemplateSelector
        {
            get { return (DataTemplateSelector)GetValue(ControlPanelTemplateSelectorProperty); }
            set { SetValue(ControlPanelTemplateSelectorProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ControlPanelTemplateSelector.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ControlPanelTemplateSelectorProperty =
            DependencyProperty.Register("ControlPanelTemplateSelector", typeof(DataTemplateSelector), typeof(VideoPanelVLC), new PropertyMetadata(null));


        #endregion ---------------------------------------------------------------- ControlPanelTemplateSelector DependencyProperty

        #region ---------------------------------------------------------------- IDSControlPanelTemplate DependencyProperty

        /// <summary>
        /// Property for IDS COntrol Panel Template binding
        /// </summary>
        public DataTemplate IDSControlPanelTemplate
        {
            get { return (DataTemplate)GetValue(IDSControlPanelTemplateProperty); }
            set { SetValue(IDSControlPanelTemplateProperty, value); }
        }

        /// <summary>
        ///Using a DependencyProperty as the backing store for IDSControlPanelTemplate.  This enables animation, styling, binding, etc... 
        /// </summary>
        public static readonly DependencyProperty IDSControlPanelTemplateProperty =
            DependencyProperty.Register("IDSControlPanelTemplate", typeof(DataTemplate), typeof(VideoPanelVLC), new PropertyMetadata(null));

        #endregion ---------------------------------------------------------------- IDSControlPanelTemplate DependencyProperty

        #region ---------------------------------------------------------------- IDSControlPanelTemplateSelector DependencyProperty

        public DataTemplateSelector IDSControlPanelTemplateSelector
        {
            get { return (DataTemplateSelector)GetValue(IDSControlPanelTemplateSelectorProperty); }
            set { SetValue(IDSControlPanelTemplateSelectorProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IDSControlPanelTemplateSelector.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IDSControlPanelTemplateSelectorProperty =
            DependencyProperty.Register("IDSControlPanelTemplateSelector", typeof(DataTemplateSelector), typeof(VideoPanelVLC), new PropertyMetadata(null));


        #endregion ---------------------------------------------------------------- IDSControlPanelTemplateSelector DependencyProperty

        #region ---------------------------------------------------------------- ProgressBarTemplate DependencyProperty

        public DataTemplate ProgressBarTemplate
        {
            get { return (DataTemplate)GetValue(ProgressBarTemplateProperty); }
            set { SetValue(ProgressBarTemplateProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ProgressBarTemplate.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ProgressBarTemplateProperty =
            DependencyProperty.Register("ProgressBarTemplate", typeof(DataTemplate), typeof(VideoPanelVLC), new PropertyMetadata(null));

        #endregion ---------------------------------------------------------------- ProgressBarTemplate DependencyProperty


        #region ---------------------------------------------------------------- SupportedVideoDevices DependencyProperty


        public ObservableCollection<string> SupportedVideoDevices
        {
            get { return (ObservableCollection<string>)GetValue(SupportedVideoDevicesProperty); }
            set { SetValue(SupportedVideoDevicesProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SupportedVideoDevices.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SupportedVideoDevicesProperty =
            DependencyProperty.Register("SupportedVideoDevices", typeof(ObservableCollection<string>), typeof(VideoPanelVLC), new PropertyMetadata(null));

        public ObservableCollection<string> SupportedVideoFormats
        {
            get { return (ObservableCollection<string>)GetValue(SupportedVideoFormatsProperty); }
            set { SetValue(SupportedVideoFormatsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SupportedVideoFormats.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SupportedVideoFormatsProperty =
            DependencyProperty.Register("SupportedVideoFormats", typeof(ObservableCollection<string>), typeof(VideoPanelVLC), new PropertyMetadata(null));


        #endregion ---------------------------------------------------------------- SupportedVideoDevices DependencyProperty


        #region ---------------------------------------------------------------- Snapshots DependencyProperty


        public ObservableCollection<Snapshot> Snapshots
        {
            get { return (ObservableCollection<Snapshot>)GetValue(SnapshotsProperty); }
            set { SetValue(SnapshotsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Snapshots.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SnapshotsProperty =
            DependencyProperty.Register("Snapshots", typeof(ObservableCollection<Snapshot>), typeof(VideoPanelVLC), new PropertyMetadata(new ObservableCollection<Snapshot>()));


        #endregion ---------------------------------------------------------------- Snapshots DependencyProperty


        #region ---------------------------------------------------------------- VideoRectangle DependencyProperty

        public System.Windows.Size VideoRectangle
        {
            get
            {
                return (System.Windows.Size)GetValue(VideoRectangleProperty);
            }
            set { SetValue(VideoRectangleProperty, value); }
        }

        // Using a DependencyProperty as the backing store for VideoRectangle.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty VideoRectangleProperty =
            DependencyProperty.Register("VideoRectangle", typeof(System.Windows.Size), typeof(VideoPanelVLC),
            new PropertyMetadata(new System.Windows.Size(0.0, 0.0), OnVideoRectangleChanged));

        private static void OnVideoRectangleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        }

        #endregion ------------------------------------------------------------- VideoRectangle DependencyProperty


        #region ---------------------------------------------------------------- RecordingStoppedCommand DependencyProperty

        public ICommand RecordingStoppedCommand
        {
            get { return (ICommand)GetValue(RecordingStoppedCommandProperty); }
            set { SetValue(RecordingStoppedCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for RecordingStoppedCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty RecordingStoppedCommandProperty =
            DependencyProperty.Register("RecordingStoppedCommand", typeof(ICommand), typeof(VideoPanelVLC), new PropertyMetadata(null));

        #endregion ---------------------------------------------------------------- RecordingStoppedCommand DependencyProperty

        #region ---------------------------------------------------------------- ScreenShotFileNameCounter DependencyProperty

        public int ScreenShotFileNameCounter
        {
            get { return (int)GetValue(ScreenShotFileNameCounterProperty); }
            set { SetValue(ScreenShotFileNameCounterProperty, value); }
        }

        public static readonly DependencyProperty ScreenShotFileNameCounterProperty =
            DependencyProperty.Register("ScreenShotFileNameCounter", typeof(int), typeof(VideoPanelVLC), new PropertyMetadata(null));

        #endregion ---------------------------------------------------------------- ScreenShotFileNameCounter DependencyProperty

        #region ---------------------------------------------------------------- VideoFileNameCounter DependencyProperty

        public int VideoFileNameCounter
        {
            get { return (int)GetValue(VideoFileNameCounterProperty); }
            set { SetValue(VideoFileNameCounterProperty, value); }
        }

        public static readonly DependencyProperty VideoFileNameCounterProperty =
            DependencyProperty.Register("VideoFileNameCounter", typeof(int), typeof(VideoPanelVLC), new PropertyMetadata(null));

        #endregion ---------------------------------------------------------------- VideoFileNameCounter DependencyProperty

        #endregion =================================================== DEPENDENCY PROPERTIES

        #region ====================================================== Private Functions

        private DispatcherTimer _Timer;


        private IVideoWrapper _VideoWrapper;
        private IVideoWrapper VideoWrapper
        {
            get
            {
                if (_VideoWrapper == null)
                {
                    try
                    {
                        if (_libVLC == null)
                        {
                            _libVLC = new LibVLC(kVLCOptions);
                        }
                        _VideoWrapper = new VLCWrapper(VideoControl, _libVLC);
                    }
                    catch (Exception inException)
                    {
                        sLogger.Log($"Error in initializing VLC Video Wrapper... {_uniqueIdentifier}", Category.Exception, inException);
                        _VideoWrapper = new ErroredWrapper();
                    }
                }

                return _VideoWrapper;
            }
        }

        private void start()
        {
            if (IsDesignMode)
            {
                // Do not start videoCapture when in design mode
                return;
            }

            if (this.VideoCaptureMode == VideoCaptureModes.None)
                return;


            bool success = false;
            if (this.VideoCaptureMode == VideoCaptureModes.ViewFile)
            {
                this.IsAlreadyStopped = false;

                this.VideoControl.MediaPlayer.Playing -= MediaPlayer_Playing;
                this.VideoControl.MediaPlayer.Stopped -= MediaPlayer_Stopped;
                this.VideoControl.MediaPlayer.Playing += MediaPlayer_Playing;
                this.VideoControl.MediaPlayer.Stopped += MediaPlayer_Stopped;
                startFilePreview();
                success = true;
            }
            else if (this.VideoCaptureMode == VideoCaptureModes.ViewOnly)
            {
                VideoParameters vParams = createVideoParameters();

                success = VideoWrapper.StartPreview(vParams, null);
            }
            else if (this.VideoCaptureMode == VideoCaptureModes.ViewAndRecord)
            {
                VideoParameters vParams = createVideoParameters();

                success = VideoWrapper.StartRecording(vParams);
            }

            if (success)
            {
                // Start dispatchertimer
                _Timer = new DispatcherTimer();
                _Timer.Interval = TimeSpan.FromMilliseconds(10);
                _Timer.Tick += new EventHandler(TimeKeeper_Tick);
                _LastTickOn = DateTime.Now;
                _Timer.Start();
            }
            else
            {
                Debug.WriteLine(string.Format("Video failed!"));
            }
        }

        private void stop()
        {
            lock (VideoStopLockObject)
            {
                sLogger.Log("Stop VideoPanelVLC started..", Category.Debug);
                if (this.VideoCaptureMode == VideoCaptureModes.None || this.VideoControl == null || this.VideoControl.MediaPlayer == null || this.IsAlreadyStopped)
                {
                    sLogger.Log($"Stop VideoPanelVLC returend due to {VideoCaptureMode}, this.VideoControl == null || this.VideoControl.MediaPlayer == null || Already Stopped: {this.IsAlreadyStopped}", Category.Debug);
                    return;
                }

                if (_Timer != null && _Timer.IsEnabled)
                {
                    _Timer.Stop();
                    _Timer.Tick -= TimeKeeper_Tick;
                    _Timer = null;
                }

                this.CurrentTime = new TimeSpan(0);
                this.VideoLength = TimeSpan.FromDays(1);

                sLogger.Log($"Stop VideoPanelVLC {VideoCaptureMode}, {VideoAction} started..", Category.Debug);
                if (this.VideoCaptureMode == VideoCaptureModes.ViewFile)
                {
                    this.VideoControl.MediaPlayer.Stop();
                    this.IsAlreadyStopped = true;

                }
                else if (this.VideoCaptureMode != VideoCaptureModes.None)
                {
                    this.VideoWrapper.Stop();
                }
                sLogger.Log($"Stop VideoPanelVLC {VideoCaptureMode}, {VideoAction} ended..", Category.Debug);

                sLogger.Log("Stop VideoPanelVLC ended..", Category.Debug);
            }
        }

        // Applicable to PlayFileOnly
        private void pausePlay()
        {
            this.VideoControl.MediaPlayer.Pause();
            _Timer.IsEnabled = false;

        }

        // Applicable to PlayFileOnly
        private void resumePlay()
        {
            this.VideoControl.MediaPlayer.Play();
            this.SeekStarts = false;
            _LastTickOn = DateTime.Now;
            _Timer.IsEnabled = true;
        }

        private VideoParameters createVideoParameters()
        {
            VideoParameters vParams = new VideoParameters();
            // vParams.AudioDeviceName = this.AudioDeviceName;
            // vParams.VideoDeviceName = this.VideoDeviceName;
            vParams.VideoDeviceName = this.VideoDevice;
            vParams.VirtualCameraName = this.VirtualCameraName;

            vParams.FrameRate = this.FrameRate;
            vParams.FrameSize = this.FrameSize;
            vParams.BitRate = this.BitRate;
            vParams.ScreenShotFilePrifix = this.ScreenShotFilePrifix;
            vParams.ScreenShotOutputPath = this.ScreenShotOutputPath;
            vParams.SnapshotFormat = this.SnapshotFormat;

            vParams.VideoCaptureFormat = this.VideoCaptureFormat;
            vParams.VideoOutputPath = this.VideoOutputPath;
            vParams.VideoFileNameWithExt = getValidFileName(this.VideoOutputPath, this.VideoFileNameWithExt);

            vParams.VLCLibCommonOptions = this.VLCLibCommonOptions;
            vParams.VLCMediaPreviewOptions = this.VLCMediaPreviewOptions;
            vParams.VLCMediaRecordOptions = this.VLCMediaRecordOptions;
            vParams.VLCTranscodeString = this.VLCTranscodeString;
            vParams.VLCMediaPlaybackOptions = this.VLCMediaPlaybackOptions;

            return vParams;
        }

        bool _IsPreviewing = false;
        private void startFilePreview()
        {
            try
            {
                if (string.IsNullOrEmpty(this.VideoFileNameWithExt))
                {
                    throw new ArgumentException("VideoFileNameWithExt NOT specified!");
                }

                string filePath = this.VideoOutputPath;
                if (string.IsNullOrEmpty(filePath))
                {
                    // Try finding file in the application directory
                    Debug.WriteLine("startFilePreview(): VideoOutputPath is null or empty, using application base directory to find the file.");
                    filePath = System.AppDomain.CurrentDomain.BaseDirectory;
                }

                string fileName = System.IO.Path.Combine(this.VideoOutputPath, this.VideoFileNameWithExt);
                if (!System.IO.File.Exists(fileName))
                {
                    // Throw exception if does not exist
                    Debug.WriteLine("startFilePreview(): File not found " + fileName);
                    throw new FileNotFoundException(string.Format("File [{0}] does not exist!", fileName));
                }

                VideoParameters vParams = createVideoParameters();

                _IsPreviewing = ((VLCWrapper)VideoWrapper).startFilePreview(fileName, vParams);
            }
            catch (Exception inException)
            {
                sLogger.Log($"Error occured in Start File preview {_uniqueIdentifier}", Category.Exception, inException);

                throw inException;
            }
        }
        private void stopFilePreview()
        {
        }

        private DateTime _LastTickOn;
        private void TimeKeeper_Tick(object sender, EventArgs e)
        {
            TimeSpan diff = (DateTime.Now.Subtract(_LastTickOn));

            if (this.VideoCaptureMode == VideoCaptureModes.ViewFile)
            {
                this.CurrentTime = TimeSpan.FromMilliseconds(this.VideoControl.MediaPlayer.Time);
            }
            else if(this.VideoCaptureMode != VideoCaptureModes.None)
            {
                this.CurrentTime = TimeSpan.FromMilliseconds(this.VideoControl.MediaPlayer.Time);
            }

            _LastTickOn = DateTime.Now;
        }

        private bool IsDesignMode
        {
            get
            {
                return DesignerProperties.GetIsInDesignMode(this);
            }
        }

        private string getValidFileName(string inDir, string inFileName)
        {
            int cntr = VideoFileNameCounter;

            while (true)
            {
                string fileName = inFileName;
                string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                string fileExt = System.IO.Path.GetExtension(fileName);
                if (cntr > 0)
                {
                    fileName = string.Format("{0}_{1}{2}", fileNameWithoutExt, cntr, fileExt);
                }

                string filePath = System.IO.Path.Combine(inDir, fileName);
                if (!System.IO.File.Exists(filePath))
                {
                    if (this.VideoCaptureMode == VideoCaptureModes.ViewAndRecord)
                    {
                        VideoFileNameCounter++;
                    }
                    return filePath;
                }
                cntr++;
            }
        }


        #endregion =================================================== Private Functions

        #region ======================================================== COMMAND HANDLERS

        private void CanExecutePlay(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;

            if (this.VideoAction == VideoActions.Stop)
            {
                switch (this.VideoCaptureMode)
                {
                    case VideoCaptureModes.ViewOnly:
                    case VideoCaptureModes.ViewAndRecord:
                    case VideoCaptureModes.ViewFile:
                        {
                            break;
                        }
                    default:
                        e.CanExecute = false;
                        break;
                }
            }
        }

        private void ExecutePlay(object sender, ExecutedRoutedEventArgs e)
        {
            if (this.VideoCaptureMode == VideoCaptureModes.ViewFile)
            {
                if (this.VideoAction == VideoActions.Stop ||
                    this.VideoAction == VideoActions.Pause)
                {
                    this.VideoAction = VideoActions.Play;
                }
                else if (this.VideoAction == VideoActions.Play)
                {
                    this.VideoAction = VideoActions.Pause;
                }
            }
            else if (this.VideoCaptureMode == VideoCaptureModes.ViewOnly ||
                this.VideoCaptureMode == VideoCaptureModes.ViewAndRecord)
            {
                if (this.VideoAction == VideoActions.Stop)
                {
                    this.VideoAction = VideoActions.Play;
                }
                else if (this.VideoAction == VideoActions.Play)
                {
                    this.VideoAction = VideoActions.Stop;
                }
            }
        }

        private void CanExecuteStopRecording(object sender, CanExecuteRoutedEventArgs e)
        {

            e.CanExecute = false;

            if (this.VideoAction == VideoActions.Play)
            {
                switch (this.VideoCaptureMode)
                {
                    case VideoCaptureModes.ViewAndRecord:
                        e.CanExecute = true;
                        break;
                }
            }
        }

        private void ExecuteStopRecording(object sender, ExecutedRoutedEventArgs e)
        {
            if (this.VideoCaptureMode == VideoCaptureModes.ViewAndRecord)
            {
                if (this.VideoAction == VideoActions.Play)
                {
                    this.VideoAction = VideoActions.Stop;

                    this.VideoCaptureMode = VideoCaptureModes.ViewOnly;

                    this.VideoAction = VideoActions.Play;

                    fireRecordingStoppedCommand();
                }
            }
        }

        private void fireRecordingStoppedCommand()
        {
            ICommand recordingStoppedCmd = RecordingStoppedCommand as ICommand;
            if (recordingStoppedCmd == null)
                return;

            if (recordingStoppedCmd.CanExecute(null))
            {
                recordingStoppedCmd.Execute(null);
            }
        }

        private void fireVideoResizeCommand(VideoPanelVLC inVidepPanel, bool inFullScreen)
        {
            ICommand videoResizeCmd = inVidepPanel.VideoResizeCommand as ICommand;
            if (videoResizeCmd == null)
                return;

            VideoResizeArgs args = new VideoResizeArgs(inFullScreen, this.VideoCaptureMode);

            if (videoResizeCmd.CanExecute(args))
            {
                videoResizeCmd.Execute(args);
            }
        }


        private void CanExecuteFullScreen(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        bool _isFullScreen = false;
        private void ExecuteFullScreen(object sender, ExecutedRoutedEventArgs e)
        {

            try
            {
                if (e == null ||
                    e.OriginalSource == null ||
                    !(e.OriginalSource is VideoPanelVLC))
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    VideoPanelVLC vp = e.OriginalSource as VideoPanelVLC;
                    bool isOpenFullScreenPopup = !vp._isFullScreen;
                    vp.fireVideoResizeCommand(vp, isOpenFullScreenPopup);

                    vp._isFullScreen = isOpenFullScreenPopup;
                }), System.Windows.Threading.DispatcherPriority.Send);
            }
            catch (Exception inException)
            {
                sLogger.Log($"Error in FullScreen: ", Category.Exception, inException);
                throw inException;
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

        private void CanExecuteGrabScreenshot(object sender, CanExecuteRoutedEventArgs e)
        {
            // 
            if (e == null ||
                e.OriginalSource == null ||
                !(e.OriginalSource is VideoPanelVLC))
            {
                e.CanExecute = false;
                return;
            }

            VideoPanelVLC vp = e.OriginalSource as VideoPanelVLC;

            e.CanExecute = (vp.VideoAction == VideoActions.Play &&
                            Directory.Exists(vp.ScreenShotOutputPath) &&
                            (!string.IsNullOrEmpty(vp.ScreenShotFilePrifix)));

            return;
        }

        private void ExecuteGrabScreenshot(object sender, ExecutedRoutedEventArgs e)
        {
            if (e == null ||
                e.OriginalSource == null ||
                !(e.OriginalSource is VideoPanelVLC))
            {
                return;
            }

            VideoPanelVLC vp = e.OriginalSource as VideoPanelVLC;
            int screenshotCounter = vp.ScreenShotFileNameCounter;
            screenshotCounter++;

            if (vp.VideoWrapper.CaptureFrame(null, screenshotCounter))
            {
                string filename = ImageExt.GenerateThumbnail(vp.ScreenShotOutputPath,
                                           vp.VideoWrapper.LastScreenshotFileName);

                if (!string.IsNullOrEmpty(filename))
                {
                    // Add the file to generated snaps
                    vp.Snapshots.Insert(0,
                                    new Snapshot
                                        (vp.ScreenShotOutputPath,
                                         vp.VideoWrapper.LastScreenshotFileName,
                                         vp.CurrentTime));
                }
            }
            vp.ScreenShotFileNameCounter = screenshotCounter;
        }

        private void CanExecuteSetPointingMode(object sender, CanExecuteRoutedEventArgs e)
        {

            if (e == null ||
                e.OriginalSource == null ||
                !(e.OriginalSource is VideoPanelVLC))
            {
                e.CanExecute = false;
                return;
            }

            VideoPanelVLC vp = e.OriginalSource as VideoPanelVLC;

            // 
            e.CanExecute = (vp.VideoAction == VideoActions.Play &&
                            e.Parameter != null &&
                            e.Parameter is VideoPointingModes);
        }

        private void ExecuteSetPointingMode(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                if (e == null ||
                    e.OriginalSource == null ||
                    !(e.OriginalSource is VideoPanelVLC))
                {
                    return;
                }

                VideoPanelVLC vp = e.OriginalSource as VideoPanelVLC;

                VideoPointingModes mode = (VideoPointingModes)e.Parameter;
                // done for enabling toggle button like behaviour
                if (vp.VideoPointingMode == mode)
                {
                    // if the mode set is same i.e. clicked on same button twice consecutively 
                    // swicth the mode back to None.
                    vp.VideoPointingMode = VideoPointingModes.None;
                }
                else
                {
                    // if clicked on different buttons consecutively
                    vp.VideoPointingMode = mode;
                }
            }
            catch (Exception inException)
            {
                sLogger.Log($"Error in ExecuteSetPointingMode", Category.Exception, inException);
            }
        }

        private void CanExecuteGotoFrame(object sender, CanExecuteRoutedEventArgs e)
        {
            if (e == null ||
                e.Source == null ||
                !(e.Source is VideoPanelVLC))
            {
                return;
            }

            VideoPanelVLC vp = e.Source as VideoPanelVLC;

            e.CanExecute = (vp.VideoAction == VideoActions.Play);
        }

        private void ExecuteGotoFrame(object sender, ExecutedRoutedEventArgs e)
        {
        }

        #endregion ===================================================== COMMAND HANDLERS

        public override void OnApplyTemplate()
        {
            sLogger.Log($"VideoPanelVLC: OnApplyTemplate!!, Thead ID :", Category.Debug);
            base.OnApplyTemplate();
            InitializeControls();
        }

        private void InitializeControls()
        {
            if (_libVLC == null)
            {
                _libVLC = new LibVLC(kVLCOptions);
            }

            sLogger.Log("VideoPanelVLC: InitializeControls!!", Category.Debug);
            this.VideoControl = this.GetTemplateChild(kVideoControl) as VideoView;

            _VideoGrid = this.GetTemplateChild(kVideoGridName) as Grid;

            this.SupportedVideoDevices = new ObservableCollection<string>();
            List<string> devList = this.VideoWrapper.GetDevices(MediaDeviceTypes.Video);
            if (devList != null)
            {
                foreach (string item in devList)
                {
                    this.SupportedVideoDevices.Add(item);
                }
            }

            this.SupportedVideoFormats = new ObservableCollection<string>();
        }


        void VideoControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            VideoPanelVLC vp = this;
            if (vp != null)
            {
                recalculateSize(vp);
            }
        }

        void recalculateSize(VideoPanelVLC inVideoPanelVF)
        {
            System.Windows.Size availableSize = new System.Windows.Size(VideoControl.ActualWidth, VideoControl.ActualHeight);
            this.VideoRectangle = availableSize;
        }

        private static System.Drawing.Size ExpandToBound(System.Drawing.Size image, System.Drawing.Size boundingBox)
        {
            double widthScale = 0, heightScale = 0;
            if (image.Width != 0)
                widthScale = (double)boundingBox.Width / (double)image.Width;
            if (image.Height != 0)
                heightScale = (double)boundingBox.Height / (double)image.Height;

            double scale = Math.Min(widthScale, heightScale);

            System.Drawing.Size result = new System.Drawing.Size((int)(image.Width * scale),
                                                                 (int)(image.Height * scale));
            return result;
        }

        private int GCD(int a, int b)
        {
            return b == 0 ? a : GCD(b, a % b);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    CleanUp();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
                GC.Collect();
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~VideoPanelVLC() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            //GC.SuppressFinalize(this);
        }
        #endregion
        private void MediaPlayer_Stopped(object sender, EventArgs e)
        {
            //<ToDo: Need to do something about this>
            this.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (this.VideoCaptureMode == VideoCaptureModes.ViewFile)
                        {
                            this.VideoAction = VideoActions.Stop;
                            CommandManager.InvalidateRequerySuggested();
                            this.VideoControl.Visibility = Visibility.Hidden;
                            this.VideoControl.InvalidateVisual();
                            this.VideoControl.UpdateLayout();
                            this.VideoControl.Visibility = Visibility.Visible;
                        }
                    }),
                    DispatcherPriority.Send,
                    null
                );
        }

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            //<ToDo: Need to do something about this>
            this.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (this.VideoCaptureMode == VideoCaptureModes.ViewFile)
                        {
                            this.VideoLength = TimeSpan.FromMilliseconds(this.VideoControl.MediaPlayer.Length);
                        }
                    }),
                    DispatcherPriority.Send,
                    null
                );
        }

        private void CleanUp()
        {
            sLogger.Log($"VideoPanelVLC: Dispose called for : {_uniqueIdentifier}!!", Category.Debug);

            this.VideoCaptureMode = VideoCaptureModes.None;

            if (_Timer != null)
            {
                if (_Timer.IsEnabled)
                    _Timer.Stop();
                _Timer.Tick -= TimeKeeper_Tick;
                _Timer = null;
            }

            if (VideoControl != null)
            {
                this.VideoControl.MediaPlayer.Playing -= MediaPlayer_Playing;
                this.VideoControl.MediaPlayer.Stopped -= MediaPlayer_Stopped;
                VideoControl.Dispose();
                VideoControl = null;
                sLogger.Log("VideoControl reference released!!", Category.Debug);
            }

            this.Loaded -= VideoPanelVLC_Loaded;
            this.Unloaded -= VideoPanelVLC_Unloaded;
            if (_VideoWrapper != null)
            {
                _VideoWrapper.Dispose();
                _VideoWrapper = null;
                sLogger.Log("VideoWrapper disposed!!", Category.Debug);
            }

            if (_libVLC != null)
            {
                _libVLC.Dispose();
                _libVLC = null;
                sLogger.Log("libVLC disposed!!", Category.Debug);
            }

            this.CommandBindings.Remove(cbStopRecording);
            this.CommandBindings.Remove(cbPlay);
            this.CommandBindings.Remove(cbFullScreen);
            this.CommandBindings.Remove(cbGrabScreenshot);
            this.CommandBindings.Remove(cbGotoFrame);
            this.CommandBindings.Remove(cbSetPointingMode);

            this.CommandBindings.Clear();
        }
    }
}
