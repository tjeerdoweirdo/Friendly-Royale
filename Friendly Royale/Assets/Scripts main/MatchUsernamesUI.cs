using TMPro;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Displays the two players' usernames in a TMP_Text you assign in the inspector.
/// Put this on a UI GameObject and assign the Text field.
/// Call Refresh() when players join or when names change. It also auto-refreshes on Start.
/// </summary>
public class MatchUsernamesUI : MonoBehaviour
{
    [SerializeField] private TMP_Text usernamesText;
    [SerializeField] private string fallbackLocal = "You";
    [SerializeField] private string fallbackOpponent = "Opponent";
    [SerializeField] private string format = "{0} vs {1}"; // {0}=local, {1}=opponent

    private void OnEnable()
    {
        // Refresh once shown
        Refresh();
        // Subscribe to basic connection events if NetworkManager exists
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientEvent;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientEvent;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientEvent;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientEvent;
        }
    }

    private void OnClientEvent(ulong _)
    {
        Refresh();
    }

    public void Refresh()
    {
        if (usernamesText == null)
            return;

        string localName = fallbackLocal;
        string opponentName = fallbackOpponent;

        var localPP = PlayerProgress.LocalPlayerProgress;
        if (localPP != null)
        {
            var n = localPP.GetUsername();
            if (!string.IsNullOrEmpty(n)) localName = n;
        }

        // Also fall back to values stored by MatchmakingManager
        if (string.IsNullOrEmpty(localName))
        {
            localName = PlayerPrefs.GetString("LocalPlayerUsername", localName);
        }
        opponentName = PlayerPrefs.GetString("OpponentUsername", opponentName);

        // Try to resolve via PlayerProgress mapping if available; otherwise keep PlayerPrefs fallback
        if (NetworkManager.Singleton != null)
        {
            try
            {
                var clients = NetworkManager.Singleton.ConnectedClientsIds; // works on client and server
                foreach (var clientId in clients)
                {
                    if (clientId == NetworkManager.Singleton.LocalClientId) continue;
                    var oppPP = PlayerProgress.GetPlayerProgress(clientId);
                    if (oppPP != null)
                    {
                        var n = oppPP.GetUsername();
                        if (!string.IsNullOrEmpty(n)) { opponentName = n; break; }
                    }
                }
            }
            catch
            {
                // ignore and use PlayerPrefs fallback
            }
        }

        usernamesText.text = string.Format(format, localName, opponentName);
    }

    public void SetTextTarget(TMP_Text text)
    {
        usernamesText = text;
        Refresh();
    }
}
