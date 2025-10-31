using System;
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

    [Header("Defaults")] public string defaultPort = "7777";

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
        }
    }

    public async void OnHostClicked()
    {
        SetStatus("Hosting...");
        try
        {
            var result = await RelayQuickConnect.StartRelayHostAsync();
            if (result.ok)
            {
                if (!string.IsNullOrEmpty(result.joinCode))
                    SetStatus($"Hosting via Relay. Code: {result.joinCode}");
                else
                    SetStatus("Hosting (direct)");
            }
            else
            {
                SetStatus("Host failed: " + result.error);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Host error: " + ex.Message);
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
        Debug.Log("[ConnectInput] " + msg);
    }
}
