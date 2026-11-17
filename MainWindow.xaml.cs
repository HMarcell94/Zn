using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZnDownloader
{
    public partial class MainWindow : Window
    {
        // YouTube / Pinterest fields
        private string _ytSavePath = "";
        private string _pinSavePath = "";

        // Converter fields
        private string _converterInputFile = "";
        private string _converterSavePath = "";
        private string _converterSelectedFormat = "";
        private readonly List<Button> _formatButtons = new List<Button>();

        // Upscaler fields
        private string _upscalerInputFile = "";
        private string _upscalerSavePath = "";

        // Download history (newest first), persisted by DownloadHistoryStore
        private readonly ObservableCollection<DownloadHistoryEntry> _history = new ObservableCollection<DownloadHistoryEntry>();

        public MainWindow()
        {
            InitializeComponent();

            // Default save location = the user's real Downloads folder for every tool
            // (it can be moved, e.g. to D:\Downloads, so don't assume %USERPROFILE%\Downloads).
            string defaultDownloads = GetDownloadsFolder();

            foreach (var entry in DownloadHistoryStore.Load().OrderByDescending(h => h.Time))
                _history.Add(entry);
            HistoryList.ItemsSource = _history;
            UpdateHistoryEmptyState();

            _ytSavePath = defaultDownloads;
            _pinSavePath = defaultDownloads;
            _converterSavePath = defaultDownloads;
            _upscalerSavePath = defaultDownloads;

            YtPathTextBox.Text = _ytSavePath;
            PinPathTextBox.Text = _pinSavePath;
            OutputPathTextBox.Text = _converterSavePath;
            UpscalerOutputPathTextBox.Text = _upscalerSavePath;

            FindFormatButtons(FormatButtonsWrapPanel);
            ApplyFormatCompatibility(updateText: true);   // no input yet: every target stays inactive

            // Initialize dynamic panels
            YtMode_Checked(null!, null!);
            UpscalerMethod_Checked(null!, null!);
        }

        #region Window Controls

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        // Fixes the borderless-window overflow when maximized, and swaps the maximize glyph.
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                RootLayout.Margin = new Thickness(7);
                MaximizeButton.Content = "\uE923"; // ChromeRestore glyph
                MaximizeButton.ToolTip = "Előző méret";
            }
            else
            {
                RootLayout.Margin = new Thickness(0);
                MaximizeButton.Content = "\uE922"; // ChromeMaximize glyph
                MaximizeButton.ToolTip = "Teljes méret";
            }

            // No point animating the background while nobody can see it.
            if (_backdropDrift != null)
            {
                if (WindowState == WindowState.Minimized) _backdropDrift.Pause(this);
                else _backdropDrift.Resume(this);
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // Windows 11: ask DWM for native rounded corners on this borderless window (ignored on Windows 10).
        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2;
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        private Storyboard? _backdropDrift;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyResponsiveLayout();
            UpdateToolsStatus();

            // Slow drift of the background light orbs; skipped when Windows animations are turned off.
            if (SystemParameters.ClientAreaAnimation)
            {
                _backdropDrift = (Storyboard)FindResource("BackdropDrift");
                _backdropDrift.Begin(this, isControllable: true);
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

        // Breakpoints: icon-only sidebar below 1040 px, tighter page gutters below 900 px,
        // shorter log on low windows. The cards reflow on their own (AdaptiveColumnsPanel).
        private void ApplyResponsiveLayout()
        {
            if (Sidebar == null || ConsoleCard == null) return;

            bool compactNav = ActualWidth < 1040;
            Sidebar.Width = compactNav ? 76 : 236;
            Ui.SetIsCompact(Sidebar, compactNav);

            double gutter = ActualWidth < 900 ? 24 : 40;
            Resources["PageMargin"] = new Thickness(gutter, 0, gutter, 28);
            ConsoleCard.Margin = new Thickness(gutter, 0, gutter, 20);

            StatusLog.MaxHeight = ActualHeight < 720 ? 64 : 118;
        }

        // Sidebar status card: are the external tools next to the app?
        private void UpdateToolsStatus()
        {
            string[] tools = { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" };
            var missing = tools.Where(t => !File.Exists(ToolPath(t))).ToList();

            Brush color = missing.Count == 0 ? (Brush)FindResource("AccentCyan") : (Brush)FindResource("AccentPink");
            ToolsStatusDot.Fill = color;
            ToolsStatusGlow.Fill = color;
            ToolsStatusText.Text = missing.Count == 0 ? "Minden eszköz a helyén" : "Hiányzik: " + string.Join(", ", missing);
            ToolsStatusCard.ToolTip = missing.Count == 0
                ? "yt-dlp, FFmpeg, ffprobe és deno megtalálva az app mellett."
                : "Hiányzó eszközök: " + string.Join(", ", missing) + "\nFuttasd a fetch-tools.ps1 szkriptet a projekt mappájából.";
        }

        // Sidebar navigation drives the (header-less) TabControl.
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null && sender is RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int index))
                MainTabControl.SelectedIndex = index;
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ComboBox selection changes bubble up here too; only react to the TabControl itself.
            if (!ReferenceEquals(e.OriginalSource, MainTabControl)) return;

            // Keep the sidebar in sync (e.g. Ctrl+Tab).
            foreach (var rb in NavPanel.Children.OfType<RadioButton>())
                if (rb.Tag?.ToString() == MainTabControl.SelectedIndex.ToString())
                    rb.IsChecked = true;

            // Soft fade + rise of the incoming page.
            if (SystemParameters.ClientAreaAnimation &&
                MainTabControl.Template?.FindName("PART_SelectedContentHost", MainTabControl) is FrameworkElement host)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var shift = new TranslateTransform(0, 14);
                host.RenderTransform = shift;
                host.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            }
        }

        private void ConsoleToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool collapse = StatusLog.Visibility == Visibility.Visible;
            StatusLog.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
            ConsoleToggleButton.Content = collapse ? "" : ""; // ChevronUp / ChevronDown
            ConsoleToggleButton.ToolTip = collapse ? "Napló kinyitása" : "Napló összecsukása";
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            StatusLog.Text = "Üdv! Válassz egy eszközt bal oldalt a kezdéshez.";
        }

        private void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Copy the selection if there is one, otherwise the whole log.
                Clipboard.SetText(StatusLog.SelectionLength > 0 ? StatusLog.SelectedText : StatusLog.Text);
            }
            catch { /* clipboard can be briefly locked by another app; ignore */ }
        }

        #endregion

        // Collects the format chips (grouped into Hang / Videó / Kép rows) and wires them up.
        private void FindFormatButtons(Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Button btn)
                {
                    _formatButtons.Add(btn);
                    btn.Click += FormatButton_Click;
                }
                else if (child is Panel nested)
                {
                    FindFormatButtons(nested);
                }
            }
        }

        #region YouTube

        private void YtMode_Checked(object sender, RoutedEventArgs e)
        {
            // Guard: fires during XAML init before all controls exist
            if (YtAudioPanel == null || YtVideoPanel == null || YtDownloadButton == null || YtAudioModeRadio == null)
                return;

            bool audio = YtAudioModeRadio.IsChecked == true;
            YtAudioPanel.Visibility = audio ? Visibility.Visible : Visibility.Collapsed;
            YtVideoPanel.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
            YtDownloadButton.Content = audio ? "Zene letöltése" : "Videó letöltése";
        }

        private void YtPasteButton_Click(object sender, RoutedEventArgs e) => PasteInto(YtUrlTextBox);

        private void YtBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            _ytSavePath = SelectFolder(_ytSavePath);
            YtPathTextBox.Text = _ytSavePath;
        }

        private void YtOpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenFolder(_ytSavePath);

        private async void YtDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetWebLink(YtUrlTextBox.Text, out string url, out string linkError))
            {
                AppendStatus(linkError == "" ? "Illessz be egy YouTube linket először." : linkError, isError: true);
                return;
            }

            bool audio = YtAudioModeRadio.IsChecked == true;
            string saveFolder = _ytSavePath;
            var modeArgs = new List<string>();
            string formatLabel;

            if (audio)
            {
                string fmt = (YtAudioFormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mp3";
                // Prefer a stereo source: YouTube's "best" audio is often a 6-channel 5.1 track, which would
                // turn a long video into a huge 6-channel WAV (a 4.5 h one is ~9.5 GB, past WAV's 4 GB limit).
                // For m4a, take YouTube's own AAC track first so it needs no re-encoding.
                string audioSelector = fmt == "m4a"
                    ? "ba[ext=m4a][audio_channels<=2]/ba[audio_channels<=2]/ba/b"
                    : "ba[audio_channels<=2]/ba/b";
                modeArgs.AddRange(new[] { "-f", audioSelector, "-x", "--audio-format", fmt, "--audio-quality", "0" });
                // RF64 kicks in only if a WAV would still exceed 4 GB (very long videos); otherwise a normal WAV.
                if (fmt == "wav") modeArgs.AddRange(new[] { "--postprocessor-args", "ExtractAudio:-rf64 auto" });
                formatLabel = fmt;
            }
            else
            {
                string quality = (YtVideoQualityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Legjobb elérhető";
                var heightMatch = Regex.Match(quality, @"(\d+)");
                string fmtSelector = heightMatch.Success
                    ? $"bv*[height<={heightMatch.Groups[1].Value}]+ba/b[height<={heightMatch.Groups[1].Value}]"
                    : "bv*+ba/b";
                modeArgs.AddRange(new[] { "-f", fmtSelector, "--merge-output-format", "mp4" });
                formatLabel = $"mp4 ({quality})";
            }

            SetUIEnabled(false);
            StartProgress(indeterminate: true);
            StatusLog.Text = (audio ? "Zene letöltése indul…" : "Videó letöltése indul…") + Environment.NewLine;

            await RunYtDlpDownloadAsync("YouTube", url, formatLabel, saveFolder, modeArgs);
            SetUIEnabled(true);
        }

        // Accepts only a single absolute http(s) link. Anything with spaces or quotes is refused, so pasted
        // text can never be read as extra yt-dlp options (e.g. --exec, which would run a command).
        private static bool TryGetWebLink(string text, out string url, out string error)
        {
            url = text.Trim();
            error = "";
            if (url == "") return false;
            bool valid = !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '"')
                         && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                         && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                         && uri.Host != "";
            if (!valid) error = "Ez nem egy érvényes webes link (https://…). Csak magát a linket illeszd be.";
            return valid;
        }

        #endregion

        #region Downloads & history

        // Runs one yt-dlp download, says where the file(s) ended up, and records it on the history tab.
        private async Task RunYtDlpDownloadAsync(string source, string url, string format, string saveFolder, IEnumerable<string> modeArgs)
        {
            // yt-dlp appends {title, filepath} of every finished file here. "after_move" fires after
            // post-processing, so the path is the final .wav / .mp4, not the intermediate download.
            string printFile = Path.Combine(Path.GetTempPath(), $"zn_ytdlp_{Guid.NewGuid():N}.jsonl");

            var args = new List<string> { "--print-to-file", "after_move:%(.{title,filepath})j", printFile };
            args.AddRange(YtDlpRuntimeArgs());
            args.AddRange(modeArgs);
            args.AddRange(new[] { "-o", "%(title)s.%(ext)s", "-P", saveFolder, "--ffmpeg-location", AppDir });
            args.Add("--");   // everything after this is a link, never an option
            args.Add(url);

            await EnsureYtDlpUpdatedAsync();
            _lastProcessError = "";
            int exit = await RunProcessAsync(ToolPath("yt-dlp.exe"), args);

            if (_lastProcessError.Contains("not a bot"))
                AppendStatus("A YouTube most bot-ellenőrzést kér erről a gépről (sok letöltés után előfordul). Próbáld újra később.", isError: true);

            var saved = ReadPrintedFiles(printFile);
            try { File.Delete(printFile); } catch { }

            if (exit == 0)
            {
                AppendStatus("Kész! A letöltés befejeződött.", isError: false);
                foreach (var file in saved)
                    AppendStatus("Mentve ide: " + file.Path, isError: false);
            }
            else
            {
                AppendStatus($"A letöltés hibával zárult (kód: {exit}).", isError: true);
            }

            DateTime now = DateTime.Now;
            var entries = saved.Select(file => new DownloadHistoryEntry
            {
                Time = now, Source = source, Url = url, Format = format, Success = true,
                Title = file.Title, FilePath = file.Path, Folder = saveFolder
            }).ToList();
            if (exit != 0 || entries.Count == 0)
            {
                entries.Add(new DownloadHistoryEntry
                {
                    Time = now, Source = source, Url = url, Format = format, Success = exit == 0, Folder = saveFolder,
                    Error = exit == 0 ? "" : (_lastProcessError != "" ? _lastProcessError : $"Kilépési kód: {exit}")
                });
            }

            foreach (var entry in entries)
                _history.Insert(0, entry);
            UpdateHistoryEmptyState();
            DownloadHistoryStore.Save(_history);
        }

        private static List<(string Title, string Path)> ReadPrintedFiles(string file)
        {
            var result = new List<(string Title, string Path)>();
            try
            {
                if (!File.Exists(file)) return result;
                foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        string path = root.TryGetProperty("filepath", out var p) ? p.GetString() ?? "" : "";
                        if (path != "") result.Add((title, path));
                    }
                    catch (JsonException) { /* skip a malformed line */ }
                }
            }
            catch { /* unreadable: the download itself still counts, just without file details */ }
            return result;
        }

        private void UpdateHistoryEmptyState() =>
            HistoryEmptyText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private static DownloadHistoryEntry? HistoryEntryOf(object sender) =>
            (sender as FrameworkElement)?.DataContext as DownloadHistoryEntry;

        private void HistoryShowInFolder_Click(object sender, RoutedEventArgs e)
        {
            var entry = HistoryEntryOf(sender);
            if (entry == null) return;

            if (entry.FilePath != "" && File.Exists(entry.FilePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.FilePath}\"") { UseShellExecute = true });
                return;
            }
            if (entry.FilePath != "")
                AppendStatus("A fájl már nem található: " + entry.FilePath, isError: true);
            OpenFolder(entry.Folder);
        }

        private void HistoryCopyLink_Click(object sender, RoutedEventArgs e)
        {
            var entry = HistoryEntryOf(sender);
            if (entry == null) return;
            try { Clipboard.SetText(entry.Url); } catch { /* clipboard briefly locked; ignore */ }
        }

        private void HistoryRemove_Click(object sender, RoutedEventArgs e)
        {
            var entry = HistoryEntryOf(sender);
            if (entry == null) return;
            _history.Remove(entry);
            UpdateHistoryEmptyState();
            DownloadHistoryStore.Save(_history);
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_history.Count == 0) return;
            var answer = MessageBox.Show(this,
                "Biztosan törlöd az összes letöltési előzményt?\nA letöltött fájlok megmaradnak, csak a lista ürül ki.",
                "Előzmények törlése", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            _history.Clear();
            UpdateHistoryEmptyState();
            DownloadHistoryStore.Save(_history);
        }

        #endregion

        #region Pinterest

        private void PinPasteButton_Click(object sender, RoutedEventArgs e) => PasteInto(PinUrlTextBox);

        private void PinBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            _pinSavePath = SelectFolder(_pinSavePath);
            PinPathTextBox.Text = _pinSavePath;
        }

        private void PinOpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenFolder(_pinSavePath);

        private async void PinDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetWebLink(PinUrlTextBox.Text, out string url, out string linkError))
            {
                AppendStatus(linkError == "" ? "Illessz be egy Pinterest linket először." : linkError, isError: true);
                return;
            }

            string selectedFormat = (PinFormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mp4";
            string saveFolder = _pinSavePath;
            var modeArgs = selectedFormat.Contains("mp4") ? new List<string> { "-f", "mp4" } : new List<string>();

            SetUIEnabled(false);
            StartProgress(indeterminate: true);
            StatusLog.Text = "Pinterest videó letöltése indul…" + Environment.NewLine;

            await RunYtDlpDownloadAsync("Pinterest", url, selectedFormat, saveFolder, modeArgs);
            SetUIEnabled(true);
        }

        #endregion

        #region File Converter

        private async void InputFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Válassz bemeneti fájlt", CheckFileExists = true, CheckPathExists = true };
            if (dialog.ShowDialog() == true)
                await SetConverterInputAsync(dialog.FileName);
        }

        // ---- What the input is, and which targets make sense for it ----

        private enum MediaKind { None, Unknown, Audio, Video, Image, AnimatedImage }

        private MediaKind _converterInputKind = MediaKind.None;
        private bool _converterInputHasAudio;

        private static readonly string[] AudioTargets = { ".mp3", ".wav", ".aac", ".flac", ".ogg" };
        private static readonly string[] VideoTargets = { ".mp4", ".mkv", ".mov", ".avi", ".webm" };
        private static readonly string[] ImageTargets = { ".png", ".jpg", ".gif", ".bmp", ".ico" };

        private async Task SetConverterInputAsync(string file)
        {
            _converterInputFile = file;
            InputFileTextBox.Text = file;
            _converterInputKind = MediaKind.None;
            ConverterCompatText.Text = "Fájl vizsgálata…";
            ApplyFormatCompatibility(updateText: false);

            var (kind, hasAudio) = await ProbeMediaAsync(file);
            if (file != _converterInputFile) return;   // another file was picked meanwhile
            _converterInputKind = kind;
            _converterInputHasAudio = hasAudio;
            ApplyFormatCompatibility(updateText: true);
        }

        // Asks ffprobe what is inside the file (not just its extension).
        private static async Task<(MediaKind kind, bool hasAudio)> ProbeMediaAsync(string file)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(ToolPath("ffprobe.exe"))
                    {
                        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    foreach (string a in new[] { "-v", "error", "-show_entries", "stream=codec_type:stream_disposition=attached_pic:format=format_name", "-of", "json", file })
                        psi.ArgumentList.Add(a);
                    using var p = Process.Start(psi)!;
                    string json = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) return (MediaKind.Unknown, false);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    string format = root.TryGetProperty("format", out var f) && f.TryGetProperty("format_name", out var fn) ? fn.GetString() ?? "" : "";
                    bool hasAudio = false, hasVideo = false;
                    if (root.TryGetProperty("streams", out var streams))
                        foreach (var s in streams.EnumerateArray())
                        {
                            string type = s.TryGetProperty("codec_type", out var t) ? t.GetString() ?? "" : "";
                            // Album art inside an mp3/m4a is a "video" stream too; it doesn't make the file a video.
                            bool coverArt = s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;
                            if (type == "audio") hasAudio = true;
                            if (type == "video" && !coverArt) hasVideo = true;
                        }

                    bool imageContainer = format == "image2" || format == "ico" || format.EndsWith("_pipe");
                    if (format == "gif") return (MediaKind.AnimatedImage, false);
                    if (hasVideo && imageContainer && !hasAudio) return (MediaKind.Image, false);
                    if (hasVideo) return (MediaKind.Video, hasAudio);
                    if (hasAudio) return (MediaKind.Audio, true);
                    return (MediaKind.Unknown, false);
                }
                catch { return (MediaKind.Unknown, false); }
            });
        }

        // Audio -> audio; video -> video, its soundtrack as audio, or an animated GIF; image -> image;
        // animated GIF -> image or video. Nothing that would only produce a broken or meaningless file.
        private string[] AllowedTargets() => _converterInputKind switch
        {
            MediaKind.Audio => AudioTargets,
            MediaKind.Video => VideoTargets.Concat(_converterInputHasAudio ? AudioTargets : Array.Empty<string>()).Append(".gif").ToArray(),
            MediaKind.Image => ImageTargets,
            MediaKind.AnimatedImage => ImageTargets.Concat(VideoTargets).ToArray(),
            _ => Array.Empty<string>()
        };

        private void ApplyFormatCompatibility(bool updateText)
        {
            var allowed = AllowedTargets();
            foreach (var chip in _formatButtons)
                chip.IsEnabled = allowed.Contains(chip.Content?.ToString());

            if (_converterSelectedFormat != "" && !allowed.Contains(_converterSelectedFormat))
            {
                _converterSelectedFormat = "";
                foreach (var chip in _formatButtons)
                {
                    chip.ClearValue(BackgroundProperty);
                    chip.ClearValue(Button.BorderBrushProperty);
                    chip.ClearValue(ForegroundProperty);
                }
            }

            if (!updateText) return;
            ConverterCompatText.Text = _converterInputKind switch
            {
                MediaKind.Audio => "Hangfájl: hangformátumokba alakítható.",
                MediaKind.Video => _converterInputHasAudio
                    ? "Videó: más videóformátumba, csak a hangsávja hangformátumba, vagy animált GIF-be alakítható."
                    : "Videó (hangsáv nélkül): más videóformátumba vagy animált GIF-be alakítható.",
                MediaKind.Image => "Kép: más képformátumba alakítható.",
                MediaKind.AnimatedImage => "GIF: más képformátumba (első képkocka) vagy videóba alakítható.",
                MediaKind.Unknown => "Ezt a fájlt az FFmpeg nem ismeri fel hang-, videó- vagy képfájlként.",
                _ => "Előbb válassz bemeneti fájlt: utána csak a hozzá illő formátumok lesznek aktívak."
            };
        }

        private void OutputPathButton_Click(object sender, RoutedEventArgs e)
        {
            _converterSavePath = SelectFolder(_converterSavePath);
            OutputPathTextBox.Text = _converterSavePath;
        }

        private void OutputOpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenFolder(_converterSavePath);

        private void FormatButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton) return;

            _converterSelectedFormat = clickedButton.Content?.ToString() ?? "";

            foreach (var btn in _formatButtons)
            {
                btn.ClearValue(BackgroundProperty);
                btn.ClearValue(Button.BorderBrushProperty);
                btn.ClearValue(ForegroundProperty);
            }

            clickedButton.Background = (Brush)FindResource("AccentGradient");
            clickedButton.BorderBrush = Brushes.Transparent;
            clickedButton.Foreground = Brushes.White;
        }

        private async void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_converterInputFile) || string.IsNullOrWhiteSpace(_converterSavePath) || string.IsNullOrWhiteSpace(_converterSelectedFormat))
            {
                AppendStatus("Válassz bemeneti fájlt, cél formátumot és mentési mappát.", isError: true);
                return;
            }
            if (!AllowedTargets().Contains(_converterSelectedFormat))
            {
                AppendStatus($"Ez a fájl nem alakítható {_converterSelectedFormat} formátumba.", isError: true);
                return;
            }

            string inputFileName = Path.GetFileNameWithoutExtension(_converterInputFile) ?? "converted_file";
            string ext = _converterSelectedFormat.ToLowerInvariant();
            // Never overwrite: an existing "name.mp3" (or the source itself) makes this "name (1).mp3".
            string outputFile = GetUniqueOutputPath(_converterSavePath, inputFileName, ext);

            var args = new List<string> { "-hide_banner", "-i", _converterInputFile };
            if (ext == ".ico")   // icons are at most 256x256; quoted so the commas aren't read as filter separators
                args.AddRange(new[] { "-vf", "scale='min(256,iw)':'min(256,ih)':force_original_aspect_ratio=decrease" });
            if (ext is ".png" or ".jpg" or ".bmp" or ".ico")   // still image targets: an animated GIF gives its first frame
                args.AddRange(new[] { "-frames:v", "1" });
            if (AudioTargets.Contains(ext))   // audio only: no video track (e.g. .ogg would otherwise also carry the picture)
                args.Add("-vn");
            if (VideoTargets.Contains(ext))   // even size + 4:2:0 colour, so every player can open it (a GIF source would give 4:4:4)
                args.AddRange(new[] { "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-pix_fmt", "yuv420p" });
            args.AddRange(new[] { "-n", outputFile });

            SetUIEnabled(false);
            StartProgress(indeterminate: true);
            StatusLog.Text = $"Konvertálás indul ({_converterSelectedFormat})…" + Environment.NewLine;

            int exit = await RunProcessAsync(ToolPath("ffmpeg.exe"), args);

            AppendStatus(exit == 0 ? "Kész! A konvertálás befejeződött." : $"A konvertálás hibával zárult (kód: {exit}).", isError: exit != 0);
            if (exit == 0) AppendStatus("Mentve ide: " + outputFile, isError: false);
            SetUIEnabled(true);
        }

        private async void ConverterInput_Drop(object sender, DragEventArgs e)
        {
            string? file = GetDroppedFile(e);
            if (file != null)
                await SetConverterInputAsync(file);
        }

        #endregion

        #region Video Upscaler

        private void UpscalerInputFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Válassz bemeneti videót",
                CheckFileExists = true,
                CheckPathExists = true,
                Filter = "Videó fájlok|*.mp4;*.mkv;*.avi;*.mov;*.webm|Minden fájl|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                _upscalerInputFile = dialog.FileName;
                UpscalerInputFileTextBox.Text = _upscalerInputFile;
            }
        }

        private void UpscalerOutputPathButton_Click(object sender, RoutedEventArgs e)
        {
            _upscalerSavePath = SelectFolder(_upscalerSavePath);
            UpscalerOutputPathTextBox.Text = _upscalerSavePath;
        }

        private void UpscalerOpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenFolder(_upscalerSavePath);

        private void UpscalerInput_Drop(object sender, DragEventArgs e)
        {
            string? file = GetDroppedFile(e);
            if (file != null)
            {
                _upscalerInputFile = file;
                UpscalerInputFileTextBox.Text = file;
            }
        }

        private void UpscalerMethod_Checked(object sender, RoutedEventArgs e)
        {
            if (LocalUpscalerOptionsPanel == null || CloudUpscalerOptionsPanel == null || LocalUpscalerRadioButton == null)
                return;

            bool local = LocalUpscalerRadioButton.IsChecked == true;
            LocalUpscalerOptionsPanel.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
            CloudUpscalerOptionsPanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void UpscaleButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_upscalerInputFile))
            {
                AppendStatus("Válassz bemeneti videót először.", isError: true);
                return;
            }
            if (string.IsNullOrWhiteSpace(_upscalerSavePath))
            {
                AppendStatus("Válassz mentési mappát először.", isError: true);
                return;
            }

            string inputFileName = Path.GetFileNameWithoutExtension(_upscalerInputFile) ?? "upscaled_video";
            string inputExtension = Path.GetExtension(_upscalerInputFile).ToLowerInvariant();
            // The result is H.264: keep containers that hold it (mp4/mkv/mov), use .mp4 for the rest (webm, avi, …).
            string outputExtension = inputExtension is ".mp4" or ".mkv" or ".mov" ? inputExtension : ".mp4";

            SetUIEnabled(false);
            StartProgress(indeterminate: true);

            if (LocalUpscalerRadioButton.IsChecked == true)
            {
                string scale = (LocalScaleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "2x";
                string algorithm = (LocalAlgorithmComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Lanczos";
                string scaleFactor = scale.Replace("x", "");

                // Even dimensions + yuv420p: H.264 that every player can open (xBR alone would give 4:4:4).
                string ffmpegFilter = algorithm switch
                {
                    var a when a.Contains("Lanczos") => $"scale=trunc(iw*{scaleFactor}/2)*2:trunc(ih*{scaleFactor}/2)*2:flags=lanczos",
                    var a when a.Contains("xBR") => $"xbr={scaleFactor},scale=trunc(iw/2)*2:trunc(ih/2)*2",
                    _ => $"scale=trunc(iw*{scaleFactor}/2)*2:trunc(ih*{scaleFactor}/2)*2:flags=bicubic"
                } + ",format=yuv420p";

                string outputFile = GetUniqueOutputPath(_upscalerSavePath, $"{inputFileName}_{scale}_{algorithm.Split(' ')[0].ToLower()}", outputExtension);
                StatusLog.Text = $"Helyi felskálázás indul ({scale} {algorithm.Split(' ')[0]})…" + Environment.NewLine;

                // Same container: copy the audio as is. New container: re-encode to AAC so it is guaranteed to fit.
                var audioArgs = outputExtension == inputExtension ? new[] { "-c:a", "copy" } : new[] { "-c:a", "aac", "-b:a", "192k" };
                var args = new List<string> { "-hide_banner", "-i", _upscalerInputFile, "-vf", ffmpegFilter, "-c:v", "libx264", "-preset", "medium", "-crf", "20" };
                args.AddRange(audioArgs);
                args.AddRange(new[] { "-n", outputFile });
                int exit = await RunProcessAsync(ToolPath("ffmpeg.exe"), args);

                AppendStatus(exit == 0
                    ? "Kész! Helyi felskálázás befejeződött: " + outputFile
                    : $"A felskálázás hibával zárult (kód: {exit}).", isError: exit != 0);
            }
            else
            {
                await RunCloudUpscaleAsync(inputFileName, outputExtension);
            }

            SetUIEnabled(true);
        }

        private async Task RunCloudUpscaleAsync(string inputFileName, string outputExtension)
        {
            string apiKey = CloudApiKeyTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AppendStatus("Add meg a Fal.ai API kulcsod először.", isError: true);
                return;
            }

            string selectedModelItem = (CloudModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "clarityai/crystal-video-upscaler";
            string modelId = selectedModelItem.Split(' ')[0];

            StatusLog.Text = "Felhő AI felskálázás indul…" + Environment.NewLine;

            try
            {
                // tmpfiles.org accepts at most 100 MB; fail early instead of after a long upload.
                const long TmpFilesLimit = 100L * 1024 * 1024;
                if (new FileInfo(_upscalerInputFile).Length > TmpFilesLimit)
                {
                    AppendStatus("A felhős felskálázáshoz a videó legfeljebb 100 MB lehet (a tmpfiles.org korlátja). Használd a helyi módot.", isError: true);
                    return;
                }

                AppendStatus("Videó feltöltése a felhőbe (tmpfiles.org)… Figyelem: a feltöltött videó kb. 60 percig bárki számára elérhető, aki ismeri a linkjét.", isError: false);
                string directDownloadUrl = await UploadToTmpFilesAsync(_upscalerInputFile);
                AppendStatus("Feltöltés kész. Videó URL: " + directDownloadUrl, isError: false);

                AppendStatus($"Feladat beküldése a Fal.ai-hoz ({modelId})…", isError: false);
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Key {apiKey}");

                var requestBody = new { video_url = directDownloadUrl };
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"https://queue.fal.run/{modelId}", content);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Fal.ai beküldés sikertelen: {response.StatusCode} - {responseBody}");

                using var doc = JsonDocument.Parse(responseBody);
                string requestId = doc.RootElement.GetProperty("request_id").GetString() ?? "";
                // Fal's queue URLs use only "owner/app" (the first two segments of the model id).
                string appPath = string.Join("/", modelId.Split('/').Take(2));
                string statusUrl = doc.RootElement.TryGetProperty("status_url", out var su) && su.GetString() is string s1
                    ? s1 : $"https://queue.fal.run/{appPath}/requests/{requestId}/status";
                // The status endpoint never contains the result; it has to be fetched from response_url.
                string resultUrl = doc.RootElement.TryGetProperty("response_url", out var ru) && ru.GetString() is string s2
                    ? s2 : $"https://queue.fal.run/{appPath}/requests/{requestId}";

                AppendStatus($"Feladat a sorban. Azonosító: {requestId}. Állapot figyelése…", isError: false);

                string upscaledVideoUrl = "";
                bool completed = false;
                int pollCount = 0;

                while (!completed && pollCount < 360) // Max ~30 minutes
                {
                    await Task.Delay(5000);
                    pollCount++;

                    var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                    statusRequest.Headers.Add("Authorization", $"Key {apiKey}");

                    var statusResponse = await client.SendAsync(statusRequest);
                    string statusBody = await statusResponse.Content.ReadAsStringAsync();
                    if (!statusResponse.IsSuccessStatusCode)
                    {
                        AppendStatus($"Figyelmeztetés: állapot lekérdezés sikertelen ({statusResponse.StatusCode}). Újrapróbálom…", isError: true);
                        continue;
                    }

                    using var statusDoc = JsonDocument.Parse(statusBody);
                    string status = statusDoc.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "IN_QUEUE" : "IN_QUEUE";

                    if (status == "COMPLETED")
                    {
                        // A failed job also ends as COMPLETED, with an "error" field.
                        if (statusDoc.RootElement.TryGetProperty("error", out var jobError) && jobError.ValueKind == JsonValueKind.String)
                            throw new Exception($"Fal.ai feladat sikertelen: {jobError.GetString()}");

                        var resultRequest = new HttpRequestMessage(HttpMethod.Get, resultUrl);
                        var resultResponse = await client.SendAsync(resultRequest);
                        string resultBody = await resultResponse.Content.ReadAsStringAsync();
                        if (!resultResponse.IsSuccessStatusCode)
                            throw new Exception($"Az eredmény lekérése sikertelen: {resultResponse.StatusCode} - {resultBody}");

                        using var resultDoc = JsonDocument.Parse(resultBody);
                        if (resultDoc.RootElement.TryGetProperty("video", out var videoObj) && videoObj.TryGetProperty("url", out var urlProp))
                            upscaledVideoUrl = urlProp.GetString() ?? "";
                        else if (resultDoc.RootElement.TryGetProperty("video_url", out var urlProp2))
                            upscaledVideoUrl = urlProp2.GetString() ?? "";
                        completed = true;
                    }
                    else if (status == "IN_PROGRESS")
                    {
                        AppendStatus("Videó feldolgozása a felhőben…", isError: false);
                    }
                    else if (status == "IN_QUEUE")
                    {
                        int pos = statusDoc.RootElement.TryGetProperty("queue_position", out var posProp) ? posProp.GetInt32() : 0;
                        AppendStatus($"Várakozás a sorban. Pozíció: {pos}", isError: false);
                    }
                    else if (status == "FAILED")
                    {
                        string errorMsg = statusDoc.RootElement.TryGetProperty("error", out var errorProp) ? errorProp.GetString() ?? "Ismeretlen hiba" : "Ismeretlen hiba";
                        throw new Exception($"Fal.ai feladat sikertelen: {errorMsg}");
                    }
                }

                if (!completed)
                    throw new Exception("A Fal.ai 30 perc alatt sem végzett a feladattal. Az azonosító: " + requestId);
                if (string.IsNullOrEmpty(upscaledVideoUrl))
                    throw new Exception("A feldolgozás befejeződött, de nem érkezett kimeneti videó URL.");

                AppendStatus("Felskálázott videó letöltése…", isError: false);
                string outputFile = GetUniqueOutputPath(_upscalerSavePath, $"{inputFileName}_upscaled_ai", ".mp4");
                // Separate client WITHOUT the Authorization header: the API key must not go to the file host.
                using (var plainClient = new HttpClient())
                using (var downloadStream = await plainClient.GetStreamAsync(upscaledVideoUrl))
                using (var fileStream = new FileStream(outputFile, FileMode.CreateNew))
                {
                    await downloadStream.CopyToAsync(fileStream);
                }

                AppendStatus("Kész! Felhő AI felskálázás befejeződött: " + outputFile, isError: false);
            }
            catch (Exception ex)
            {
                AppendStatus($"Hiba a felhő felskálázás közben: {ex.Message}", isError: true);
            }
        }

        private async Task<string> UploadToTmpFilesAsync(string filePath)
        {
            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(filePath);

            var fileContent = new StreamContent(fileStream);
            content.Add(fileContent, "file", Path.GetFileName(filePath));
            var response = await client.PostAsync("https://tmpfiles.org/api/v1/upload", content);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            string url = doc.RootElement.GetProperty("data").GetProperty("url").GetString() ?? "";
            return url.Replace("https://tmpfiles.org/", "https://tmpfiles.org/dl/");
        }

        #endregion

        #region Helpers

        // Full path to a bundled tool (yt-dlp.exe / ffmpeg.exe).
        private static string ToolPath(string toolName) => Path.Combine(AppContext.BaseDirectory, toolName);

        // App directory WITHOUT a trailing separator, so it is safe inside a quoted command-line argument.
        private static string AppDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        private static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken);

        private static readonly Guid FolderIdDownloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        // The user's real Downloads folder as Windows knows it (honours a moved folder, e.g. D:\Downloads).
        private static string GetDownloadsFolder()
        {
            try
            {
                string path = SHGetKnownFolderPath(FolderIdDownloads, 0, IntPtr.Zero);
                if (Directory.Exists(path)) return path;
            }
            catch { /* fall back below */ }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        // Last "ERROR:" line a tool printed, kept for the history entry of a failed download.
        private string _lastProcessError = "";

        // yt-dlp needs a JavaScript runtime to solve YouTube's player challenges. Without one YouTube
        // only offers SABR-only / unusable formats and the download dies with HTTP 403. Use the
        // deno.exe that fetch-tools.ps1 drops next to the app. Deno only: yt-dlp runs it with no file
        // system or network access, whereas Node's sandbox is weaker (e.g. network stays open).
        private static IEnumerable<string> YtDlpRuntimeArgs()
        {
            string deno = ToolPath("deno.exe");
            return File.Exists(deno) ? new[] { "--js-runtimes", "deno:" + deno } : Array.Empty<string>();
        }

        // "name.ext", or "name (1).ext", "name (2).ext", … if that already exists, so nothing is ever overwritten.
        private static string GetUniqueOutputPath(string folder, string baseName, string extension)
        {
            string candidate = Path.Combine(folder, baseName + extension);
            for (int i = 1; File.Exists(candidate); i++)
                candidate = Path.Combine(folder, $"{baseName} ({i}){extension}");
            return candidate;
        }

        private bool _ytDlpUpdateChecked;

        // YouTube changes constantly and an outdated yt-dlp soon starts failing (403, missing formats),
        // so run "yt-dlp -U" once per app session before the first download. Failure is non-fatal.
        private async Task EnsureYtDlpUpdatedAsync()
        {
            if (_ytDlpUpdateChecked) return;
            _ytDlpUpdateChecked = true;

            Dispatcher.Invoke(() => StatusLog.AppendText("yt-dlp frissítés ellenőrzése…" + Environment.NewLine));
            int exit = await RunProcessAsync(ToolPath("yt-dlp.exe"), new[] { "-U" });
            if (exit != 0)
                AppendStatus("A yt-dlp frissítése nem sikerült, a meglévő verzióval folytatom.", isError: true);
        }

        private void PasteInto(TextBox box)
        {
            try
            {
                if (Clipboard.ContainsText())
                    box.Text = Clipboard.GetText().Trim();
            }
            catch { /* clipboard access can occasionally fail; ignore */ }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                else
                    AppendStatus("A mappa nem található: " + path, isError: true);
            }
            catch (Exception ex)
            {
                AppendStatus("Nem sikerült megnyitni a mappát: " + ex.Message, isError: true);
            }
        }

        private void File_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private static string? GetDroppedFile(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                return files[0];
            return null;
        }

        private string SelectFolder(string initialPath)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Válassz mappát",
                InitialDirectory = Directory.Exists(initialPath) ? initialPath : "",
                Multiselect = false
            };
            return dialog.ShowDialog() == true ? dialog.FolderName : initialPath;
        }

        // The tool process currently running (yt-dlp / ffmpeg), so closing the window can stop it.
        private Process? _runningProcess;

        // Each argument is passed separately (ArgumentList), so no value — a link, a file name, a filter —
        // can ever be split into extra options by quotes or spaces.
        private async Task<int> RunProcessAsync(string fileName, IEnumerable<string> arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        WorkingDirectory = AppContext.BaseDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    // yt-dlp (Python) would otherwise write to the pipe in the ANSI code page,
                    // garbling accented titles (ő, ű, –) since we read the output as UTF-8.
                    psi.Environment["PYTHONIOENCODING"] = "utf-8";
                    foreach (string argument in arguments)
                        psi.ArgumentList.Add(argument);

                    using var process = new Process { StartInfo = psi };
                    process.OutputDataReceived += (s, args) => { if (args.Data != null) AppendStatus(args.Data, isError: false); };
                    process.ErrorDataReceived += (s, args) =>
                    {
                        if (args.Data == null) return;
                        if (args.Data.StartsWith("ERROR:")) _lastProcessError = args.Data;
                        AppendStatus(args.Data, isError: true);
                    };

                    process.Start();
                    _runningProcess = process;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    _runningProcess = null;
                    return process.ExitCode;
                }
                catch (Exception ex)
                {
                    _runningProcess = null;
                    AppendStatus($"Nem sikerült elindítani: {Path.GetFileName(fileName)} — {ex.Message}", isError: true);
                    return -1;
                }
            });
        }

        // Closing while yt-dlp / ffmpeg runs would leave it running invisibly in the background.
        // Ask first; if the user still wants to quit, stop the tool (and its child processes).
        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var running = _runningProcess;
            if (running == null) return;
            try { if (running.HasExited) return; } catch { return; }

            var answer = MessageBox.Show(this,
                "Még fut egy feladat (letöltés, konvertálás vagy felskálázás).\nHa most kilépsz, leáll, és a félkész fájl ott maradhat a mappában.\n\nBiztosan kilépsz?",
                "Zn Tools", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            StopRunningProcess();
        }

        private void StopRunningProcess()
        {
            try { _runningProcess?.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        private void StartProgress(bool indeterminate)
        {
            Dispatcher.Invoke(() =>
            {
                GlobalProgressBar.Visibility = Visibility.Visible;
                GlobalProgressBar.IsIndeterminate = indeterminate;
                GlobalProgressBar.Value = 0;
            });
        }

        private void SetUIEnabled(bool isEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                GlobalProgressBar.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
                if (isEnabled) GlobalProgressBar.IsIndeterminate = true;
                MainTabControl.IsEnabled = isEnabled;

                if (isEnabled)
                {
                    _converterSelectedFormat = "";
                    ApplyFormatCompatibility(updateText: false);   // only the targets that fit the current input
                    foreach (var btn in _formatButtons)
                    {
                        btn.ClearValue(BackgroundProperty);
                        btn.ClearValue(Button.BorderBrushProperty);
                        btn.ClearValue(ForegroundProperty);
                    }
                }
            });
        }

        private static readonly Regex _percentRegex = new Regex(@"\[download\]\s+(\d{1,3}(?:\.\d+)?)%", RegexOptions.Compiled);

        private void AppendStatus(string message, bool isError)
        {
            // Update the progress bar from yt-dlp download percentage
            var pctMatch = _percentRegex.Match(message);
            if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double pct))
            {
                Dispatcher.Invoke(() =>
                {
                    GlobalProgressBar.IsIndeterminate = false;
                    GlobalProgressBar.Value = pct;
                });
                // Intermediate percentages only drive the progress bar; logging every one of them would
                // bury the useful lines. The final "100% of X in 00:01:04" summary line is kept.
                if (!message.Contains(" in ")) return;
            }

            // Keep the log readable: only surface meaningful lines
            bool isRelevant =
                isError ||
                message.Contains("Kész") ||
                message.Contains("finished") ||
                message.Contains("Destination") ||
                message.Contains("has already been downloaded") ||
                message.StartsWith("[download]") ||
                message.StartsWith("[Merger]") ||
                message.StartsWith("[ExtractAudio]") ||
                message.StartsWith("Updated yt-dlp") || message.StartsWith("yt-dlp is up to date") ||
                message.StartsWith("Mentve ide:") ||
                message.StartsWith("Videó") || message.StartsWith("Feltöltés") || message.StartsWith("Feladat") ||
                message.StartsWith("Várakozás") || message.StartsWith("Felskálázott") || message.StartsWith("Felhő") ||
                message.Trim().StartsWith("frame=");

            if (!isRelevant) return;

            Dispatcher.Invoke(() =>
            {
                // yt-dlp prints warnings to stderr too; don't label those as errors.
                string prefix = !isError ? "" : message.StartsWith("WARNING:") ? "FIGYELEM: " : "HIBA: ";
                StatusLog.AppendText(prefix + message + Environment.NewLine);
                StatusLog.ScrollToEnd();
            });
        }

        #endregion
    }
}
