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
        private readonly List<string> _files;
        private readonly ComboBox _preset;
        private readonly RadioButton _copyMode;
        private readonly RadioButton _replaceMode;
        private readonly CheckBox _noDither;
        private readonly Label _status;
        private readonly Label _details;
        private readonly ProgressBar _progress;
        private readonly Button _startButton;
        private readonly Button _closeButton;
        private bool _runReplace;
        private bool _runNoDither;
        private string _runSpeed = "3";

        private readonly bool _autoRun;

        public MainForm(string pngquant, List<string> files, RunOptions options)
        {
            _pngquant = pngquant;
            _files = files;
            _autoRun = options.AutoRun;

            Text = "Compress PNG";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 260);
            Font = new Font("Segoe UI", 9);

            var title = new Label
            {
                AutoSize = false,
                Location = new Point(18, 14),
                Size = new Size(424, 24),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Text = "PNG files: " + files.Count
            };

            var modeLabel = new Label
            {
                AutoSize = false,
                Location = new Point(18, 48),
                Size = new Size(80, 20),
                Text = "Mode"
            };

            _copyMode = new RadioButton
            {
                Location = new Point(104, 46),
                Size = new Size(96, 22),
                Text = "Copy",
                Checked = !options.Replace
            };

            _replaceMode = new RadioButton
            {
                Location = new Point(206, 46),
                Size = new Size(96, 22),
                Text = "Replace",
                Checked = options.Replace
            };

            var presetLabel = new Label
            {
                AutoSize = false,
                Location = new Point(18, 82),
                Size = new Size(80, 20),
                Text = "Preset"
            };

            _preset = new ComboBox
            {
                Location = new Point(104, 78),
                Size = new Size(242, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _preset.Items.Add("Balanced (speed 3)");
            _preset.Items.Add("Best quality (speed 1)");
            _preset.Items.Add("Fast (speed 8)");
            _preset.SelectedIndex = options.Preset == "quality" ? 1 : (options.Preset == "fast" ? 2 : 0);

            _noDither = new CheckBox
            {
                Location = new Point(104, 110),
                Size = new Size(230, 22),
                Text = "Disable dithering",
                Checked = options.NoDither
            };

            _status = new Label
            {
                AutoSize = false,
                Location = new Point(18, 145),
                Size = new Size(424, 34),
                Text = "Ready to compress."
            };

            _progress = new ProgressBar
            {
                Location = new Point(18, 184),
                Size = new Size(424, 18),
                Minimum = 0,
                Maximum = Math.Max(files.Count, 1),
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

            _details = new Label
            {
                AutoSize = false,
                Location = new Point(18, 212),
                Size = new Size(260, 22),
                ForeColor = Color.FromArgb(85, 85, 85),
                Text = "pngquant: " + Path.GetFileName(_pngquant)
            };

            _startButton = new Button
            {
                Location = new Point(244, 224),
                Size = new Size(96, 28),
                Text = "Compress"
            };
            _startButton.Click += delegate { StartCompression(); };

            _closeButton = new Button
            {
                Location = new Point(346, 224),
                Size = new Size(96, 28),
                Text = "Close"
            };
            _closeButton.Click += delegate { Close(); };

            Controls.Add(title);
            Controls.Add(modeLabel);
            Controls.Add(_copyMode);
            Controls.Add(_replaceMode);
            Controls.Add(presetLabel);
            Controls.Add(_preset);
            Controls.Add(_noDither);
            Controls.Add(_status);
            Controls.Add(_progress);
            Controls.Add(_details);
            Controls.Add(_startButton);
            Controls.Add(_closeButton);

            if (_autoRun)
            {
                Shown += delegate { StartCompression(); };
            }
        }

        private void StartCompression()
        {
            _runReplace = _replaceMode.Checked;
            _runNoDither = _noDither.Checked;
            _runSpeed = _preset.SelectedIndex == 1 ? "1" : (_preset.SelectedIndex == 2 ? "8" : "3");

            _startButton.Enabled = false;
            _closeButton.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate { CompressFiles(); });
        }

        private void CompressFiles()
        {
            var done = 0;
            var failed = 0;
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

            for (var i = 0; i < _files.Count; i++)
            {
                var file = _files[i];
                var ext = replace ? ".png" : "-compressed.png";
                var expectedOutput = replace
                    ? file
                    : Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + "-compressed.png");
                var beforeLength = File.Exists(file) ? new FileInfo(file).Length : -1;
                var arguments = BuildArguments(ext, speed, noDither, file);
                var exitCode = -1;
                var timedOut = false;

                SetProgress(i, "Compressing: " + Path.GetFileName(file), (i + 1) + " of " + _files.Count, true);

                using (var process = new Process())
                {
                    process.StartInfo.FileName = _pngquant;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WorkingDirectory = Path.GetDirectoryName(file);
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.Start();

                    if (!process.WaitForExit(120000))
                    {
                        timedOut = true;
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                        exitCode = process.ExitCode;
                    }
                }

                var afterLength = File.Exists(expectedOutput) ? new FileInfo(expectedOutput).Length : -1;
                var success = exitCode == 0 && afterLength > 0 && (!replace || afterLength != beforeLength);
                AppendLog(file, arguments, exitCode, timedOut, expectedOutput, beforeLength, afterLength);

                if (success)
                {
                    done++;
                }
                else
                {
                    failed++;
                }

                SetProgress(i + 1, null, null, false);
            }

            Finish(done, failed, replace);
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

        private void SetProgress(int value, string status, string details, bool marquee)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                if (marquee)
                {
                    _progress.Style = ProgressBarStyle.Marquee;
                    _progress.MarqueeAnimationSpeed = 24;
                }
                else
                {
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.MarqueeAnimationSpeed = 0;
                    _progress.Value = Math.Min(Math.Max(value, _progress.Minimum), _progress.Maximum);
                }

                if (status != null)
                {
                    _status.Text = status;
                }

                if (details != null)
                {
                    _details.Text = details;
                }
            });
        }

        private void Finish(int done, int failed, bool replace)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                _status.Text = failed == 0
                    ? "Done: compressed " + done + " PNG file(s)."
                    : "Done: compressed " + done + ", failed " + failed + ".";
                _details.Text = failed == 0
                    ? (replace ? "Source files were replaced." : "Compressed copies were created next to source files.")
                    : "Details: " + LogPath;
                _startButton.Enabled = true;
                _closeButton.Enabled = true;
                _closeButton.Focus();

                if (_autoRun && failed == 0)
                {
                    var timer = new System.Windows.Forms.Timer { Interval = 900 };
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        timer.Dispose();
                        Close();
                    };
                    timer.Start();
                }
            });
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

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
