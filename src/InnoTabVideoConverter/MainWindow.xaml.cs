namespace InnoTabVideoConverter
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;

    using MahApps.Metro.Controls;
    using MahApps.Metro.Controls.Dialogs;

    using Microsoft.Win32;

    using YoutubeExtractor;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void DownloadButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.InvokeAsync(async () => await this.Download(this.UrlBox.Text));
        }

        private async Task Download(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                await this.ShowMessageAsync(
                        "Invalid Url",
                        "Please enter the full URL to the YouTube page\n\ne.g. https://www.youtube.com/watch?v=3PADxcM_Vi8");

                return;
            }

            var videoInfos = DownloadUrlResolver.GetDownloadUrls(url);

            var video = SelectBestMatch(videoInfos);

            if (video == null)
            {
                await this.ShowMessageAsync(
                        "Url Error",
                        "Unable to download a suitable format for conversion.");

                return;
            }

            var dialog = new SaveFileDialog
                             {
                                 FileName = video.Title,
                                 DefaultExt = video.VideoExtension,
                                 Filter = string.Format("Video Files ({0})|*{0}", video.VideoExtension),
                                 OverwritePrompt = true,
                                 CheckPathExists = true
                             };

            var result = dialog.ShowDialog(this);

            if (result == true)
            {
                var controller = await this.ShowProgressAsync("Please wait...", "Downloading");

                var videoDownloader = new VideoDownloader(video, dialog.FileName);

                videoDownloader.DownloadProgressChanged += (s, e) => this.Dispatcher.Invoke(() => controller.SetProgress(e.ProgressPercentage / 100));

                await Task.Run(() => videoDownloader.Execute());

                await controller.CloseAsync();

                this.InputVideoFilename.Text = dialog.FileName;

                this.ConverterTab.IsSelected = true;
            }
        }

        private static VideoInfo SelectBestMatch(IEnumerable<VideoInfo> videoInfos)
        {
            var infos = videoInfos.Where(vi => vi.VideoType == VideoType.WebM && vi.Resolution <= 480).OrderByDescending(vi => vi.Resolution).ToArray();

            return infos.FirstOrDefault();
        }

        private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.InvokeAsync(async () => await this.Browse());
        }

        private async Task Browse()
        {
            var dialog = new OpenFileDialog
                             {
                                 AddExtension = true,
                                 CheckFileExists = true,
                                 Filter = "Video Files(*.AVI;*.MP4;*.MPG;*.MPEG;*.WEBM;*.MKV)|*.AVI;*.MP4;*.MPG;*.MPEG;*.WEBM;*.MKV|All files (*.*)|*.*"
                             };

            var result = dialog.ShowDialog(this);

            if (result == true)
            {
                this.InputVideoFilename.Text = dialog.FileName;
            }
        }

        private void ConvertButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.InvokeAsync(async () => await this.Convert(this.InputVideoFilename.Text));
        }

        private async Task Convert(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || !File.Exists(filename))
            {
                await this.ShowMessageAsync(
                            "Filename",
                            "Please select a valid file..");

                return;
            }

            this.BusyGrid.Visibility = Visibility.Visible;

            await Task.Delay(5000);

            this.BusyGrid.Visibility = Visibility.Hidden;
        }
    }
}
