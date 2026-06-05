using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static class PngQuantContext
{
    private static readonly string AppDirectory = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string QueueDirectory = Path.Combine(Path.GetTempPath(), "PngQuantContext");
    private static readonly string LogPath = Path.Combine(AppDirectory, "PngQuantContext.log");

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

        var pngquant = FindPngQuant();
        if (pngquant == null || files.Count == 0)
        {
            MessageBox.Show(
                "Не найден pngquant.exe или PNG-файл для сжатия.",
                "Сжать PNG",
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

        private readonly RunOptions _options;

        public MainForm(string pngquant, List<string> files, RunOptions options)
        {
            _pngquant = pngquant;
            _files = files;
            _options = options;

            Text = "Сжать PNG";
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
                Text = "PNG: " + files.Count + " файл(ов)"
            };

            var modeLabel = new Label
            {
                AutoSize = false,
                Location = new Point(18, 48),
                Size = new Size(80, 20),
                Text = "Режим"
            };

            _copyMode = new RadioButton
            {
                Location = new Point(104, 46),
                Size = new Size(96, 22),
                Text = "Копия",
                Checked = !options.Replace
            };

            _replaceMode = new RadioButton
            {
                Location = new Point(206, 46),
                Size = new Size(96, 22),
                Text = "Заменить",
                Checked = options.Replace
            };

            var presetLabel = new Label
            {
                AutoSize = false,
                Location = new Point(18, 82),
                Size = new Size(80, 20),
                Text = "Пресет"
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
                Text = "Без dithering",
                Checked = options.NoDither
            };

            _status = new Label
            {
                AutoSize = false,
                Location = new Point(18, 145),
                Size = new Size(424, 34),
                Text = "Готово к сжатию."
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
                Text = "Сжать"
            };
            _startButton.Click += delegate { StartCompression(); };

            _closeButton = new Button
            {
                Location = new Point(346, 224),
                Size = new Size(96, 28),
                Text = "Закрыть"
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

            if (options.AutoRun)
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

                SetProgress(i, "Сжимаю: " + Path.GetFileName(file), (i + 1) + " из " + _files.Count, true);

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
                    ? "Готово: сжато " + done + " PNG."
                    : "Готово: сжато " + done + ", ошибок " + failed + ".";
                _details.Text = failed == 0
                    ? (replace ? "Исходные файлы заменены." : "Сжатые копии созданы рядом с исходниками.")
                    : "Подробности: " + LogPath;
                _startButton.Enabled = true;
                _closeButton.Enabled = true;
                _closeButton.Focus();
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
