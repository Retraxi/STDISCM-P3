using LibVLCSharp.Shared;
using System;
using System.Windows;

namespace VideoPlayerApp
{
    public partial class MainWindow : Window
    {
        // LibVLC and MediaPlayer instances
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;

        public MainWindow()
        {
            InitializeComponent();
            Core.Initialize(); // Initializes the LibVLC instance
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC)
            {
                EnableHardwareDecoding = true,
                VideoOutput = videoView
            };
        }

        // Load Video button click handler
        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            // Open file dialog to select a video file
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "Video Files|*.mp4;*.avi;*.mkv";
            if (dialog.ShowDialog() == true)
            {
                var media = new Media(_libVLC, dialog.FileName, MediaFromType.FromFile);
                _mediaPlayer.Media = media;
                _mediaPlayer.Play();
                MessageBox.Show("Video Loaded Successfully!");
            }
        }

        // Preview for 10 seconds button click handler
        private void PlayPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer.Media != null)
            {
                _mediaPlayer.Play();
                // Wait for 10 seconds and then stop
                System.Threading.Tasks.Task.Delay(10000).ContinueWith(_ =>
                {
                    // Stop playback after 10 seconds
                    _mediaPlayer.Pause();
                    MessageBox.Show("Preview Complete!");
                });
            }
            else
            {
                MessageBox.Show("Please load a video first.");
            }
        }
    }
}