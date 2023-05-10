using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace VRCSaveHelper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const uint CLIPBRD_E_CANT_OPEN = 0x800401D0;

        private static Regex _nameRegex = new Regex(@"^output_log_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.txt$");
        private static Regex _logRegex = new Regex(@"^(\d{4})\.(\d{2})\.(\d{2}) (\d{2}):(\d{2}):(\d{2}) Log\s*-  (.*)$");
        private static Regex _enteringRegex = new Regex(@"^\[Behaviour\] Entering Room: (.*)$");
        private static Regex _joiningRegex = new Regex(@"^\[Behaviour\] Joining (wrld_[^:]*):(.*)$");

        private readonly string _folder;
        private readonly DispatcherTimer _timer;

        private string _currentFile = "";
        private FileStream? _currentStream = null;
        private StreamReader? _currentReader = null;

        private FileStream? _historyStream = null;

        private string _lastWorldName = "";
        private string _lastWorldID = "";

        private Database _database = new Database();

        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = _database.LoadData();
            WorldsComboBox.DataContext = _viewModel;
            HistoryListView.DataContext = _viewModel;
            AutoLoadCheckBox.DataContext = _viewModel;
            AutoSaveCheckBox.DataContext = _viewModel;

            _folder = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"..\LocalLow\VRChat\VRChat");

            _timer = new DispatcherTimer();
            _timer.Interval = new TimeSpan(0, 0, 1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private extern static void AddClipboardFormatListener(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private extern static void RemoveClipboardFormatListener(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private extern static IntPtr GetClipboardOwner();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private extern static IntPtr FindWindow(string lpClassName, string lpWindowName);

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source.AddHook(new HwndSourceHook(WndProc));

            AddClipboardFormatListener(hwnd);
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_currentReader != null) { _currentReader.Close(); }
            if (_currentStream != null) { _currentStream.Close(); }
            if (_historyStream != null) { _historyStream.Close(); }

            var hwnd = new WindowInteropHelper(this).Handle;
            RemoveClipboardFormatListener(hwnd);

            _database.Close();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_CLIPBOARDUPDATE = 0x31D;
            if (msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardUpdate();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnClipboardUpdate()
        {
            string data = "";
            for (int i = 0; i < 32; ++i)
            {
                try
                {
                    data = Clipboard.GetText();
                    break;
                }
                catch (COMException e)
                {
                    if ((uint)e.ErrorCode != CLIPBRD_E_CANT_OPEN) throw;
                }
                Thread.Sleep(5);
            }
            if (string.IsNullOrEmpty(data)) { return; }

            // Some apps (incl. VRChat) set NULL for clipboard owner.
            var ownerHwnd = GetClipboardOwner();
            if (ownerHwnd != IntPtr.Zero) { return; }

            // Safety: VRChat window must exist
            var vrchatHwnd = FindWindow("UnityWndClass", "VRChat");
            if (vrchatHwnd == IntPtr.Zero) { return; }

            var history = new HistoryViewModel(-1, _lastWorldID, DateTime.Now, data);
            var world = _viewModel.FindWorldById(_lastWorldID);
            if (world != null && world.AutoSave)
            {
                world.History.Add(history);
                new ToastContentBuilder()
                    .AddText(world.Name)
                    .AddText("Saved: " + data)
                    .Show();
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            bool fileChanged = false;
            foreach(var path in Directory.GetFiles(_folder, "output_log_*.txt"))
            {
                var name = Path.GetFileName(path);
                if (_nameRegex.IsMatch(name) && string.Compare(name, _currentFile) > 0)
                {
                    fileChanged = true;
                    _currentFile = name;
                }
            }

            if (fileChanged)
            {
                var path = Path.Combine(_folder, _currentFile);
                Debug.WriteLine(path);
                if (_currentReader != null) { _currentReader.Close(); }
                if (_currentStream != null) { _currentStream.Close(); }
                _currentStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _currentReader = new StreamReader(_currentStream);
            }

            if (_currentReader != null)
            {
                string? line = null;
                while ((line = _currentReader.ReadLine()) != null)
                {
                    ParseLogLine(line);
                }
            }
        }

        private void ParseLogLine(string line)
        {
            line = line.Trim();
            var match = _logRegex.Match(line);
            if (!match.Success) { return; }
            var timestamp = new DateTime(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value),
                int.Parse(match.Groups[4].Value),
                int.Parse(match.Groups[5].Value),
                int.Parse(match.Groups[6].Value));
            var body = match.Groups[7].Value;

            // ignore if the message is too old (15sec?)
            if (DateTime.Now - timestamp > TimeSpan.FromSeconds(15))
            {
                return;
            }

            match = _enteringRegex.Match(body);
            if (match.Success)
            {
                _lastWorldName = match.Groups[1].Value;
            }

            match = _joiningRegex.Match(body);
            if (match.Success)
            {
                _lastWorldID = match.Groups[1].Value;
                if (!string.IsNullOrEmpty(_lastWorldName))
                {
                    OnEnterWorld(_lastWorldID, _lastWorldName);
                }
            }
        }

        private void OnEnterWorld(string id, string name)
        {
            var world = _viewModel.FindWorldById(id);
            if (world != null)
            {
                world.Name = name;
            }
            else
            {
                world = new WorldViewModel(_database, id, name, true, true, new HistoryViewModel[0]);
                _viewModel.Worlds.Add(world);
            }
            var history = world.History;
            if (history.Count > 0 && world.AutoLoad)
            {
                var data = history[history.Count - 1].Data;
                for (int i = 0; i < 32; ++i)
                {
                    try
                    {
                        Clipboard.SetText(data);
                        break;
                    }
                    catch (COMException e)
                    {
                        if ((uint)e.ErrorCode != CLIPBRD_E_CANT_OPEN) throw;
                    }
                    Thread.Sleep(5);
                }
                new ToastContentBuilder()
                    .AddText(world.Name)
                    .AddText("Copied: " + data)
                    .Show();
            }
            _viewModel.SelectedWorld = world;
        }
    }
}
