using LibVLCSharp.Shared;
using System;
using System.Windows;
using System.Windows.Controls;

namespace WpfVLCVidepLanPOC
{
    public partial class Controls : UserControl
    {
        readonly Example1 parent;
        LibVLC _libVLC;
        MediaPlayer _mediaPlayer;

        public Controls(Example1 Parent)
        {
            Core.Initialize();
            parent = Parent;

            InitializeComponent();

            // we need the VideoView to be fully loaded before setting a MediaPlayer on it.
            parent.VideoView.Loaded += VideoView_Loaded;
            PlayButton.Click += PlayButton_Click;
            StopButton.Click += StopButton_Click;
            Unloaded += Controls_Unloaded;
        }

        private void Controls_Unloaded(object sender, RoutedEventArgs e)
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _libVLC.Dispose();
        }

        private void VideoView_Loaded(object sender, RoutedEventArgs e)
        {
            _libVLC = new LibVLC(enableDebugLogs: true);
            _mediaPlayer = new MediaPlayer(_libVLC);

            parent.VideoView.MediaPlayer = _mediaPlayer;
        }

        void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (parent.VideoView.MediaPlayer.IsPlaying)
            {
                parent.VideoView.MediaPlayer.Stop();
            }
        }

        void PlayButton_Click(object sender, RoutedEventArgs e)
        {

            try
            {
                if (!parent.VideoView.MediaPlayer.IsPlaying)
                {
                    //    using (var media = new Media(_libVLC, new Uri("http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4")))
                    //        parent.VideoView.MediaPlayer.Play(media);
                    //}

                    //string CatureOptions = @"dshow:// :dshow-vdev=SA7160 PCI, Analog Capture (#01) :dshow-adev=none  :live-caching=300";
                    using (var media = new Media(_libVLC, "dshow:// ", FromType.FromLocation))
                    {

                        media.AddOption(":dshow-vdev=Integrated Camera");
                        media.AddOption(":dshow-config");
                        media.AddOption(":dshow-fps=10 ");

                        //media.AddOption(":dshow-vdev=SA7160 PCI, Analog Capture (#01)");
                        //media.AddOption(":dshow-adev=none");
                        //media.AddOption(":live-caching=300");
                        //media.AddOption(":dshow-fps=30 ");
                        if (!parent.VideoView.MediaPlayer.Play(media))
                        {
                            MessageBox.Show("Error in Playing...");
                        }
                    }
                }
            }
            catch (Exception inException)
            {
                MessageBox.Show(inException.Message);
            }
        }
    }
}
