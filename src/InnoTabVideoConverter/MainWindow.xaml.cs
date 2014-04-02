namespace InnoTabVideoConverter
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Navigation;

    using MahApps.Metro.Controls;
    using MahApps.Metro.Controls.Dialogs;

    using Microsoft.Win32;

    using YoutubeExtractor;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private const string ffmpegParameters = "-y -i \"{0}\" -vcodec libx264 -profile:v baseline -b:v 600k -r 24 -s 480x272 -aspect 16:9 -acodec libmp3lame -af volume=1 -b:a 96k -ar 22050 \"{1}\"";

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

            var defaultFilename = this.CleanFilename(video.Title);

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

        private string CleanFilename(string title)
        {
            var invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            var cleaned = title;

            foreach (var c in invalid)
            {
                cleaned = cleaned.Replace(c.ToString(), "");
            }

            return cleaned;
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

            if (!File.Exists(ConfigurationManager.AppSettings["ffmpeg-path"]))
            {
                await this.ShowMessageAsync(
                    "FFMpeg",
                    "Unable to load ffmpeg, please make sure you have placed it in the correct location.\n\nSee ffmpeg.txt in the ffmpeg folder for more information.");

                return;
            }

            this.BusyGrid.Visibility = Visibility.Visible;

            Task.Run(
                async () =>
                {
                    var exitCode = -1;
                    var logFile = string.Empty;
                    try
                    {
                        var destinationFilename = Path.Combine(
                            Path.GetDirectoryName(filename),
                            Path.GetFileNameWithoutExtension(filename) + "-converted" + ".avi");

                        var arguments = string.Format(ffmpegParameters, filename, destinationFilename);

                        var outputBuilder = new StringBuilder();
                        var errorBuilder = new StringBuilder();

                        var ffmpeg = new Process
                                         {
                                             StartInfo =
                                                 {
                                                     FileName =
                                                         ConfigurationManager.AppSettings["ffmpeg-path"],
                                                     Arguments = arguments,
                                                     UseShellExecute = false,
                                                     RedirectStandardOutput = true,
                                                     RedirectStandardError = true,
                                                     CreateNoWindow = true,
                                                 }
                                         };

                        using (ffmpeg)
                        {
                            ffmpeg.OutputDataReceived += (sender, e) => outputBuilder.AppendLine(e.Data);
                            ffmpeg.ErrorDataReceived += (sender, e) => errorBuilder.AppendLine(e.Data);

                            ffmpeg.Start();

                            ffmpeg.BeginOutputReadLine();
                            ffmpeg.BeginErrorReadLine();

                            ffmpeg.WaitForExit();

                            exitCode = ffmpeg.ExitCode;
                        }

                        logFile = Path.ChangeExtension(filename, "log");
                        using (var log = File.CreateText(logFile))
                        {
                            log.WriteLine(
                                "Commandline: {0} {1}",
                                ConfigurationManager.AppSettings["ffmpeg-path"],
                                arguments);
                            log.WriteLine("** Output:");
                            log.Write(errorBuilder.ToString()); // ffmpeg only uses stderr - feck knows why
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        this.Dispatcher.Invoke(() => this.BusyGrid.Visibility = Visibility.Hidden);
                    }

                    if (exitCode == 0)
                    {
                        this.Dispatcher.InvokeAsync(async () => await this.ShowMessageAsync("Conversion", "Conversion completely sucessfully."));
                    }
                    else
                    {
                        this.Dispatcher.InvokeAsync(
                            async () =>
                            {
                                await
                                    this.ShowMessageAsync(
                                        "Conversion",
                                        "Conversion failed - see log for more information.");

                                Process.Start(logFile);
                            });
                    }
                });
        }

        private void OpenUrl(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }
    }
}
