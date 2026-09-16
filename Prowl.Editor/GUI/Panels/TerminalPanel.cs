using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Scribe;
using Prowl.Vector;

using static Prowl.Editor.GUI.EditorGUI;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Panels;

internal class TerminalPanel : DockPanel
{
    [MenuItem("Window/General/Terminal", priority: 0)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(TerminalPanel));

    public override string Title => Loc.Get("panel.terminal");
    public override string Icon => EditorIcons.Terminal;

    private static readonly ShellInfo[] s_availableShells = FindShells();
    private static readonly List<Shell> s_activeShells = [];
    private int _selectedShellIndex = 0;

    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;
        
        using (paper.Column("term_root").Size(width, height).Enter())
        {
            using (paper.Row("term_tabs_bar").Height(UnitValue.Auto).Enter())
            {
                // TODO: Stop the tabs from growing after a point, or just add scrolling.
                TabsBuilder tabs = Origami.Tabs(paper, "term_tabs", _selectedShellIndex, OnTabSelect)
                    .Height(EditorTheme.TabBarHeight)
                    .Closeable(OnCloseTab);

                for (int i = 0; i < s_activeShells.Count; i++)
                {
                    Shell shell = s_activeShells[i];
                    tabs.Tab(shell.Title);
                }
                tabs.Show();
                Origami.Button(paper, "term_tabs_new", EditorIcons.Plus, OnNewTab)
                    .Width(24)
                    .Height(EditorTheme.TabBarHeight)
                    .Ghost()
                    .NotFocusable()
                    .Show();
                Origami.Dropdown<ShellInfo>(paper, "term_tabs_new_shell_select", ShellInfo.Null, OnNewTabWithShellIndex, s_availableShells)
                    .NoChevron()
                    .Placeholder(EditorIcons.AngleDown)
                    .Width(24)
                    .Height(EditorTheme.TabBarHeight)
                    .Variant(OrigamiVariant.Subtle)
                    .Show();
                //Origami.ButtonGroup(paper, "term_tabs_new", -1, OnNewTab)
                //    .Height(EditorTheme.TabBarHeight)
                //    .Segmented()
                //    .Item("", EditorIcons.Plus)
                //    .Item("", EditorIcons.AngleDown)
                //    .Show();
            }
            using(paper.Column("term_body").Size(UnitValue.Stretch()))
            {
                Shell? shell = s_activeShells.ElementAtOrDefault(_selectedShellIndex);
                if (shell == null) return;
                int maxElements = (int)MathF.Round((height - EditorTheme.TabBarHeight) / (EditorTheme.FontSize + 2));
                for(int i = 0; i < maxElements; i++)
                {
                    if(i >= shell.OutputLines.Count)
                        break;
                    string line = shell.OutputLines[shell.OutputLines.Count - 1 - i];
                    paper.Box("term_line_" + i)
                        .Size(UnitValue.Stretch(), UnitValue.Auto)
                        .Padding(2, 0, 2, 0)
                        .Text(line, EditorTheme.Font)
                        .FontSize(EditorTheme.FontSize)
                        .TextColor(EditorTheme.Ink400);
                }
            }
        }
    }
    private void OnNewTab()
    {
        var shell = new Shell(s_availableShells[0]);
        s_activeShells.Add(shell);
        _selectedShellIndex = Math.Max(0, s_activeShells.Count - 1);
        shell.Start();
    }
    private void OnNewTabWithShellIndex(ShellInfo info)
    {
        var shell = new Shell(info);
        s_activeShells.Add(shell);
        _selectedShellIndex = Math.Max(0, s_activeShells.Count - 1);
        shell.Start();
    }
    private void OnTabSelect(int index)
    {
        Shell? shell = s_activeShells.ElementAtOrDefault(index);
        if (shell == null) return;
        _selectedShellIndex = index;
    }
    private void OnCloseTab(int index)
    {
        Shell? shell = s_activeShells.ElementAtOrDefault(index);
        if (shell == null) return;
        s_activeShells.RemoveAt(index);
        if (index <= _selectedShellIndex && index > 0)
            _selectedShellIndex = index - 1;
        shell.Dispose();
    }

    private static ShellInfo[] FindShells()
    {
        List<ShellInfo> shells = [];

        if(OperatingSystem.IsWindows())
            FindShellsWindows(shells);
        else if (OperatingSystem.IsLinux())
            FindShellsLinux(shells);
        else if (OperatingSystem.IsMacOS())
            FindShellsMacOS(shells);

        return [.. shells];

        static void FindShellsWindows(List<ShellInfo> shells)
        {
            shells.Add(new ShellInfo("Command Prompt", Environment.GetFolderPath(Environment.SpecialFolder.System) + "\\cmd.exe"));
            if (ExecutableExists(Environment.GetFolderPath(Environment.SpecialFolder.System) + "\\WindowsPowerShell\\v1.0\\powershell.exe", out string powershellPath))
                shells.Add(new ShellInfo("Windows PowerShell", powershellPath));
            if(ExecutableExists("pwsh.exe", out string pwshPath))
                shells.Add(new ShellInfo("PowerShell", pwshPath));
        }
        static void FindShellsLinux(List<ShellInfo> shells)
        {
            if (ExecutableExists("bash", out string bashPath))
                shells.Add(new ShellInfo("Bash", bashPath));
            if (ExecutableExists("zsh", out string zshPath))
                shells.Add(new ShellInfo("Zsh", zshPath));
            if (ExecutableExists("fish", out string fishPath))
                shells.Add(new ShellInfo("Fish", fishPath));
            if (ExecutableExists("sh", out string shPath))
                shells.Add(new ShellInfo("Sh", shPath));
        }
        static void FindShellsMacOS(List<ShellInfo> shells)
        {
            throw new NotImplementedException();
        }

        static bool ExecutableExists(string executable, out string fullPath)
        {
            if(Path.IsPathFullyQualified(executable) && File.Exists(executable))
            {
                fullPath = executable;
                return true;
            }
            string[] path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
            if(path.Any(p => File.Exists(Path.Combine(p, executable))))
            {
                fullPath = Path.Combine(path.First(p => File.Exists(Path.Combine(p, executable))), executable);
                return true;
            }
            fullPath = string.Empty;
            return false;
        }
    }

    public record ShellInfo(string Name, string Executable)
    {
        public static readonly ShellInfo Null = default;
        public override string ToString() => Name;
    }
    public class Shell : IDisposable
    {
        private bool _isDisposed;
        public readonly ShellInfo Info;
        public string Title { get; private set; }
        public bool IsRunning { get; private set; }

        private readonly Process _shellProcess = new();
        public const int MaxOutputLines = 1000;
        private readonly List<string> _outputLines = [];
        public IReadOnlyList<string> OutputLines => _outputLines;

        public Shell(ShellInfo info)
        {
            Info = info;
            Title = info.Name;
            _shellProcess.EnableRaisingEvents = true;
            _shellProcess.StartInfo = new ProcessStartInfo()
            {
                FileName = Info.Executable,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                //StandardInputEncoding = System.Text.Encoding.UTF8,
                //StandardErrorEncoding = System.Text.Encoding.UTF8,
                //StandardOutputEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Project.Current.RootPath,
            };
            _shellProcess.OutputDataReceived += OutputDataReceived;
            _shellProcess.ErrorDataReceived += ErrorDataReceived;
        }
        public void Start()
        {
            if (IsRunning)
                return;
            IsRunning = _shellProcess.Start();
        }
        private void OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            _outputLines.AddRange(e.Data.Split(Environment.NewLine));
            int toRemove = _outputLines.Count - MaxOutputLines;
            if (toRemove > 0)
                _outputLines.RemoveRange(0, toRemove);
        }
        private void ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            _outputLines.AddRange(e.Data.Split(Environment.NewLine));
            int toRemove = _outputLines.Count - MaxOutputLines;
            if (toRemove > 0)
                _outputLines.RemoveRange(0, toRemove);
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public override string ToString() => Title;

        public void Dispose()
        {
            if (_isDisposed)
                return;
            GC.SuppressFinalize(this);
            if(IsRunning)
                Stop();
            _isDisposed = true;
        }
    }
}
