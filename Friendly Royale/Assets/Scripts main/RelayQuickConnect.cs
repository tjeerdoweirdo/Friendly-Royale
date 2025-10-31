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
/// Minimal helper to start a Relay-backed host or client with a simple join code.
/// Works independently of the matchmaking flow.
/// </summary>
public static class RelayQuickConnect
{
    public struct HostResult { public bool ok; public string joinCode; public string error; }
    public struct ClientResult { public bool ok; public string error; }

    public static async Task<HostResult> StartRelayHostAsync()
    {
#if UNITY_RELAY_INSTALLED
        try
        {
            await EnsureServicesAsync();
            var nm = NetworkManager.Singleton;
            if (nm == null) return new HostResult { ok = false, error = "NetworkManager missing" };
            var utp = nm.GetComponent<UnityTransport>();
            if (utp == null) return new HostResult { ok = false, error = "UnityTransport missing" };

            string proto = PlayerPrefs.GetString("RelayProtocol", "wss"); // wss over 443 default
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            var serverData = new RelayServerData(alloc, proto);
            utp.SetRelayServerData(serverData);
            nm.StartHost();
            return new HostResult { ok = true, joinCode = joinCode };
        }
        catch (System.Exception ex)
        {
            return new HostResult { ok = false, error = ex.Message };
        }
#else
        // No Relay package: fallback to direct host
        var nm = NetworkManager.Singleton;
        if (nm == null) return new HostResult { ok = false, error = "NetworkManager missing" };
        nm.StartHost();
        return new HostResult { ok = true, joinCode = "" };
#endif
    }

    public static async Task<ClientResult> StartRelayClientAsync(string joinCode)
    {
#if UNITY_RELAY_INSTALLED
        try
        {
            await EnsureServicesAsync();
            var nm = NetworkManager.Singleton;
            if (nm == null) return new ClientResult { ok = false, error = "NetworkManager missing" };
            var utp = nm.GetComponent<UnityTransport>();
            if (utp == null) return new ClientResult { ok = false, error = "UnityTransport missing" };
            if (string.IsNullOrWhiteSpace(joinCode)) return new ClientResult { ok = false, error = "Join code empty" };

            string proto = PlayerPrefs.GetString("RelayProtocol", "wss");
            JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(joinCode);
            var serverData = new RelayServerData(join, proto);
            utp.SetRelayServerData(serverData);
            nm.StartClient();
            return new ClientResult { ok = true };
        }
        catch (System.Exception ex)
        {
            return new ClientResult { ok = false, error = ex.Message };
        }
#else
        // No Relay: try direct client
        var nm = NetworkManager.Singleton;
        if (nm == null) return new ClientResult { ok = false, error = "NetworkManager missing" };
        nm.StartClient();
        return new ClientResult { ok = true };
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
}
