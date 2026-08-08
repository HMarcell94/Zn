using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

        public MainWindow()
        {
            InitializeComponent();

            // Default save location = user's Downloads folder for every tool
            string defaultDownloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            _ytSavePath = defaultDownloads;
            _pinSavePath = defaultDownloads;
            _converterSavePath = defaultDownloads;
            _upscalerSavePath = defaultDownloads;

            YtPathTextBox.Text = _ytSavePath;
            PinPathTextBox.Text = _pinSavePath;
            OutputPathTextBox.Text = _converterSavePath;
            UpscalerOutputPathTextBox.Text = _upscalerSavePath;

            FindFormatButtons(FormatButtonsWrapPanel);

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
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Üdv! Válassz egy fület fent a kezdéshez.";
        }

        #endregion

        private void FindFormatButtons(Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Button btn)
                {
                    _formatButtons.Add(btn);
                    btn.Click += FormatButton_Click;
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
            string url = YtUrlTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                AppendStatus("Illessz be egy YouTube linket először.", isError: true);
                return;
            }

            bool audio = YtAudioModeRadio.IsChecked == true;
            string common = $"-o \"%(title)s.%(ext)s\" -P \"{_ytSavePath}\" \"{url}\" --ffmpeg-location \"{AppDir}\"";
            string arguments;

            if (audio)
            {
                string fmt = (YtAudioFormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mp3";
                arguments = $"-x --audio-format {fmt} --audio-quality 0 {common}";
            }
            else
            {
                string quality = (YtVideoQualityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Legjobb elérhető";
                var heightMatch = Regex.Match(quality, @"(\d+)");
                string fmtSelector = heightMatch.Success
                    ? $"bv*[height<={heightMatch.Groups[1].Value}]+ba/b[height<={heightMatch.Groups[1].Value}]"
                    : "bv*+ba/b";
                arguments = $"-f \"{fmtSelector}\" --merge-output-format mp4 {common}";
            }

            SetUIEnabled(false);
            StartProgress(indeterminate: true);
            StatusTextBlock.Text = (audio ? "Zene letöltése indul…" : "Videó letöltése indul…") + Environment.NewLine;

            int exit = await RunProcessAsync(ToolPath("yt-dlp.exe"), arguments);

            AppendStatus(exit == 0 ? "Kész! A letöltés befejeződött." : $"A letöltés hibával zárult (kód: {exit}).", isError: exit != 0);
            SetUIEnabled(true);
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
            string url = PinUrlTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                AppendStatus("Illessz be egy Pinterest linket először.", isError: true);
                return;
            }

            string selectedFormat = (PinFormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mp4";
            string common = $"-o \"%(title)s.%(ext)s\" -P \"{_pinSavePath}\" \"{url}\" --ffmpeg-location \"{AppDir}\"";
            string arguments = selectedFormat.Contains("mp4")
                ? $"-f mp4 {common}"
                : common;

            SetUIEnabled(false);
            StartProgress(indeterminate: true);
            StatusTextBlock.Text = "Pinterest videó letöltése indul…" + Environment.NewLine;

            int exit = await RunProcessAsync(ToolPath("yt-dlp.exe"), arguments);

            AppendStatus(exit == 0 ? "Kész! A letöltés befejeződött." : $"A letöltés hibával zárult (kód: {exit}).", isError: exit != 0);
            SetUIEnabled(true);
        }

        #endregion

        #region File Converter

        private void InputFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Válassz bemeneti fájlt", CheckFileExists = true, CheckPathExists = true };
            if (dialog.ShowDialog() == true)
            {
                _converterInputFile = dialog.FileName;
                InputFileTextBox.Text = _converterInputFile;
            }
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

            clickedButton.Background = (Brush)new BrushConverter().ConvertFromString("#FF3D9E")!;
            clickedButton.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#FF3D9E")!;
            clickedButton.Foreground = Brushes.White;
        }

        private async void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_converterInputFile) || string.IsNullOrWhiteSpace(_converterSavePath) || string.IsNullOrWhiteSpace(_converterSelectedFormat))
            {
                AppendStatus("Válassz bemeneti fájlt, cél formátumot és mentési mappát.", isError: true);
                return;
            }

            string inputFileName = Path.GetFileNameWithoutExtension(_converterInputFile) ?? "converted_file";
            string outputFile = Path.Combine(_converterSavePath, inputFileName + _converterSelectedFormat);

            string arguments = _converterSelectedFormat.ToLower() == ".ico"
                ? $"-i \"{_converterInputFile}\" -vf \"scale=min(256,iw):min(256,ih):force_original_aspect_ratio=decrease\" \"{outputFile}\" -y"
                : $"-i \"{_converterInputFile}\" \"{outputFile}\" -y";

            SetUIEnabled(false);
            StartProgress(indeterminate: true);
            StatusTextBlock.Text = $"Konvertálás indul ({_converterSelectedFormat})…" + Environment.NewLine;

            int exit = await RunProcessAsync(ToolPath("ffmpeg.exe"), arguments);

            AppendStatus(exit == 0 ? "Kész! A konvertálás befejeződött." : $"A konvertálás hibával zárult (kód: {exit}).", isError: exit != 0);
            SetUIEnabled(true);
        }

        private void ConverterInput_Drop(object sender, DragEventArgs e)
        {
            string? file = GetDroppedFile(e);
            if (file != null)
            {
                _converterInputFile = file;
                InputFileTextBox.Text = file;
            }
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
            string outputExtension = Path.GetExtension(_upscalerInputFile);
            if (string.IsNullOrEmpty(outputExtension)) outputExtension = ".mp4";

            SetUIEnabled(false);
            StartProgress(indeterminate: true);

            if (LocalUpscalerRadioButton.IsChecked == true)
            {
                string scale = (LocalScaleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "2x";
                string algorithm = (LocalAlgorithmComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Lanczos";
                string scaleFactor = scale.Replace("x", "");

                string ffmpegFilter = algorithm switch
                {
                    var a when a.Contains("Lanczos") => $"scale=iw*{scaleFactor}:ih*{scaleFactor}:flags=lanczos",
                    var a when a.Contains("xBR") => $"xbr={scaleFactor}",
                    _ => $"scale=iw*{scaleFactor}:ih*{scaleFactor}:flags=bicubic"
                };

                string outputFile = Path.Combine(_upscalerSavePath, $"{inputFileName}_{scale}_{algorithm.Split(' ')[0].ToLower()}{outputExtension}");
                StatusTextBlock.Text = $"Helyi felskálázás indul ({scale} {algorithm.Split(' ')[0]})…" + Environment.NewLine;

                string arguments = $"-i \"{_upscalerInputFile}\" -vf \"{ffmpegFilter}\" -c:v libx264 -preset medium -crf 20 -c:a copy \"{outputFile}\" -y";
                int exit = await RunProcessAsync(ToolPath("ffmpeg.exe"), arguments);

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

            StatusTextBlock.Text = "Felhő AI felskálázás indul…" + Environment.NewLine;

            try
            {
                AppendStatus("Videó feltöltése a felhőbe (tmpfiles.org)…", isError: false);
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
                string statusUrl = doc.RootElement.TryGetProperty("status_url", out var su)
                    ? su.GetString() ?? $"https://queue.fal.run/{modelId}/requests/{requestId}/status"
                    : $"https://queue.fal.run/{modelId}/requests/{requestId}/status";

                AppendStatus($"Feladat a sorban. Azonosító: {requestId}. Állapot figyelése…", isError: false);

                string upscaledVideoUrl = "";
                bool completed = false;
                int pollCount = 0;

                while (!completed && pollCount < 120) // Max ~10 minutes
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
                        if (statusDoc.RootElement.TryGetProperty("video", out var videoObj) && videoObj.TryGetProperty("url", out var urlProp))
                            upscaledVideoUrl = urlProp.GetString() ?? "";
                        else if (statusDoc.RootElement.TryGetProperty("video_url", out var urlProp2))
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

                if (string.IsNullOrEmpty(upscaledVideoUrl))
                    throw new Exception("A feldolgozás befejeződött, de nem érkezett kimeneti videó URL.");

                AppendStatus("Felskálázott videó letöltése…", isError: false);
                string outputFile = Path.Combine(_upscalerSavePath, $"{inputFileName}_upscaled_ai{outputExtension}");
                using (var downloadStream = await client.GetStreamAsync(upscaledVideoUrl))
                using (var fileStream = File.Create(outputFile))
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

        private async Task<int> RunProcessAsync(string fileName, string arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        WorkingDirectory = AppContext.BaseDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using var process = new Process { StartInfo = psi };
                    process.OutputDataReceived += (s, args) => { if (args.Data != null) AppendStatus(args.Data, isError: false); };
                    process.ErrorDataReceived += (s, args) => { if (args.Data != null) AppendStatus(args.Data, isError: true); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    return process.ExitCode;
                }
                catch (Exception ex)
                {
                    AppendStatus($"Nem sikerült elindítani: {Path.GetFileName(fileName)} — {ex.Message}", isError: true);
                    return -1;
                }
            });
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
                    foreach (var btn in _formatButtons)
                    {
                        btn.IsEnabled = true;
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
                message.StartsWith("Videó") || message.StartsWith("Feltöltés") || message.StartsWith("Feladat") ||
                message.StartsWith("Várakozás") || message.StartsWith("Felskálázott") || message.StartsWith("Felhő") ||
                message.Trim().StartsWith("frame=");

            if (!isRelevant) return;

            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text += (isError ? "HIBA: " : "") + message + Environment.NewLine;
                (StatusTextBlock.Parent as ScrollViewer)?.ScrollToEnd();
            });
        }

        #endregion
    }
}
