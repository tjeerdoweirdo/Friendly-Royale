using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Simple in-game terminal that can host/join via RelayQuickConnect, start a local server, and view live logs.
/// Toggle with BackQuote (`). Persists across scenes.
/// </summary>
public class MatchTerminalPanel : MonoBehaviour
{
    private static MatchTerminalPanel _instance;

    public KeyCode toggleKey = KeyCode.BackQuote; // ` key
    public bool visible = false;
    public Rect windowRect = new Rect(24, 360, 700, 280);

    private Vector2 _logScroll;
    private Vector2 _cmdScroll;
    private readonly List<string> _lines = new List<string>(2048);
    private readonly List<string> _history = new List<string>(64);
    private int _historyIndex = -1;
    private string _input = string.Empty;

    // Optional external server process tracking
    private Process _serverProcess;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceivedThreaded += OnLog;
        Log("Terminal ready. Type 'help' for commands.");
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        Application.logMessageReceivedThreaded -= OnLog;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
        }
    }

    void OnGUI()
    {
        if (!visible) return;
        try
        {
            windowRect = GUI.Window(305197, windowRect, DrawWindow, "Match Terminal");
        }
        catch { }
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        // Logs
        GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
        _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
        lock (_lines)
        {
            foreach (var l in _lines)
            {
                GUILayout.Label(l);
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.Space(8);
        // Command cheatsheet
        GUILayout.BeginVertical(GUILayout.Width(220));
        GUILayout.Label("Commands:");
        GUILayout.Label("- host               (Relay or direct)");
        GUILayout.Label("- join CODE          (Relay join)");
    GUILayout.Label("- server             (Start local server in-process)");
    GUILayout.Label("- server external    (Start external server via start_server.bat)");
        GUILayout.Label("- status             (Net state)");
    GUILayout.Label("- save [name]        (Save logs to file)");
    GUILayout.Label("- clear              (Clear log view)");
        GUILayout.Label("- help               (Show help)");
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        // Command line
        GUILayout.BeginHorizontal();
        GUI.SetNextControlName("cmdInput");
        _input = GUILayout.TextField(_input ?? string.Empty, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Run", GUILayout.Width(80)))
        {
            RunCommand(_input);
        }
        GUILayout.EndHorizontal();

        // History nav
        var e = Event.current;
        if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "cmdInput")
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                RunCommand(_input);
                e.Use();
            }
            else if (e.keyCode == KeyCode.UpArrow)
            {
                if (_history.Count > 0)
                {
                    _historyIndex = Mathf.Clamp(_historyIndex < 0 ? _history.Count - 1 : _historyIndex - 1, 0, _history.Count - 1);
                    _input = _history[_historyIndex];
                }
                e.Use();
            }
            else if (e.keyCode == KeyCode.DownArrow)
            {
                if (_history.Count > 0)
                {
                    _historyIndex = Mathf.Clamp(_historyIndex + 1, -1, _history.Count - 1);
                    _input = _historyIndex >= 0 ? _history[_historyIndex] : string.Empty;
                }
                e.Use();
            }
        }

        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        var sb = new StringBuilder(2048);
        sb.Append('[').Append(type.ToString()).Append("] ").Append(condition);
        lock (_lines)
        {
            _lines.Add(sb.ToString());
            if (_lines.Count > 2000) _lines.RemoveRange(0, _lines.Count - 2000);
        }
    }

    private void Log(string msg)
    {
        lock (_lines)
        {
            _lines.Add(msg);
            if (_lines.Count > 2000) _lines.RemoveRange(0, _lines.Count - 2000);
        }
    }

    private async void RunCommand(string cmdLine)
    {
        if (string.IsNullOrWhiteSpace(cmdLine)) return;
        _history.Add(cmdLine);
        _historyIndex = -1;
        _input = string.Empty;

        var parts = cmdLine.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        try
        {
            switch (cmd)
            {
                case "help":
                    Log("Commands: host | join CODE | server | status | clear | help");
                    break;
                case "clear":
                    lock (_lines) _lines.Clear();
                    break;
                case "status":
                {
                    var nm = NetworkManager.Singleton;
                    if (nm == null)
                    {
                        Log("[status] NetworkManager missing");
                        break;
                    }
                    Log($"[status] Listening={nm.IsListening} Role={(nm.IsHost?"Host":(nm.IsServer?"Server":(nm.IsClient?"Client":"None")))} LocalId={nm.LocalClientId}");
                    if (nm.IsListening)
                    {
                        var ids = string.Join(", ", nm.ConnectedClientsIds);
                        Log($"[status] Clients: {nm.ConnectedClientsIds.Count} | IDs: [{ids}] | ServerId={NetworkManager.ServerClientId}");
                    }
                }
                break;
                case "save":
                {
                    string name = (parts.Length >= 2 ? parts[1] : ("logs_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                    string baseDir = Application.persistentDataPath;
                    string path = Path.Combine(baseDir, name.EndsWith(".log") ? name : (name + ".log"));
                    try
                    {
                        lock (_lines)
                        {
                            File.WriteAllLines(path, _lines.ToArray(), Encoding.UTF8);
                        }
                        Log("[save] wrote " + path);
                    }
                    catch (Exception ex)
                    {
                        Log("[save] failed: " + ex.Message);
                    }
                }
                break;
                case "host":
                {
                    Log("[host] starting...");
                    var res = await RelayQuickConnect.StartRelayHostAsync();
                    if (res.ok)
                    {
                        Log(string.IsNullOrEmpty(res.joinCode) ? "[host] started (direct)" : $"[host] started. Code={res.joinCode}");
                    }
                    else
                    {
                        Log($"[host] failed: {res.error}");
                    }
                }
                break;
                case "join":
                {
                    if (parts.Length < 2) { Log("Usage: join CODE"); break; }
                    string code = parts[1];
                    Log($"[join] {code}...");
                    var res = await RelayQuickConnect.StartRelayClientAsync(code);
                    Log(res.ok ? "[join] client started" : $"[join] failed: {res.error}");
                }
                break;
                case "server":
                {
                    if (parts.Length >= 2 && parts[1].Equals("external", StringComparison.OrdinalIgnoreCase))
                    {
                        // Try to launch start_server.bat in project root
                        string bat = FindStartServerBat();
                        if (string.IsNullOrEmpty(bat)) { Log("[server] start_server.bat not found"); break; }
                        try
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                                Arguments = "/C start \"\" \"" + bat + "\"",
                                WorkingDirectory = Path.GetDirectoryName(bat),
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            Process.Start(psi);
                            Log("[server] external server started (separate window)");
                        }
                        catch (Exception ex)
                        {
                            Log("[server] external start failed: " + ex.Message);
                        }
                    }
                    else
                    {
                        var nm = NetworkManager.Singleton;
                        if (nm == null) { Log("[server] NetworkManager missing"); break; }
                        if (!nm.IsListening)
                        {
                            bool ok = nm.StartServer();
                            Log(ok ? "[server] started (in-process)" : "[server] failed to start");
                        }
                        else
                        {
                            Log("[server] already running");
                        }
                    }
                }
                break;
                default:
                    Log($"Unknown command: {cmd}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"[error] {ex.Message}");
        }
    }

    private static string FindStartServerBat()
    {
        try
        {
            // Try a few likely locations relative to Assets
            string assets = Application.dataPath; // .../Friendly Royale_Data or .../Assets
            // If running in Editor, this points to project/Assets; in Player, to MyGame_Data
            var dir = new DirectoryInfo(assets);
            string candidate1 = Path.Combine(dir.Parent?.FullName ?? "", "start_server.bat");
            if (File.Exists(candidate1)) return candidate1;
            string candidate2 = Path.Combine(dir.Parent?.Parent?.FullName ?? "", "start_server.bat");
            if (File.Exists(candidate2)) return candidate2;
            // Workspace root (when running from editor under this repo structure)
            string candidate3 = Path.Combine(dir.FullName, "..", "start_server.bat");
            candidate3 = Path.GetFullPath(candidate3);
            if (File.Exists(candidate3)) return candidate3;
        }
        catch { }
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // Auto-create once per run
        if (_instance == null)
        {
            var go = new GameObject("MatchTerminal");
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<MatchTerminalPanel>();
        }
    }
}
