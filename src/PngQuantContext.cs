using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
        private readonly ComboBox _preset;
        private readonly RadioButton _copyMode;
        private readonly RadioButton _replaceMode;
        private readonly CheckBox _noDither;
        private readonly Label _summary;
        private readonly Label _current;
        private readonly Label _details;
        private readonly ProgressBar _overallProgress;
        private readonly ListView _fileList;
        private readonly Button _startButton;
        private readonly Button _cancelButton;
        private readonly Button _closeButton;
        private readonly object _processLock = new object();
        private readonly bool _autoRun;
        private volatile bool _cancelRequested;
        private Process _currentProcess;
        private bool _running;
        private bool _runReplace;
        private bool _runNoDither;
        private string _runSpeed = "3";

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
            MinimumSize = new Size(760, 500);
            ClientSize = new Size(820, 540);
            Font = new Font("Segoe UI", 9);
            BackColor = Color.FromArgb(246, 248, 250);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = Color.FromArgb(31, 41, 55)
            };

            var title = new Label
            {
                AutoSize = false,
                Location = new Point(20, 15),
                Size = new Size(500, 26),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White,
                Text = "Compress PNG files"
            };

            _summary = new Label
            {
                AutoSize = false,
                Location = new Point(20, 43),
                Size = new Size(760, 22),
                ForeColor = Color.FromArgb(209, 213, 219),
                Text = BuildReadySummary()
            };

            header.Controls.Add(title);
            header.Controls.Add(_summary);

            var optionsPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 86,
                BackColor = Color.White
            };

            var modeLabel = new Label
            {
                AutoSize = false,
                Location = new Point(20, 17),
                Size = new Size(72, 22),
                Text = "Mode"
            };

            _copyMode = new RadioButton
            {
                Location = new Point(96, 15),
                Size = new Size(75, 24),
                Text = "Copy",
                Checked = !options.Replace
            };

            _replaceMode = new RadioButton
            {
                Location = new Point(174, 15),
                Size = new Size(90, 24),
                Text = "Replace",
                Checked = options.Replace
            };

            var presetLabel = new Label
            {
                AutoSize = false,
                Location = new Point(20, 49),
                Size = new Size(72, 22),
                Text = "Preset"
            };

            _preset = new ComboBox
            {
                Location = new Point(96, 46),
                Size = new Size(220, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _preset.Items.Add("Balanced (speed 3)");
            _preset.Items.Add("Best quality (speed 1)");
            _preset.Items.Add("Fast (speed 8)");
            _preset.SelectedIndex = options.Preset == "quality" ? 1 : (options.Preset == "fast" ? 2 : 0);

            _noDither = new CheckBox
            {
                Location = new Point(340, 47),
                Size = new Size(150, 24),
                Text = "Disable dithering",
                Checked = options.NoDither
            };

            _startButton = new Button
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(514, 24),
                Size = new Size(92, 30),
                Text = "Compress"
            };
            _startButton.Click += delegate { StartCompression(); };

            _cancelButton = new Button
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(612, 24),
                Size = new Size(82, 30),
                Text = "Cancel",
                Enabled = false
            };
            _cancelButton.Click += delegate { CancelCompression(); };

            _closeButton = new Button
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(700, 24),
                Size = new Size(82, 30),
                Text = "Close"
            };
            _closeButton.Click += delegate { Close(); };

            optionsPanel.Controls.Add(modeLabel);
            optionsPanel.Controls.Add(_copyMode);
            optionsPanel.Controls.Add(_replaceMode);
            optionsPanel.Controls.Add(presetLabel);
            optionsPanel.Controls.Add(_preset);
            optionsPanel.Controls.Add(_noDither);
            optionsPanel.Controls.Add(_startButton);
            optionsPanel.Controls.Add(_cancelButton);
            optionsPanel.Controls.Add(_closeButton);

            _fileList = new ListView
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(18, 180),
                Size = new Size(784, 275),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HideSelection = false,
                ShowItemToolTips = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            _fileList.Columns.Add("File", 320);
            _fileList.Columns.Add("Before", 95, HorizontalAlignment.Right);
            _fileList.Columns.Add("After", 95, HorizontalAlignment.Right);
            _fileList.Columns.Add("Saved", 95, HorizontalAlignment.Right);
            _fileList.Columns.Add("Status", 170);

            foreach (var job in _jobs)
            {
                _fileList.Items.Add(CreateListItem(job));
            }

            _current = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Location = new Point(18, 464),
                Size = new Size(784, 22),
                Text = "Ready."
            };

            _overallProgress = new ProgressBar
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(18, 490),
                Size = new Size(784, 18),
                Minimum = 0,
                Maximum = Math.Max(_jobs.Count, 1),
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

            _details = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                Location = new Point(18, 514),
                Size = new Size(784, 20),
                ForeColor = Color.FromArgb(95, 105, 117),
                Text = "pngquant: " + _pngquant
            };

            Controls.Add(_details);
            Controls.Add(_overallProgress);
            Controls.Add(_current);
            Controls.Add(_fileList);
            Controls.Add(optionsPanel);
            Controls.Add(header);

            Resize += delegate { ResizeColumns(); };
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
                ResizeColumns();
                if (_autoRun)
                {
                    StartCompression();
                }
            };
        }

        private void StartCompression()
        {
            if (_running)
            {
                return;
            }

            _runReplace = _replaceMode.Checked;
            _runNoDither = _noDither.Checked;
            _runSpeed = _preset.SelectedIndex == 1 ? "1" : (_preset.SelectedIndex == 2 ? "8" : "3");
            _cancelRequested = false;
            _running = true;

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
            _copyMode.Enabled = !running;
            _replaceMode.Enabled = !running;
            _preset.Enabled = !running;
            _noDither.Enabled = !running;
            _startButton.Enabled = !running;
            _cancelButton.Enabled = running;
            _closeButton.Enabled = !running;
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

                var item = _fileList.Items[index];
                item.SubItems[2].Text = FormatBytes(job.AfterLength);
                item.SubItems[3].Text = job.AfterLength > 0 ? FormatBytes(job.SavedBytes) : "";
                item.SubItems[4].Text = job.Status;
                item.BackColor = GetStatusColor(job.Status);
                _fileList.EnsureVisible(index);
                _summary.Text = BuildProgressSummary();
            });
        }

        private void RefreshAllRows()
        {
            for (var i = 0; i < _jobs.Count; i++)
            {
                var job = _jobs[i];
                var item = _fileList.Items[i];
                item.SubItems[2].Text = "";
                item.SubItems[3].Text = "";
                item.SubItems[4].Text = job.Status;
                item.BackColor = Color.White;
            }
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

        private ListViewItem CreateListItem(FileJob job)
        {
            var item = new ListViewItem(job.Name);
            item.SubItems.Add(FormatBytes(job.BeforeLength));
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add(job.Status);
            item.ToolTipText = job.Path;
            return item;
        }

        private void ResizeColumns()
        {
            if (_fileList.Columns.Count == 0)
            {
                return;
            }

            var fixedWidth = 95 + 95 + 95 + 170 + 8;
            _fileList.Columns[0].Width = Math.Max(220, _fileList.ClientSize.Width - fixedWidth);
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

        private static Color GetStatusColor(string status)
        {
            if (status == "Done")
            {
                return Color.FromArgb(236, 253, 245);
            }

            if (status == "Failed" || status == "Timed out")
            {
                return Color.FromArgb(254, 242, 242);
            }

            if (status == "Processing...")
            {
                return Color.FromArgb(239, 246, 255);
            }

            if (status == "Cancelled")
            {
                return Color.FromArgb(243, 244, 246);
            }

            return Color.White;
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
