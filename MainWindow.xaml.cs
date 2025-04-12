using LibVLCSharp.Shared;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VideoPlayerApp
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<VideoFile> _videoFiles;
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private DispatcherTimer _timer;
        private bool _isSeeking = false;
        private DispatcherTimer _folderScanTimer;
        private DispatcherTimer _previewTimer;
        private int _previewTimeLeft;
        private object? _currentlyHoveredItem;
        private bool _suppressMouseLeave = false;
        private bool _suppressMouseEnter = false;
        private HashSet<string> _pendingFiles = new();

        // Folder to monitor for video files
        private const string VideoFolderPath = @"C:\Users\Rapha\Source\Repos\STDISCM-P3\UploadedVideos\";

        public MainWindow()
        {
            InitializeComponent();
            Core.Initialize();

            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            VlcVideoView.MediaPlayer = _mediaPlayer; 

            _videoFiles = new ObservableCollection<VideoFile>();
            VideoListView.ItemsSource = _videoFiles;

            // Timer to update seek bar and time
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += Timer_Tick;

            VolumeSlider.Value = 100;
            _mediaPlayer.Volume = 100;

            LoadExistingFiles();

            _folderScanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5) // Scan every 5 seconds
            };
            _folderScanTimer.Tick += FolderScanTimer_Tick;
            _folderScanTimer.Start();
        }

        // Load all video files that already exist in the folder
        private void LoadExistingFiles()
        {
            var videoFiles = Directory.GetFiles(VideoFolderPath, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var filePath in videoFiles)
            {
                if (IsValidVideoFile(filePath)) // Check if the file is a valid video
                {
                    _videoFiles.Add(new VideoFile { FileName = Path.GetFileName(filePath), FilePath = filePath });
                }
            }
        }

        // Check if the file is a valid video file based on extension
        private bool IsValidVideoFile(string filePath)
        {
            string[] validExtensions = { ".mp4", ".avi", ".mkv" };
            string fileExtension = Path.GetExtension(filePath).ToLower();
            return Array.Exists(validExtensions, ext => ext == fileExtension);
        }

        // Called when a new file is added to the folder
        private void FolderScanTimer_Tick(object sender, EventArgs e)
        {
            //var files = Directory.GetFiles(VideoFolderPath, "*.*", SearchOption.TopDirectoryOnly);
            //foreach (var file in files)
            //{
            //    if (IsValidVideoFile(file) && !_videoFiles.Any(v => v.FilePath == file))
            //    {
            //        if (IsFileReady(file))
            //        {
            //            _videoFiles.Add(new VideoFile
            //            {
            //                FileName = Path.GetFileName(file),
            //                FilePath = file
            //            });
            //        }
            //    }
            //}
            var files = Directory.GetFiles(VideoFolderPath);
            foreach (var file in files)
            {
                if (IsValidVideoFile(file) && !_videoFiles.Any(v => v.FilePath == file))
                {
                    if (IsFileReady(file))
                    {
                        _videoFiles.Add(new VideoFile
                        {
                            FileName = Path.GetFileName(file),
                            FilePath = file
                        });
                        _pendingFiles.Remove(file); // Remove if previously pending
                    }
                    else
                    {
                        _pendingFiles.Add(file);
                    }
                }
            }
        }

        private bool IsFileReady(string path)
        {
            try
            {
                long initialSize = new FileInfo(path).Length;
                Thread.Sleep(500); // Give it a moment
                long laterSize = new FileInfo(path).Length;

                return initialSize == laterSize && initialSize > 0;
            }
            catch
            {
                return false;
            }
        }
        private CancellationTokenSource _previewCts;

        private void ListViewItem_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_suppressMouseEnter)
            {
                // Get the ListViewItem under the mouse
                var item = GetListViewItemUnderMouse(VideoListView, e.GetPosition(VideoListView));

                // Check if the item exists and has a valid DataContext (VideoFile)
                if (item == null || item.DataContext == null) return;

                var videoFile = item.DataContext as VideoFile;
                if (videoFile == null) return;

                if (_currentlyHoveredItem != item.DataContext)
                {
                    _currentlyHoveredItem = item.DataContext;
                    // Start video preview for the hovered file
                    StartVideoPreview(videoFile.FilePath);
                }
            }

        }

        private void ListViewItem_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if(!_suppressMouseLeave)
            {
                _currentlyHoveredItem = null;
                StopVideoPreview();
            }
        }



        private async void StartVideoPreview(string filePath)
        {
            StopVideoPreview(); // cancel existing preview if any

            _previewCts = new CancellationTokenSource();

            _previewTimeLeft = 10;
            PreviewTimerTextBlock.Text = "10";
            PreviewTimerTextBlock.Visibility = Visibility.Visible;

            _previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _previewTimer.Tick += (s, e) =>
            {
                _previewTimeLeft--;
                PreviewTimerTextBlock.Text = _previewTimeLeft.ToString();
                if (_previewTimeLeft <= 0)
                {
                    _previewTimer.Stop();
                    PreviewTimerTextBlock.Visibility = Visibility.Collapsed;
                }
            };
            _previewTimer.Start();

            try
            {
                var media = new Media(_libVLC, filePath, FromType.FromPath);
                _mediaPlayer.Media = media;
                _mediaPlayer.Play();

                await Task.Delay(10000, _previewCts.Token); // 10 seconds
                _mediaPlayer.Pause();
            }
            catch (TaskCanceledException)
            {
                // Handle early cancellation
            }
        }

        private void StopVideoPreview()
        {
            if (_previewCts != null && !_previewCts.IsCancellationRequested)
            {
                _previewCts.Cancel();
                _previewCts.Dispose();
                _previewCts = null;
            }

            _previewTimer?.Stop();
            PreviewTimerTextBlock.Visibility = Visibility.Collapsed;
            _mediaPlayer.Stop();
            _mediaPlayer.Media= null;
        }

        // Helper to get item under mouse
        private ListViewItem? GetListViewItemUnderMouse(ListView listView, Point position)
        {
            var hit = VisualTreeHelper.HitTest(listView, position);
            DependencyObject? obj = hit?.VisualHit;
            while (obj != null && !(obj is ListViewItem))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }
            return obj as ListViewItem;
        }

        private void ListViewItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _suppressMouseLeave = true;
            _suppressMouseEnter = true;
            if (ControlSection.Visibility == Visibility.Collapsed)
            {
                ControlSection.Visibility = Visibility.Visible;
            } else
            {
                ControlSection.Visibility = Visibility.Collapsed;
            }

            var item = GetListViewItemUnderMouse(VideoListView, e.GetPosition(VideoListView));
            var videoFile = item.DataContext as VideoFile;
            var filePath = videoFile.FilePath;
            try
            {
                if (File.Exists(filePath))
                {
                    // Stop current media if any
                    StopVideoPreview();

                    // Play new media
                    using var media = new Media(_libVLC, new Uri(filePath));
                    _mediaPlayer.Play(media);
                }

                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(1000); // Wait for double-click to complete
                    _suppressMouseLeave = false;
                    _suppressMouseEnter = false;
                });
            }
            catch (TaskCanceledException)
            {
                // Handle early cancellation
            }
        }

        // 🔁 Update seek bar and time display
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsPlaying || _isSeeking)
                return;

            long duration = _mediaPlayer.Length;
            long time = _mediaPlayer.Time;

            if (duration > 0)
            {
                SeekBar.Value = (double)time / duration * 100;
                CurrentTimeText.Text = TimeSpan.FromMilliseconds(time).ToString(@"m\:ss");
                TotalTimeText.Text = TimeSpan.FromMilliseconds(duration).ToString(@"m\:ss");
            }
        }

        // ▶️ Play
        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer.Play();
            _timer.Start();
        }

        // ⏸ Pause
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer.Pause();
        }

        // ⏹ Stop
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer.Stop();
            _timer.Stop();
            SeekBar.Value = 0;
            CurrentTimeText.Text = "0:00";
        }

        // 🔊 Volume
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer != null)
                _mediaPlayer.Volume = (int)e.NewValue;
        }

        // ⏩ Seek
        private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSeeking && _mediaPlayer.Length > 0)
            {
                long newTime = (long)(_mediaPlayer.Length * (SeekBar.Value / 100));
                _mediaPlayer.Time = newTime;
                CurrentTimeText.Text = TimeSpan.FromMilliseconds(newTime).ToString(@"m\:ss");
            }
        }

        // Handle mouse interaction to avoid jittery seek
        private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
        }

        private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = false;
            SeekBar_ValueChanged(sender, null);
        }
    }

    // VideoFile class to store file name and file path
    public class VideoFile
    {
        public required string FileName { get; set; }
        public required string FilePath { get; set; }
    }
}
