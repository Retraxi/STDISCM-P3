using LibVLCSharp.Shared;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VideoPlayerApp
{
    public partial class MainWindow : Window
    {
        private FileSystemWatcher _fileSystemWatcher;
        private ObservableCollection<VideoFile> _videoFiles;
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;

        // Folder to monitor for video files
        private const string VideoFolderPath = @"C:\Users\Rapha\Source\Repos\STDISCM-P3\UploadedVideos\";

        public MainWindow()
        {
            InitializeComponent();
            Core.Initialize();  // Initialize LibVLC

            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);

            _videoFiles = new ObservableCollection<VideoFile>();
            VideoListView.ItemsSource = _videoFiles;

            VlcVideoView.MediaPlayer = _mediaPlayer;

            // Initialize FileSystemWatcher for real-time monitoring
            _fileSystemWatcher = new FileSystemWatcher(VideoFolderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                Filter = "*.mp4;*.avi;*.mkv"  // File extensions you want to watch
            };

            _fileSystemWatcher.Created += FileSystemWatcher_Created;
            _fileSystemWatcher.EnableRaisingEvents = true;

            // Load existing video files when the app starts
            LoadExistingFiles();
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
        private void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            // Make sure this runs on the UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Add the new video file to the list
                if (IsValidVideoFile(e.FullPath))
                {
                    _videoFiles.Add(new VideoFile { FileName = Path.GetFileName(e.FullPath), FilePath = e.FullPath });
                }
            });
        }

        // Handle video selection in the ListView
        private void VideoListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (VideoListView.SelectedItem is VideoFile selectedFile)
            {
                PlayVideo(selectedFile.FilePath);
            }
        }

        // Play the selected video file
        private void PlayVideo(string filePath)
        {
            var media = new Media(_libVLC, filePath, FromType.FromPath);
            _mediaPlayer.Media = media;

            // Start playing the video
            _mediaPlayer.Play();
        }

        private void VideoListView_MouseEnter_1(object sender, System.Windows.Input.MouseEventArgs e)
        {

        }
        private CancellationTokenSource _previewCts;

        private void VideoListView_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var item = GetListViewItemUnderMouse(VideoListView, e.GetPosition(VideoListView));
            if (item == null) return;

            var videoFile = item.DataContext as VideoFile;
            if (videoFile == null) return;

            StartVideoPreview(videoFile.FilePath);
        }

        private void VideoListView_MouseLeave_1(object sender, System.Windows.Input.MouseEventArgs e)
        {
            StopVideoPreview();
        }

        private async void StartVideoPreview(string filePath)
        {
            StopVideoPreview(); // cancel existing preview if any

            _previewCts = new CancellationTokenSource();
            try
            {
                var media = new Media(_libVLC, filePath, FromType.FromPath);
                _mediaPlayer.Media = media;
                _mediaPlayer.Play();

                await Task.Delay(10000, _previewCts.Token); // 10 seconds preview
                _mediaPlayer.Pause(); // Or .Stop() depending on what you want
            }
            catch (TaskCanceledException)
            {
                // preview was cancelled early
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
        }

        // Helper to get item under mouse
        private ListViewItem GetListViewItemUnderMouse(ListView listView, Point position)
        {
            var hit = VisualTreeHelper.HitTest(listView, position);
            DependencyObject obj = hit?.VisualHit;
            while (obj != null && !(obj is ListViewItem))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }
            return obj as ListViewItem;
        }
    }

    // VideoFile class to store file name and file path
    public class VideoFile
    {
        public required string FileName { get; set; }
        public required string FilePath { get; set; }
    }
}
