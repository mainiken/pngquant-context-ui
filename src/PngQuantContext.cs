using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Windows.Forms;

internal static class PngQuantContext
{
    private static readonly string AppDirectory = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string QueueDirectory = Path.Combine(Path.GetTempPath(), "PngQuantContext");
    private static readonly string LogPath = Path.Combine(AppDirectory, "PngQuantContext.log");
    private const string PngQuantDownloadUrl = "https://pngquant.org/pngquant-windows.zip";
    private const string PngQuantWebsiteUrl = "https://pngquant.org/";
    private const string GitHubUrl = "https://github.com/mainiken/pngquant-context-ui";
    private const string AppVersion = "0.4.0";

    [STAThread]
    private static int Main(string[] args)
    {
        var options = ParseArguments(args);
        var files = options.Files;
        Mutex instance = null;

        if (files.Count > 0)
        {
            QueueFiles(files);

            bool ownsInstance;
            instance = new Mutex(true, "Local\\PngQuantContext", out ownsInstance);
            if (!ownsInstance)
            {
                instance.Dispose();
                return 0;
            }

            Thread.Sleep(700);
            files = ReadQueuedFiles();
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var pngquant = EnsurePngQuantAvailable();
        if (pngquant == null || files.Count == 0)
        {
            MessageBox.Show(
                pngquant == null
                    ? "pngquant.exe is required to compress PNG files."
                    : "No PNG file was passed to the application.",
                "Compress PNG",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            if (instance != null)
            {
                instance.Dispose();
            }
            return 1;
        }

        using (var form = new MainForm(pngquant, files, options))
        {
            Application.Run(form);
        }

        if (instance != null)
        {
            instance.Dispose();
        }

        return 0;
    }

    private static string EnsurePngQuantAvailable()
    {
        var pngquant = FindPngQuant();
        if (pngquant != null)
        {
            return pngquant;
        }

        using (var form = new PngQuantInstallForm())
        {
            if (form.ShowDialog() == DialogResult.OK)
            {
                return FindPngQuant();
            }
        }

        return null;
    }

    private static RunOptions ParseArguments(string[] args)
    {
        var options = new RunOptions();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var lower = arg.ToLowerInvariant();

            if (lower == "--auto")
            {
                options.AutoRun = true;
                continue;
            }

            if (lower == "--nofs")
            {
                options.NoDither = true;
                continue;
            }

            if (lower == "--mode" && i + 1 < args.Length)
            {
                var mode = args[++i].ToLowerInvariant();
                options.Replace = mode == "replace";
                continue;
            }

            if (lower == "--preset" && i + 1 < args.Length)
            {
                options.Preset = args[++i].ToLowerInvariant();
                continue;
            }

            if (File.Exists(arg) && seen.Add(arg))
            {
                options.Files.Add(arg);
            }
        }

        return options;
    }

    private static string FindPngQuant()
    {
        var candidates = new[]
        {
            Path.Combine(AppDirectory, "pngquant.exe"),
            Path.Combine(AppDirectory, "pngquant", "pngquant.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void QueueFiles(List<string> files)
    {
        Directory.CreateDirectory(QueueDirectory);

        using (var queueLock = new Mutex(false, "Local\\PngQuantContextQueue"))
        {
            queueLock.WaitOne();
            try
            {
                using (var writer = new StreamWriter(GetQueuePath(), true))
                {
                    foreach (var file in files)
                    {
                        writer.WriteLine(file);
                    }
                }
            }
            finally
            {
                queueLock.ReleaseMutex();
            }
        }
    }

    private static List<string> ReadQueuedFiles()
    {
        Directory.CreateDirectory(QueueDirectory);

        using (var queueLock = new Mutex(false, "Local\\PngQuantContextQueue"))
        {
            queueLock.WaitOne();
            try
            {
                var result = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queuePath = GetQueuePath();

                if (File.Exists(queuePath))
                {
                    foreach (var line in File.ReadAllLines(queuePath))
                    {
                        if (File.Exists(line) && seen.Add(line))
                        {
                            result.Add(line);
                        }
                    }

                    File.Delete(queuePath);
                }

                return result;
            }
            finally
            {
                queueLock.ReleaseMutex();
            }
        }
    }

    private static string GetQueuePath()
    {
        return Path.Combine(QueueDirectory, "files.txt");
    }

    private sealed class PngQuantInstallForm : Form
    {
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly Button _downloadButton;
        private readonly Button _websiteButton;
        private readonly Button _cancelButton;
        private bool _downloadStarted;

        public PngQuantInstallForm()
        {
            Text = "pngquant is required";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(500, 190);
            Font = new Font("Segoe UI", 9);

            var title = new Label
            {
                AutoSize = false,
                Location = new Point(18, 16),
                Size = new Size(464, 24),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Text = "PngQuant Context UI needs pngquant.exe"
            };

            var description = new Label
            {
                AutoSize = false,
                Location = new Point(18, 48),
                Size = new Size(464, 44),
                Text = "This app is a Windows context-menu UI for pngquant. It cannot compress PNG files until the pngquant command-line binary is installed."
            };

            _status = new Label
            {
                AutoSize = false,
                Location = new Point(18, 98),
                Size = new Size(464, 22),
                Text = "Download the official Windows binary from pngquant.org?"
            };

            _progress = new ProgressBar
            {
                Location = new Point(18, 126),
                Size = new Size(464, 18),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            _downloadButton = new Button
            {
                Location = new Point(188, 154),
                Size = new Size(110, 28),
                Text = "Download"
            };
            _downloadButton.Click += delegate { StartDownload(); };

            _websiteButton = new Button
            {
                Location = new Point(304, 154),
                Size = new Size(92, 28),
                Text = "Website"
            };
            _websiteButton.Click += delegate
            {
                Process.Start(new ProcessStartInfo(PngQuantWebsiteUrl) { UseShellExecute = true });
            };

            _cancelButton = new Button
            {
                Location = new Point(402, 154),
                Size = new Size(80, 28),
                Text = "Cancel"
            };
            _cancelButton.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.Add(title);
            Controls.Add(description);
            Controls.Add(_status);
            Controls.Add(_progress);
            Controls.Add(_downloadButton);
            Controls.Add(_websiteButton);
            Controls.Add(_cancelButton);
        }

        private void StartDownload()
        {
            if (_downloadStarted)
            {
                return;
            }

            _downloadStarted = true;
            _downloadButton.Enabled = false;
            _websiteButton.Enabled = false;
            _cancelButton.Enabled = false;
            _status.Text = "Downloading pngquant...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var installedPath = DownloadAndInstallPngQuant();
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _progress.Value = 100;
                        _status.Text = "Installed: " + installedPath;
                        DialogResult = DialogResult.OK;
                        Close();
                    });
                }
                catch (Exception ex)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _status.Text = "Download failed. Use Website to install pngquant manually.";
                        _downloadButton.Enabled = true;
                        _websiteButton.Enabled = true;
                        _cancelButton.Enabled = true;
                        _downloadStarted = false;
                        MessageBox.Show(
                            ex.Message,
                            "pngquant download failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    });
                }
            });
        }

        private string DownloadAndInstallPngQuant()
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;

            var tempZip = Path.Combine(Path.GetTempPath(), "pngquant-windows.zip");
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }

            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs args)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _progress.Value = Math.Min(Math.Max(args.ProgressPercentage, 0), 100);
                    });
                };
                client.DownloadFile(PngQuantDownloadUrl, tempZip);
            }

            var targetDir = Path.Combine(AppDirectory, "pngquant");
            var targetExe = Path.Combine(targetDir, "pngquant.exe");
            Directory.CreateDirectory(targetDir);

            using (var zip = ZipFile.OpenRead(tempZip))
            {
                ZipArchiveEntry pngquantEntry = null;

                foreach (var entry in zip.Entries)
                {
                    if (string.Equals(Path.GetFileName(entry.FullName), "pngquant.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        pngquantEntry = entry;
                        break;
                    }
                }

                if (pngquantEntry == null)
                {
                    throw new InvalidOperationException("The downloaded archive does not contain pngquant.exe.");
                }

                using (var source = pngquantEntry.Open())
                using (var target = File.Create(targetExe))
                {
                    source.CopyTo(target);
                }
            }

            try
            {
                File.Delete(tempZip);
            }
            catch
            {
            }

            return targetExe;
        }
    }

    private sealed class RunOptions
    {
        public readonly List<string> Files = new List<string>();
        public bool AutoRun;
        public bool Replace;
        public bool NoDither;
        public string Preset = "balanced";
    }

    private sealed class MainForm : Form
    {
        private readonly string _pngquant;
        private readonly List<FileJob> _jobs = new List<FileJob>();
        private readonly Panel _header;
        private readonly Label _title;
        private readonly Label _summary;
        private readonly Label _current;
        private readonly Label _details;
        private readonly ThemedProgressBar _overallProgress;
        private readonly FileQueueView _fileView;
        private readonly Button _settingsButton;
        private readonly Button _startButton;
        private readonly Button _cancelButton;
        private readonly Button _closeButton;
        private readonly ContextMenuStrip _settingsMenu;
        private readonly ToolStripMenuItem _copyMenuItem;
        private readonly ToolStripMenuItem _replaceMenuItem;
        private readonly ToolStripMenuItem _balancedMenuItem;
        private readonly ToolStripMenuItem _qualityMenuItem;
        private readonly ToolStripMenuItem _fastMenuItem;
        private readonly ToolStripMenuItem _noDitherMenuItem;
        private readonly ToolStripMenuItem _lightMenuItem;
        private readonly ToolStripMenuItem _darkMenuItem;
        private readonly ToolTip _toolTip;
        private readonly object _processLock = new object();
        private readonly bool _autoRun;
        private volatile bool _cancelRequested;
        private Process _currentProcess;
        private bool _running;
        private bool _hasStarted;
        private bool _runReplace;
        private bool _runNoDither;
        private string _runSpeed = "3";
        private bool _settingsReplace;
        private bool _settingsNoDither;
        private string _settingsPreset;
        private string _settingsTheme;
        private ThemePalette _palette;

        public MainForm(string pngquant, List<string> files, RunOptions options)
        {
            _pngquant = pngquant;
            _autoRun = options.AutoRun;

            foreach (var file in files)
            {
                _jobs.Add(new FileJob(file));
            }

            Text = "PngQuant Context UI";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(380, 500);
            ClientSize = new Size(400, 560);
            Font = new Font("Segoe UI", 9);
            AutoScaleMode = AutoScaleMode.Font;
            TrySetWindowIcon();

            _settingsReplace = options.Replace;
            _settingsNoDither = options.NoDither;
            _settingsPreset = NormalizePreset(options.Preset);
            _settingsTheme = LoadThemePreference();
            _palette = ThemePalette.FromName(_settingsTheme);
            _toolTip = new ToolTip();

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66
            };

            _title = new Label
            {
                AutoSize = false,
                Location = new Point(18, 14),
                Size = new Size(190, 24),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Text = "PNG compression"
            };

            _summary = new Label
            {
                AutoSize = false,
                Location = new Point(18, 39),
                Size = new Size(260, 20),
                Text = BuildReadySummary()
            };

            _settingsButton = new Button
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = false,
                Location = new Point(352, 17),
                Size = new Size(30, 28),
                Text = "...",
                TabStop = false
            };
            _settingsButton.Click += delegate
            {
                ShowSettingsMenu();
            };
            _toolTip.SetToolTip(_settingsButton, "Settings");

            _settingsMenu = new ContextMenuStrip();
            _copyMenuItem = AddMenuItem("Mode: Copy", delegate { SetMode(false); });
            _replaceMenuItem = AddMenuItem("Mode: Replace", delegate { SetMode(true); });
            _settingsMenu.Items.Add(new ToolStripSeparator());
            _balancedMenuItem = AddMenuItem("Preset: Balanced", delegate { SetPreset("balanced"); });
            _qualityMenuItem = AddMenuItem("Preset: Quality", delegate { SetPreset("quality"); });
            _fastMenuItem = AddMenuItem("Preset: Fast", delegate { SetPreset("fast"); });
            _settingsMenu.Items.Add(new ToolStripSeparator());
            _noDitherMenuItem = AddMenuItem("No dithering", delegate { ToggleNoDither(); });
            _settingsMenu.Items.Add(new ToolStripSeparator());
            _lightMenuItem = AddMenuItem("Theme: Light", delegate { SetTheme("Light"); });
            _darkMenuItem = AddMenuItem("Theme: Dark", delegate { SetTheme("Dark"); });
            _settingsMenu.Items.Add(new ToolStripSeparator());
            AddMenuItem("About", delegate { ShowAbout(); });
            AddMenuItem("Open GitHub", delegate { OpenGitHub(); });
            UpdateSettingsMenu();

            _header.Controls.Add(_title);
            _header.Controls.Add(_summary);
            _header.Controls.Add(_settingsButton);

            _startButton = new Button
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(186, 520),
                Size = new Size(86, 28),
                Text = "Compress",
                Visible = !_autoRun
            };
            _startButton.Click += delegate { StartCompression(); };

            _cancelButton = new Button
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(278, 520),
                Size = new Size(64, 28),
                Text = "Cancel",
                Enabled = false,
                Visible = false
            };
            _cancelButton.Click += delegate { CancelCompression(); };

            _closeButton = new Button
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(278, 520),
                Size = new Size(62, 28),
                Text = "Close"
            };
            _closeButton.Click += delegate { Close(); };

            _fileView = new FileQueueView
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(14, 74),
                Size = new Size(412, 330),
                Font = Font
            };
            _fileView.SetJobs(_jobs);

            _current = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Location = new Point(14, 502),
                Size = new Size(412, 20),
                Text = "Ready."
            };

            _overallProgress = new ThemedProgressBar
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(14, 526),
                Size = new Size(412, 8),
                Minimum = 0,
                Maximum = Math.Max(_jobs.Count, 1),
                Value = 0
            };

            _details = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Location = new Point(14, 539),
                Size = new Size(412, 18),
                Text = "Ready to compress."
            };

            Controls.Add(_details);
            Controls.Add(_overallProgress);
            Controls.Add(_current);
            Controls.Add(_startButton);
            Controls.Add(_cancelButton);
            Controls.Add(_closeButton);
            Controls.Add(_fileView);
            Controls.Add(_header);
            ApplyTheme();
            LayoutContent();

            Resize += delegate
            {
                LayoutContent();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (_running)
                {
                    args.Cancel = true;
                    CancelCompression();
                }
            };
            Shown += delegate
            {
                LayoutContent();
                if (_autoRun)
                {
                    StartCompression();
                }
            };
        }

        private void LayoutContent()
        {
            var width = Math.Max(ClientSize.Width, MinimumSize.Width);
            var height = Math.Max(ClientSize.Height, MinimumSize.Height);
            var left = 14;
            var right = width - 14;
            var contentWidth = Math.Max(260, width - (left * 2));
            var listTop = _header.Height + 8;
            var buttonTop = height - 42;
            var listHeight = Math.Max(170, buttonTop - listTop - 76);

            _settingsButton.Location = new Point(right - _settingsButton.Width, 17);
            _summary.Width = Math.Max(190, _settingsButton.Left - _summary.Left - 12);

            _closeButton.Location = new Point(right - _closeButton.Width, buttonTop);
            if (_cancelButton.Visible)
            {
                _cancelButton.Location = new Point(right - _cancelButton.Width, buttonTop);
                _startButton.Location = new Point(_cancelButton.Left - _startButton.Width - 8, buttonTop);
            }
            else
            {
                _startButton.Location = new Point(_closeButton.Left - _startButton.Width - 8, buttonTop);
            }

            _fileView.Location = new Point(left, listTop);
            _fileView.Size = new Size(contentWidth, listHeight);

            var footerTop = _fileView.Bottom + 8;
            _current.Location = new Point(left, footerTop);
            _current.Size = new Size(contentWidth, 20);
            _overallProgress.Location = new Point(left, footerTop + 28);
            _overallProgress.Size = new Size(contentWidth, 8);
            _details.Location = new Point(left, footerTop + 48);
            _details.Size = new Size(Math.Max(120, _startButton.Left - left - 10), 18);
        }

        private void ApplyTheme()
        {
            _palette = ThemePalette.FromName(GetSelectedThemeName());

            BackColor = _palette.Window;
            _header.BackColor = _palette.Window;
            _fileView.SetPalette(_palette);
            _overallProgress.SetPalette(_palette);

            ApplyLabel(_title, _palette.Text);
            ApplyLabel(_summary, _palette.Muted);
            ApplyLabel(_current, _palette.Text);
            ApplyLabel(_details, _palette.Muted);

            ApplyButton(_settingsButton, false);
            ApplyButton(_startButton, true);
            ApplyButton(_cancelButton, false);
            ApplyButton(_closeButton, false);
            UpdateSettingsMenu();

            _fileView.Invalidate();
        }

        private void ApplyLabel(Label label, Color color)
        {
            label.BackColor = Color.Transparent;
            label.ForeColor = color;
        }

        private void ApplyButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = primary ? _palette.Accent : _palette.Input;
            button.ForeColor = primary ? Color.White : _palette.Text;
            button.FlatAppearance.BorderColor = primary ? _palette.Accent : _palette.Border;
            button.FlatAppearance.BorderSize = 1;
        }

        private ToolStripMenuItem AddMenuItem(string text, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += handler;
            _settingsMenu.Items.Add(item);
            return item;
        }

        private void ShowSettingsMenu()
        {
            UpdateSettingsMenu();
            _settingsMenu.Show(_settingsButton, new Point(0, _settingsButton.Height + 2));
        }

        private void SetMode(bool replace)
        {
            _settingsReplace = replace;
            UpdateSettingsMenu();
        }

        private void SetPreset(string preset)
        {
            _settingsPreset = NormalizePreset(preset);
            UpdateSettingsMenu();
        }

        private void ToggleNoDither()
        {
            _settingsNoDither = !_settingsNoDither;
            UpdateSettingsMenu();
        }

        private void SetTheme(string theme)
        {
            _settingsTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
            SaveThemePreference(_settingsTheme);
            ApplyTheme();
        }

        private void UpdateSettingsMenu()
        {
            _settingsMenu.BackColor = _palette.Surface;
            _settingsMenu.ForeColor = _palette.Text;

            _copyMenuItem.Checked = !_settingsReplace;
            _replaceMenuItem.Checked = _settingsReplace;
            _balancedMenuItem.Checked = _settingsPreset == "balanced";
            _qualityMenuItem.Checked = _settingsPreset == "quality";
            _fastMenuItem.Checked = _settingsPreset == "fast";
            _noDitherMenuItem.Checked = _settingsNoDither;
            _lightMenuItem.Checked = _settingsTheme != "Dark";
            _darkMenuItem.Checked = _settingsTheme == "Dark";
        }

        private void ShowAbout()
        {
            var result = MessageBox.Show(
                "PngQuant Context UI" + Environment.NewLine +
                "Version " + AppVersion + Environment.NewLine +
                Environment.NewLine +
                "Windows 11 context-menu UI for pngquant." + Environment.NewLine +
                GitHubUrl + Environment.NewLine +
                Environment.NewLine +
                "Open GitHub?",
                "About",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                OpenGitHub();
            }
        }

        private static void OpenGitHub()
        {
            try
            {
                Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void TrySetWindowIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    Icon = icon;
                }
            }
            catch
            {
            }
        }

        private string GetSelectedThemeName()
        {
            return string.Equals(_settingsTheme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        }

        private static string NormalizePreset(string preset)
        {
            if (string.Equals(preset, "quality", StringComparison.OrdinalIgnoreCase))
            {
                return "quality";
            }

            if (string.Equals(preset, "fast", StringComparison.OrdinalIgnoreCase))
            {
                return "fast";
            }

            return "balanced";
        }

        private static string LoadThemePreference()
        {
            try
            {
                var settingsPath = GetSettingsPath();
                if (File.Exists(settingsPath))
                {
                    foreach (var line in File.ReadAllLines(settingsPath))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("Theme=", StringComparison.OrdinalIgnoreCase))
                        {
                            var value = trimmed.Substring("Theme=".Length).Trim();
                            if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase))
                            {
                                return "Dark";
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return "Light";
        }

        private static void SaveThemePreference(string theme)
        {
            try
            {
                var settingsPath = GetSettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, "Theme=" + theme + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string GetSettingsPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                return Path.Combine(AppDirectory, "settings.ini");
            }

            return Path.Combine(localAppData, "PngQuantContext", "settings.ini");
        }

        private void StartCompression()
        {
            if (_running)
            {
                return;
            }

            _runReplace = _settingsReplace;
            _runNoDither = _settingsNoDither;
            _runSpeed = _settingsPreset == "quality" ? "1" : (_settingsPreset == "fast" ? "8" : "3");
            _cancelRequested = false;
            _running = true;
            _hasStarted = true;

            foreach (var job in _jobs)
            {
                job.AfterLength = -1;
                job.SavedBytes = 0;
                job.Status = "Queued";
                job.Success = false;
            }

            RefreshAllRows();
            SetControlsForRunning(true);
            SetOverallProgress(0);
            SetCurrent("Starting compression...");

            ThreadPool.QueueUserWorkItem(delegate { CompressFiles(); });
        }

        private void CancelCompression()
        {
            if (!_running)
            {
                return;
            }

            _cancelRequested = true;
            SetCurrent("Cancelling...");
            _cancelButton.Enabled = false;

            lock (_processLock)
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    TryKill(_currentProcess);
                }
            }
        }

        private void CompressFiles()
        {
            var done = 0;
            var failed = 0;
            var cancelled = false;
            var replace = _runReplace;
            var noDither = _runNoDither;
            var speed = _runSpeed;

            try
            {
                File.WriteAllText(LogPath, "PngQuantContext started " + DateTime.Now + Environment.NewLine);
            }
            catch
            {
            }

            for (var i = 0; i < _jobs.Count; i++)
            {
                if (_cancelRequested)
                {
                    cancelled = true;
                    MarkRemainingCancelled(i);
                    break;
                }

                var job = _jobs[i];
                UpdateJob(i, "Processing...", null, null);
                SetCurrent("Compressing " + (i + 1) + " of " + _jobs.Count + ": " + job.Name);

                var result = CompressOne(job, replace, speed, noDither);
                AppendLog(job.Path, result.Arguments, result.ExitCode, result.TimedOut, result.OutputPath, job.BeforeLength, result.AfterLength);

                if (result.Cancelled)
                {
                    cancelled = true;
                    UpdateJob(i, "Cancelled", null, false);
                    MarkRemainingCancelled(i + 1);
                    break;
                }

                if (result.Success)
                {
                    done++;
                    UpdateJob(i, "Done", result.AfterLength, true);
                }
                else
                {
                    failed++;
                    UpdateJob(i, result.TimedOut ? "Timed out" : "Failed", result.AfterLength, false);
                }

                SetOverallProgress(i + 1);
            }

            Finish(done, failed, cancelled);
        }

        private CompressionResult CompressOne(FileJob job, bool replace, string speed, bool noDither)
        {
            var ext = replace ? ".png" : "-compressed.png";
            var outputPath = replace
                ? job.Path
                : Path.Combine(Path.GetDirectoryName(job.Path), Path.GetFileNameWithoutExtension(job.Path) + "-compressed.png");
            var arguments = BuildArguments(ext, speed, noDither, job.Path);
            var result = new CompressionResult
            {
                Arguments = arguments,
                OutputPath = outputPath,
                ExitCode = -1,
                AfterLength = -1
            };

            using (var process = new Process())
            {
                process.StartInfo.FileName = _pngquant;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(job.Path);
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

                lock (_processLock)
                {
                    _currentProcess = process;
                }

                process.Start();
                var started = DateTime.UtcNow;

                while (!process.WaitForExit(200))
                {
                    if (_cancelRequested)
                    {
                        result.Cancelled = true;
                        TryKill(process);
                        break;
                    }

                    if ((DateTime.UtcNow - started).TotalMilliseconds > 120000)
                    {
                        result.TimedOut = true;
                        TryKill(process);
                        break;
                    }
                }

                if (_cancelRequested)
                {
                    result.Cancelled = true;
                }
                else if (!result.TimedOut)
                {
                    result.ExitCode = process.ExitCode;
                }

                lock (_processLock)
                {
                    if (ReferenceEquals(_currentProcess, process))
                    {
                        _currentProcess = null;
                    }
                }
            }

            result.AfterLength = File.Exists(outputPath) ? new FileInfo(outputPath).Length : -1;
            result.Success = !result.Cancelled && !result.TimedOut && result.ExitCode == 0 && result.AfterLength > 0;
            return result;
        }

        private void Finish(int done, int failed, bool cancelled)
        {
            RunOnUi(delegate
            {
                _running = false;
                SetControlsForRunning(false);
                _overallProgress.Value = Math.Min(_overallProgress.Maximum, CountFinishedRows());

                if (cancelled)
                {
                    _current.Text = "Cancelled. Completed " + done + " file(s).";
                }
                else if (failed == 0)
                {
                    _current.Text = "Done. Compressed " + done + " file(s).";
                }
                else
                {
                    _current.Text = "Done. Compressed " + done + ", failed " + failed + ".";
                }

                _details.Text = failed == 0
                    ? "Total saved: " + FormatBytes(CalculateTotalSaved())
                    : "Details: " + LogPath;
                _closeButton.Focus();
            });
        }

        private void SetControlsForRunning(bool running)
        {
            _settingsButton.Enabled = !running;
            _startButton.Visible = !running && !_hasStarted;
            _startButton.Enabled = !running;
            _cancelButton.Visible = running;
            _cancelButton.Enabled = running;
            _closeButton.Visible = !running;
            _closeButton.Enabled = !running;
            LayoutContent();
        }

        private void MarkRemainingCancelled(int startIndex)
        {
            for (var i = startIndex; i < _jobs.Count; i++)
            {
                UpdateJob(i, "Cancelled", null, false);
            }
        }

        private void UpdateJob(int index, string status, long? afterLength, bool? success)
        {
            RunOnUi(delegate
            {
                var job = _jobs[index];
                job.Status = status;

                if (afterLength.HasValue)
                {
                    job.AfterLength = afterLength.Value;
                    job.SavedBytes = Math.Max(0, job.BeforeLength - job.AfterLength);
                }

                if (success.HasValue)
                {
                    job.Success = success.Value;
                }

                _fileView.EnsureVisible(index);
                _fileView.Invalidate();
                _summary.Text = BuildProgressSummary();
            });
        }

        private void RefreshAllRows()
        {
            _fileView.Invalidate();
        }

        private void SetOverallProgress(int value)
        {
            RunOnUi(delegate
            {
                _overallProgress.Value = Math.Min(Math.Max(value, _overallProgress.Minimum), _overallProgress.Maximum);
            });
        }

        private void SetCurrent(string text)
        {
            RunOnUi(delegate { _current.Text = text; });
        }

        private int CountFinishedRows()
        {
            var count = 0;
            foreach (var job in _jobs)
            {
                if (job.Status == "Done" || job.Status == "Failed" || job.Status == "Timed out" || job.Status == "Cancelled")
                {
                    count++;
                }
            }

            return count;
        }

        private long CalculateTotalSaved()
        {
            long saved = 0;
            foreach (var job in _jobs)
            {
                saved += Math.Max(0, job.SavedBytes);
            }

            return saved;
        }

        private string BuildReadySummary()
        {
            return _jobs.Count + " file(s), total input " + FormatBytes(CalculateTotalInput());
        }

        private string BuildProgressSummary()
        {
            var done = 0;
            var failed = 0;
            foreach (var job in _jobs)
            {
                if (job.Status == "Done")
                {
                    done++;
                }
                else if (job.Status == "Failed" || job.Status == "Timed out")
                {
                    failed++;
                }
            }

            return "Done " + done + " / " + _jobs.Count + (failed > 0 ? ", failed " + failed : "") + ", saved " + FormatBytes(CalculateTotalSaved());
        }

        private long CalculateTotalInput()
        {
            long total = 0;
            foreach (var job in _jobs)
            {
                total += Math.Max(0, job.BeforeLength);
            }

            return total;
        }

        private void RunOnUi(MethodInvoker action)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed)
                    {
                        action();
                    }
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static Color GetStatusColor(string status, ThemePalette palette)
        {
            if (status == "Done")
            {
                return palette.Done;
            }

            if (status == "Failed" || status == "Timed out")
            {
                return palette.Error;
            }

            if (status == "Processing...")
            {
                return palette.Active;
            }

            if (status == "Cancelled")
            {
                return palette.Cancelled;
            }

            return palette.SurfaceAlt;
        }

        private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2f;

            if (diameter <= 0f)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static StringFormat CreateTextFormat(StringAlignment alignment)
        {
            return new StringFormat
            {
                Alignment = alignment,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
        }

        private sealed class ThemedProgressBar : Control
        {
            private ThemePalette _palette = ThemePalette.FromName("Light");
            private int _minimum;
            private int _maximum = 100;
            private int _value;

            public int Minimum
            {
                get { return _minimum; }
                set
                {
                    _minimum = Math.Max(0, value);
                    if (_maximum < _minimum)
                    {
                        _maximum = _minimum;
                    }
                    Value = _value;
                }
            }

            public int Maximum
            {
                get { return _maximum; }
                set
                {
                    _maximum = Math.Max(_minimum, value);
                    Value = _value;
                }
            }

            public int Value
            {
                get { return _value; }
                set
                {
                    _value = Math.Min(Math.Max(value, _minimum), _maximum);
                    Invalidate();
                }
            }

            public ThemedProgressBar()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            }

            public void SetPalette(ThemePalette palette)
            {
                _palette = palette;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var bounds = new RectangleF(0, 0, Width - 1, Height - 1);
                using (var track = new SolidBrush(_palette.Input))
                using (var border = new Pen(_palette.Border))
                using (var trackPath = CreateRoundedRectangle(bounds, Height / 2f))
                {
                    e.Graphics.FillPath(track, trackPath);
                    e.Graphics.DrawPath(border, trackPath);
                }

                var range = Math.Max(1, _maximum - _minimum);
                var ratio = (_value - _minimum) / (float)range;
                if (ratio <= 0f)
                {
                    return;
                }

                var fillWidth = Math.Max(Height, (Width - 1) * ratio);
                var fillBounds = new RectangleF(0, 0, fillWidth, Height - 1);
                using (var fill = new SolidBrush(_palette.Accent))
                using (var fillPath = CreateRoundedRectangle(fillBounds, Height / 2f))
                {
                    e.Graphics.FillPath(fill, fillPath);
                }
            }
        }

        private sealed class FileQueueView : Control
        {
            private readonly List<FileJob> _jobs = new List<FileJob>();
            private ThemePalette _palette = ThemePalette.FromName("Light");
            private int _scrollOffset;
            private const int PaddingSize = 8;
            private const int RowHeight = 58;
            private const int RowGap = 8;

            public FileQueueView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                TabStop = false;
            }

            public void SetJobs(IEnumerable<FileJob> jobs)
            {
                _jobs.Clear();
                _jobs.AddRange(jobs);
                _scrollOffset = 0;
                Invalidate();
            }

            public void SetPalette(ThemePalette palette)
            {
                _palette = palette;
                BackColor = palette.Window;
                Invalidate();
            }

            public void EnsureVisible(int index)
            {
                if (index < 0 || index >= _jobs.Count)
                {
                    return;
                }

                var rowTop = PaddingSize + index * (RowHeight + RowGap);
                var rowBottom = rowTop + RowHeight;
                var viewHeight = Math.Max(1, Height - PaddingSize * 2);

                if (rowTop - _scrollOffset < PaddingSize)
                {
                    _scrollOffset = Math.Max(0, rowTop - PaddingSize);
                }
                else if (rowBottom - _scrollOffset > viewHeight)
                {
                    _scrollOffset = Math.Max(0, rowBottom - viewHeight);
                }

                ClampScroll();
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _scrollOffset -= Math.Sign(e.Delta) * 42;
                ClampScroll();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var shell = new RectangleF(0, 0, Width - 1, Height - 1);
                using (var surface = new SolidBrush(_palette.Surface))
                using (var border = new Pen(_palette.Border))
                using (var path = CreateRoundedRectangle(shell, 8f))
                {
                    e.Graphics.FillPath(surface, path);
                    e.Graphics.DrawPath(border, path);
                }

                var content = new Rectangle(PaddingSize, PaddingSize, Math.Max(1, Width - PaddingSize * 2), Math.Max(1, Height - PaddingSize * 2));
                e.Graphics.SetClip(content);

                if (_jobs.Count == 0)
                {
                    DrawCenteredText(e.Graphics, "No files", content, _palette.Muted);
                    e.Graphics.ResetClip();
                    return;
                }

                using (var nameFont = new Font(Font.FontFamily, 9f, FontStyle.Bold))
                using (var smallFont = new Font(Font.FontFamily, 8.2f, FontStyle.Regular))
                {
                    for (var i = 0; i < _jobs.Count; i++)
                    {
                        var y = PaddingSize + i * (RowHeight + RowGap) - _scrollOffset;
                        var row = new RectangleF(PaddingSize, y, Width - PaddingSize * 2 - 1, RowHeight);

                        if (row.Bottom < PaddingSize || row.Top > Height - PaddingSize)
                        {
                            continue;
                        }

                        DrawJobRow(e.Graphics, row, _jobs[i], nameFont, smallFont);
                    }
                }

                e.Graphics.ResetClip();
                DrawScrollBar(e.Graphics);
            }

            private void DrawJobRow(Graphics graphics, RectangleF row, FileJob job, Font nameFont, Font smallFont)
            {
                var rowColor = GetStatusColor(job.Status, _palette);
                using (var background = new SolidBrush(rowColor))
                using (var border = new Pen(_palette.RowBorder))
                using (var path = CreateRoundedRectangle(row, 7f))
                {
                    graphics.FillPath(background, path);
                    graphics.DrawPath(border, path);
                }

                var statusText = job.Status == "Processing..." ? "Working" : job.Status;
                var statusWidth = Math.Max(58, Math.Min(88, (int)graphics.MeasureString(statusText, smallFont).Width + 20));
                var statusRect = new RectangleF(row.Right - statusWidth - 10, row.Top + 9, statusWidth, 20);
                var statusColor = job.Status == "Done" ? _palette.SuccessAccent :
                    (job.Status == "Failed" || job.Status == "Timed out" ? _palette.ErrorAccent :
                    (job.Status == "Processing..." ? _palette.Accent : _palette.Muted));

                using (var statusBrush = new SolidBrush(statusColor))
                using (var statusTextBrush = new SolidBrush(Color.White))
                using (var statusPath = CreateRoundedRectangle(statusRect, 10f))
                using (var center = CreateTextFormat(StringAlignment.Center))
                {
                    graphics.FillPath(statusBrush, statusPath);
                    graphics.DrawString(statusText, smallFont, statusTextBrush, statusRect, center);
                }

                var nameRect = new RectangleF(row.Left + 12, row.Top + 8, Math.Max(40, row.Width - statusWidth - 30), 22);
                using (var text = new SolidBrush(_palette.Text))
                using (var muted = new SolidBrush(_palette.Muted))
                using (var left = CreateTextFormat(StringAlignment.Near))
                {
                    graphics.DrawString(job.Name, nameFont, text, nameRect, left);

                    var after = job.AfterLength > 0 ? FormatBytes(job.AfterLength) : "--";
                    var saved = job.AfterLength > 0 ? FormatBytes(job.SavedBytes) : "--";
                    var metrics = "Before " + FormatBytes(job.BeforeLength) + "   After " + after + "   Saved " + saved;
                    var metricsRect = new RectangleF(row.Left + 12, row.Top + 32, row.Width - 24, 18);
                    graphics.DrawString(metrics, smallFont, muted, metricsRect, left);
                }
            }

            private void DrawScrollBar(Graphics graphics)
            {
                var contentHeight = GetContentHeight();
                if (contentHeight <= Height)
                {
                    return;
                }

                var track = new RectangleF(Width - 5, PaddingSize + 4, 3, Height - PaddingSize * 2 - 8);
                var thumbHeight = Math.Max(24f, track.Height * Height / contentHeight);
                var maxOffset = Math.Max(1, contentHeight - Height);
                var thumbTop = track.Top + (track.Height - thumbHeight) * _scrollOffset / maxOffset;
                var thumb = new RectangleF(track.Left, thumbTop, track.Width, thumbHeight);

                using (var brush = new SolidBrush(_palette.ScrollThumb))
                using (var path = CreateRoundedRectangle(thumb, 2f))
                {
                    graphics.FillPath(brush, path);
                }
            }

            private void DrawCenteredText(Graphics graphics, string text, Rectangle bounds, Color color)
            {
                using (var brush = new SolidBrush(color))
                using (var format = CreateTextFormat(StringAlignment.Center))
                {
                    graphics.DrawString(text, Font, brush, bounds, format);
                }
            }

            private int GetContentHeight()
            {
                return PaddingSize * 2 + _jobs.Count * RowHeight + Math.Max(0, _jobs.Count - 1) * RowGap;
            }

            private void ClampScroll()
            {
                var maxOffset = Math.Max(0, GetContentHeight() - Height);
                _scrollOffset = Math.Min(Math.Max(0, _scrollOffset), maxOffset);
            }
        }

        private sealed class ThemePalette
        {
            public string Name;
            public Color Window;
            public Color Surface;
            public Color SurfaceAlt;
            public Color Input;
            public Color Text;
            public Color Muted;
            public Color Border;
            public Color RowBorder;
            public Color Accent;
            public Color SuccessAccent;
            public Color ErrorAccent;
            public Color ScrollThumb;
            public Color Active;
            public Color Done;
            public Color Error;
            public Color Cancelled;

            public static ThemePalette FromName(string name)
            {
                if (string.Equals(name, "Dark", StringComparison.OrdinalIgnoreCase))
                {
                    return new ThemePalette
                    {
                        Name = "Dark",
                        Window = Color.FromArgb(28, 29, 32),
                        Surface = Color.FromArgb(36, 38, 42),
                        SurfaceAlt = Color.FromArgb(40, 42, 47),
                        Input = Color.FromArgb(43, 46, 53),
                        Text = Color.FromArgb(242, 243, 245),
                        Muted = Color.FromArgb(169, 173, 181),
                        Border = Color.FromArgb(58, 62, 70),
                        RowBorder = Color.FromArgb(54, 58, 66),
                        Accent = Color.FromArgb(83, 99, 218),
                        SuccessAccent = Color.FromArgb(57, 148, 92),
                        ErrorAccent = Color.FromArgb(224, 82, 96),
                        ScrollThumb = Color.FromArgb(84, 87, 96),
                        Active = Color.FromArgb(37, 50, 71),
                        Done = Color.FromArgb(32, 56, 43),
                        Error = Color.FromArgb(60, 37, 41),
                        Cancelled = Color.FromArgb(48, 50, 54)
                    };
                }

                return new ThemePalette
                {
                    Name = "Light",
                    Window = Color.FromArgb(246, 247, 249),
                    Surface = Color.FromArgb(255, 255, 255),
                    SurfaceAlt = Color.FromArgb(249, 250, 252),
                    Input = Color.FromArgb(240, 243, 248),
                    Text = Color.FromArgb(32, 33, 36),
                    Muted = Color.FromArgb(100, 110, 125),
                    Border = Color.FromArgb(205, 214, 226),
                    RowBorder = Color.FromArgb(226, 231, 238),
                    Accent = Color.FromArgb(61, 86, 214),
                    SuccessAccent = Color.FromArgb(45, 145, 88),
                    ErrorAccent = Color.FromArgb(218, 71, 88),
                    ScrollThumb = Color.FromArgb(190, 198, 211),
                    Active = Color.FromArgb(238, 244, 255),
                    Done = Color.FromArgb(234, 248, 240),
                    Error = Color.FromArgb(255, 241, 242),
                    Cancelled = Color.FromArgb(242, 243, 245)
                };
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
            catch
            {
            }
        }

        private static string BuildArguments(string ext, string speed, bool noDither, string file)
        {
            var args = "--force --ext " + Quote(ext) + " --speed " + Quote(speed);
            if (noDither)
            {
                args += " --nofs";
            }

            return args + " " + Quote(file);
        }

        private static void AppendLog(string file, string arguments, int exitCode, bool timedOut, string output, long beforeLength, long afterLength)
        {
            try
            {
                File.AppendAllText(
                    LogPath,
                    "File: " + file + Environment.NewLine +
                    "Arguments: " + arguments + Environment.NewLine +
                    "ExitCode: " + exitCode + Environment.NewLine +
                    "TimedOut: " + timedOut + Environment.NewLine +
                    "Output: " + output + Environment.NewLine +
                    "BeforeLength: " + beforeLength + Environment.NewLine +
                    "AfterLength: " + afterLength + Environment.NewLine +
                    Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return "";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            var unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? bytes + " B" : value.ToString("0.##") + " " + units[unit];
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class FileJob
        {
            public readonly string Path;
            public readonly string Name;
            public readonly long BeforeLength;
            public long AfterLength = -1;
            public long SavedBytes;
            public string Status = "Queued";
            public bool Success;

            public FileJob(string path)
            {
                Path = path;
                Name = System.IO.Path.GetFileName(path);
                BeforeLength = File.Exists(path) ? new FileInfo(path).Length : -1;
            }
        }

        private sealed class CompressionResult
        {
            public string Arguments;
            public string OutputPath;
            public int ExitCode;
            public bool TimedOut;
            public bool Cancelled;
            public bool Success;
            public long AfterLength;
        }
    }


}
