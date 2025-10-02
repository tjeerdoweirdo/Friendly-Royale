using UnityEngine;
using Unity.Netcode;

public class NetworkManagerSetup : MonoBehaviour
{
    void Awake()
    {
        // Ensure NetworkManager persists across scenes
        if (NetworkManager.Singleton != null && NetworkManager.Singleton != this.GetComponent<NetworkManager>())
        {
            Destroy(gameObject);
            return;
        }
        
        DontDestroyOnLoad(gameObject);
        
        // Configure basic settings
        var networkManager = GetComponent<NetworkManager>();
        if (networkManager != null)
        {
            // Set connection approval for security
            networkManager.ConnectionApprovalCallback = ApprovalCheck;
            networkManager.NetworkConfig.ConnectionApproval = true;
        }
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // For now, approve all connections
        // In production, you'd validate the client here
        response.Approved = true;
        response.CreatePlayerObject = true;
        
        Debug.Log($"Client connection approved: {request.ClientNetworkId}");
    }

    void Start()
    {
        Debug.Log("NetworkManager Setup Complete");
        Debug.Log("Available Network Transports:");
        
        var transports = GetComponents<Unity.Netcode.NetworkTransport>();
        foreach (var transport in transports)
        {
            Debug.Log($"- {transport.GetType().Name}");
        }
    }
}