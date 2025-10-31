using System;
using System.Diagnostics;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class ConnectInputPanel : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField input;
    public TMP_Text statusText; // optional
    public Button connectButton; // optional
    public Button hostButton;    // optional (Relay host)
    public TMP_Text hostButtonLabel; // optional label text for host button
    public TMP_Text relayCodeText; // optional separate field to show Relay code

    [Header("Defaults")] public string defaultPort = "7777";

    [Header("Visuals")]
    public Color hostActiveColor = new Color(0.2f, 0.8f, 0.2f); // green-ish when hosting
    public Color hostPendingColor = new Color(1.0f, 0.65f, 0.0f); // orange while setting up
    [Tooltip("Also open a PowerShell window with the Relay code when hosting starts successfully")] public bool openPowerShellOnHost = true;

    private Color _hostOriginalColor = Color.white;
    private UnityEngine.UI.ColorBlock _hostOriginalColors;
    private bool _haveOriginalColors = false;
    private string _lastJoinCode = string.Empty;
    private Coroutine _progressRoutine;
    private string _progressBaseText = string.Empty;
    private string _watcherFilePath = null;

    private void Awake()
    {
        // Optional: wire button listeners if not set in Inspector
        if (connectButton != null)
        {
            connectButton.onClick.RemoveAllListeners();
            connectButton.onClick.AddListener(OnConnectClicked);
        }
        if (hostButton != null)
        {
            hostButton.onClick.RemoveAllListeners();
            hostButton.onClick.AddListener(OnHostClicked);
            if (hostButton.image != null)
            {
                _hostOriginalColor = hostButton.image.color;
            }
            _hostOriginalColors = hostButton.colors;
            _haveOriginalColors = true;
        }
        // Prepare watcher path
        try { _watcherFilePath = Path.Combine(Application.persistentDataPath, "relay_join_code_ui.txt"); } catch { }
    }

    public async void OnHostClicked()
    {
        // Toggle behavior: if already hosting, stop; else start Relay host
        if (IsHostingActive())
        {
            try
            {
                NetworkManager.Singleton.Shutdown();
            }
            catch { }
            UpdateHostVisual(false, null);
            SetStatus("Host stopped");
            _lastJoinCode = string.Empty;
            StopStatusProgress();
            return;
        }

        // Begin setup: show pending visuals and animated status
        UpdateHostPendingVisual();
        SetStatus("Setting up Relay...");
        StartStatusProgress("Setting up Relay");
        // Force-open a PowerShell window that will wait for a join code file (if path available)
        if (openPowerShellOnHost && !string.IsNullOrEmpty(_watcherFilePath))
        {
            try
            {
                // Clear previous file content so PS waits for fresh code
                if (File.Exists(_watcherFilePath)) File.Delete(_watcherFilePath);
                TryOpenPowerShellWatcher(_watcherFilePath);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[ConnectInput] Setup watcher failed: " + ex.Message);
            }
        }
        try
        {
            var result = await RelayQuickConnect.StartRelayHostAsync();
            if (result.ok)
            {
                if (!string.IsNullOrEmpty(result.joinCode))
                {
                    _lastJoinCode = result.joinCode;
                    SetStatus($"Hosting via Relay. Code: {result.joinCode}");
                    if (input != null) input.text = result.joinCode; // make it easy to copy/share
                    if (relayCodeText != null) relayCodeText.text = result.joinCode;
                    // Write code to watcher file so any PS window can pick it up
                    try { if (!string.IsNullOrEmpty(_watcherFilePath)) File.WriteAllText(_watcherFilePath, result.joinCode); } catch { }
                    if (openPowerShellOnHost)
                    {
                        // Also attempt direct PS popup with the code
                        TryOpenPowerShellWithCode(result.joinCode);
                    }
                }
                else
                {
                    _lastJoinCode = string.Empty;
                    SetStatus("Hosting (direct)");
                    if (relayCodeText != null) relayCodeText.text = string.Empty;
                    // Indicate direct host to the watcher file (optional, leave blank to make it time out)
                }
                StopStatusProgress();
                UpdateHostVisual(true, _lastJoinCode);
            }
            else
            {
                SetStatus("Host failed: " + result.error);
                StopStatusProgress();
                UpdateHostVisual(false, null);
                if (relayCodeText != null) relayCodeText.text = string.Empty;
                // Clean watcher file
                try { if (!string.IsNullOrEmpty(_watcherFilePath) && File.Exists(_watcherFilePath)) File.Delete(_watcherFilePath); } catch { }
            }
        }
        catch (Exception ex)
        {
            SetStatus("Host error: " + ex.Message);
            StopStatusProgress();
            UpdateHostVisual(false, null);
            if (relayCodeText != null) relayCodeText.text = string.Empty;
            try { if (!string.IsNullOrEmpty(_watcherFilePath) && File.Exists(_watcherFilePath)) File.Delete(_watcherFilePath); } catch { }
        }
    }

    public async void OnConnectClicked()
    {
        string raw = input != null ? (input.text ?? string.Empty).Trim() : string.Empty;
        if (string.IsNullOrEmpty(raw)) { SetStatus("Enter Relay code or IP:PORT"); return; }

        // Decide: Relay code vs IP:PORT
        if (LooksLikeRelayCode(raw))
        {
            SetStatus("Joining via Relay...");
            var res = await RelayQuickConnect.StartRelayClientAsync(raw);
            SetStatus(res.ok ? "Client started (Relay)" : ("Join failed: " + res.error));
            return;
        }

        // Parse IP[:PORT]
        string host = raw;
        int port = SafeParsePort(defaultPort, 7777);
        int idx = raw.LastIndexOf(':');
        if (idx > 0 && idx < raw.Length - 1)
        {
            host = raw.Substring(0, idx);
            port = SafeParsePort(raw.Substring(idx + 1), port);
        }

        // Start direct client
        try
        {
            EnsureNetworkTransport(host, (ushort)port);
            bool ok = NetworkManager.Singleton.StartClient();
            SetStatus(ok ? $"Connecting to {host}:{port}" : "Failed to start client");
        }
        catch (Exception ex)
        {
            SetStatus("Connect error: " + ex.Message);
        }
    }

    private bool LooksLikeRelayCode(string s)
    {
        // Relay codes are typically 6-12 uppercase alphanumeric; accept broader alphanum length as heuristic
        if (s.Length < 5 || s.Length > 16) return false;
        return Regex.IsMatch(s, "^[A-Za-z0-9]+$");
    }

    private int SafeParsePort(string text, int fallback)
    {
        if (int.TryParse(text, out var p))
        {
            if (p >= 1 && p <= 65535) return p;
        }
        return fallback;
    }

    private void EnsureNetworkTransport(string address, ushort port)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            var go = new GameObject("NetworkManager");
            DontDestroyOnLoad(go);
            nm = go.AddComponent<NetworkManager>();
        }
        var utp = nm.GetComponent<UnityTransport>();
        if (utp == null) utp = nm.gameObject.AddComponent<UnityTransport>();
        utp.SetConnectionData(address, port);
    }

    private void SetStatus(string msg)
    {
    if (statusText != null) statusText.text = msg;
    UnityEngine.Debug.Log("[ConnectInput] " + msg);
    }

    private bool IsHostingActive()
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.IsHost;
    }

    private void UpdateHostVisual(bool hosting, string joinCode)
    {
        if (hostButton != null && hostButton.image != null)
        {
            hostButton.image.color = hosting ? hostActiveColor : _hostOriginalColor;
            // Also update the full ColorBlock so Button transition doesn't override our color
            if (_haveOriginalColors)
            {
                if (hosting)
                {
                    hostButton.colors = MakeSolidColorBlock(hostActiveColor, hostButton.colors);
                }
                else
                {
                    hostButton.colors = _hostOriginalColors;
                }
            }
        }
        if (hostButtonLabel != null)
        {
            hostButtonLabel.text = hosting ? "Stop Host" : "Host via Relay";
        }
        if (hosting && !string.IsNullOrEmpty(joinCode) && statusText != null)
        {
            statusText.text = $"Hosting via Relay. Code: {joinCode}";
        }
        if (relayCodeText != null)
        {
            relayCodeText.text = hosting ? (joinCode ?? string.Empty) : string.Empty;
        }
    }

    private void UpdateHostPendingVisual()
    {
        if (hostButton != null && hostButton.image != null)
        {
            hostButton.image.color = hostPendingColor;
            if (_haveOriginalColors)
            {
                hostButton.colors = MakeSolidColorBlock(hostPendingColor, hostButton.colors);
            }
        }
        if (hostButtonLabel != null)
        {
            hostButtonLabel.text = "Starting...";
        }
    }

    private void StartStatusProgress(string baseText)
    {
        StopStatusProgress();
        _progressBaseText = baseText ?? string.Empty;
        _progressRoutine = StartCoroutine(StatusDots());
    }

    private void StopStatusProgress()
    {
        if (_progressRoutine != null)
        {
            StopCoroutine(_progressRoutine);
            _progressRoutine = null;
        }
    }

    private IEnumerator StatusDots()
    {
        int dots = 0;
        while (true)
        {
            string suffix = new string('.', dots % 4);
            SetStatus((_progressBaseText ?? "") + suffix);
            dots++;
            yield return new WaitForSeconds(0.35f);
        }
    }

    private void TryOpenPowerShellWithCode(string code)
    {
        try
        {
            // Open an external PowerShell window that shows and copies the join code
            // -NoExit keeps the window open; user can close it manually.
            // Set-Clipboard copies the code for convenience (Windows PowerShell 5+).
            string psCmd = $"Write-Host \"JOIN CODE: {code}\" -ForegroundColor Green; " +
                           $"Write-Host \"(Copied to clipboard)\" -ForegroundColor Yellow; " +
                           $"Set-Clipboard -Value '{code}'";
            var psi = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/C start \"\" powershell.exe -NoProfile -NoLogo -ExecutionPolicy Bypass -NoExit -Command \"" + psCmd.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[ConnectInput] Failed to open PowerShell: " + ex.Message);
        }
    }

    private void TryOpenPowerShellWatcher(string path)
    {
        try
        {
            string psCmd =
                "$path = '" + path.Replace("'", "''") + "';" +
                "$spinner = @('|','/','-','\\'); $i = 0;" +
                "Write-Host 'Waiting for Relay code ' -NoNewline -ForegroundColor Yellow;" +
                "$deadline = (Get-Date).AddSeconds(120);" +
                "while ((Get-Date) -lt $deadline) {" +
                " if (Test-Path $path) {" +
                "   try { $c = Get-Content -Path $path -Raw -ErrorAction Stop; if ($c -and $c.Trim().Length -gt 0) {" +
                "     Write-Host '\r' -NoNewline;" +
                "     Write-Host ('JOIN CODE: ' + $c) -ForegroundColor Green;" +
                "     Write-Host '(Copied to clipboard)' -ForegroundColor Yellow;" +
                "     Set-Clipboard -Value $c; break } } catch {} }" +
                " Write-Host ('\rWaiting for Relay code ' + $spinner[$i % $spinner.Length]) -NoNewline -ForegroundColor Yellow;" +
                " $i++; Start-Sleep -Milliseconds 250 }" +
                "if (-not (Test-Path $path) -or -not ((Get-Content -Path $path -Raw) -and ((Get-Content -Path $path -Raw).Trim().Length -gt 0))) {" +
                " Write-Host '\nNo Relay code produced (direct host or timed out).' -ForegroundColor DarkYellow }";

            var psi = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/C start \"\" powershell.exe -NoProfile -NoLogo -ExecutionPolicy Bypass -NoExit -Command \"" + psCmd.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[ConnectInput] Failed to open PS watcher: " + ex.Message);
        }
    }

    private static UnityEngine.UI.ColorBlock MakeSolidColorBlock(Color c, UnityEngine.UI.ColorBlock template)
    {
        var b = template;
        b.normalColor = c;
        b.highlightedColor = c;
        b.pressedColor = c;
        b.selectedColor = c;
        // keep disabledColor and multipliers/fade as-is
        return b;
    }
}
