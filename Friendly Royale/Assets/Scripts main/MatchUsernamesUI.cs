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

    private void Start()
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

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsList != null)
        {
            foreach (var cc in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (cc.ClientId == NetworkManager.Singleton.LocalClientId) continue;
                var oppPP = PlayerProgress.GetPlayerProgress(cc.ClientId);
                if (oppPP != null)
                {
                    var n = oppPP.GetUsername();
                    if (!string.IsNullOrEmpty(n)) opponentName = n;
                    break;
                }
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
