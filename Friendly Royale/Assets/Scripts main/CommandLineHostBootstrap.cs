using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

#if UNITY_RELAY_INSTALLED
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
#endif

/// <summary>
/// Boots a headless/CLI host or client when specific command-line flags are present.
/// Designed for running from a terminal/CI to host matches for anyone via Relay.
///
/// Supported flags:
///   -hostForAll                 Start Relay Host (or direct if Relay unavailable)
///   -serverOnly                 Start NGO server without client (no Relay)
///   -clientJoinCode ABC123      Start client and join Relay join code
///   -joinFile path              Write the Relay join code to a file
///   -maxPlayers N               Desired total players (default 2). Relay alloc = N-1
///   -relay                      Hint to use Relay (Host path). Defaults to Relay if available
/// </summary>
public class CommandLineHostBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            bool trigger = args.Any(a => string.Equals(a, "-hostForAll", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(a, "-serverOnly", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(a, "-clientJoinCode", StringComparison.OrdinalIgnoreCase));
            if (!trigger) return;

            var go = new GameObject("CLIHostBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<CommandLineHostBootstrap>();
        }
        catch { }
    }

    private async void Start()
    {
        var args = Environment.GetCommandLineArgs();
        string joinFile = GetArgValue(args, "-joinFile");
        string clientCode = GetArgValue(args, "-clientJoinCode");
        int maxPlayers = 2;
        int.TryParse(GetArgValue(args, "-maxPlayers") ?? "2", out maxPlayers);
        maxPlayers = Mathf.Clamp(maxPlayers, 2, 8);
        bool wantHost = args.Any(a => string.Equals(a, "-hostForAll", StringComparison.OrdinalIgnoreCase));
        bool serverOnly = args.Any(a => string.Equals(a, "-serverOnly", StringComparison.OrdinalIgnoreCase));
        bool relayHint = args.Any(a => string.Equals(a, "-relay", StringComparison.OrdinalIgnoreCase));

        EnsureNetworkManager();

        if (serverOnly)
        {
            bool ok = NetworkManager.Singleton.StartServer();
            Debug.Log("[CLI] ServerOnly=" + (ok ? "OK" : "FAIL"));
            return;
        }

        if (!string.IsNullOrEmpty(clientCode))
        {
            await StartRelayClientAsync(clientCode);
            return;
        }

        if (wantHost)
        {
            bool ok = await StartHostAsync(maxPlayers, relayHint, joinFile);
            Debug.Log("[CLI] Host=" + (ok ? "OK" : "FAIL"));
            return;
        }
    }

    private void EnsureNetworkManager()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null) return;

        var go = new GameObject("NetworkManager");
        DontDestroyOnLoad(go);
        nm = go.AddComponent<NetworkManager>();
        go.AddComponent<UnityTransport>();
    }

    private static string GetArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private async Task<bool> StartHostAsync(int totalPlayers, bool relayHint, string joinFile)
    {
#if UNITY_RELAY_INSTALLED
        try
        {
            await EnsureServicesAsync();
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (utp == null) utp = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
            string proto = PlayerPrefs.GetString("RelayProtocol", "wss");

            // Relay allocation requires number of client slots (total - 1 for host)
            int slots = Mathf.Max(1, totalPlayers - 1);
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(slots);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            var serverData = new RelayServerData(alloc, proto);
            utp.SetRelayServerData(serverData);
            bool ok = NetworkManager.Singleton.StartHost();
            Debug.Log($"[CLI] Relay Host started. JOIN_CODE={joinCode}");
            if (!string.IsNullOrEmpty(joinFile))
            {
                TryWriteJoinFile(joinFile, joinCode);
            }
            return ok;
        }
        catch (Exception ex)
        {
            Debug.LogError("[CLI] Relay host failed: " + ex.Message);
#endif
            // Fallback to direct host
            bool okDirect = NetworkManager.Singleton.StartHost();
            Debug.Log("[CLI] Direct Host started (no Relay)");
            if (!string.IsNullOrEmpty(joinFile)) TryWriteJoinFile(joinFile, "");
            return okDirect;
#if UNITY_RELAY_INSTALLED
        }
#endif
    }

#if UNITY_RELAY_INSTALLED
    private static async Task EnsureServicesAsync()
    {
        if (Unity.Services.Core.UnityServices.State != ServicesInitializationState.Initialized)
        {
            await Unity.Services.Core.UnityServices.InitializeAsync();
        }
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }
#endif

    private void TryWriteJoinFile(string path, string code)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, code ?? string.Empty);
            Debug.Log("[CLI] Wrote join file: " + Path.GetFullPath(path));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CLI] Failed to write join file: " + ex.Message);
        }
    }

    private async Task<bool> StartRelayClientAsync(string code)
    {
#if UNITY_RELAY_INSTALLED
        try
        {
            await EnsureServicesAsync();
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (utp == null) utp = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
            string proto = PlayerPrefs.GetString("RelayProtocol", "wss");
            JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(code);
            var serverData = new RelayServerData(join, proto);
            utp.SetRelayServerData(serverData);
            bool ok = NetworkManager.Singleton.StartClient();
            Debug.Log("[CLI] Client join started");
            return ok;
        }
        catch (Exception ex)
        {
            Debug.LogError("[CLI] Client join failed: " + ex.Message);
            return false;
        }
#else
        bool okDirect = NetworkManager.Singleton.StartClient();
        Debug.Log("[CLI] Direct client started (no Relay)");
        return okDirect;
#endif
    }
}
