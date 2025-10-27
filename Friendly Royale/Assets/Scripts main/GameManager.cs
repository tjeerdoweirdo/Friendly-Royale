using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;

public enum MatchResult
{
    Win,
    Loss,
    Draw
}

public class GameManager : NetworkBehaviour
{
    [Header("Match Settings")]
    public float matchDuration = 180f;
    public bool pauseOnEnd = true;

    [Header("Scene Settings")]
    [Tooltip("Scene to load after match ends.")]
    public string nextSceneName = "MainMenu";

    [Header("Player Sides")] 
    [Tooltip("If true, the Host will be Player1 in online matches; the Client will be Player2. In practice/offline, local is always Player1.")]
    public bool hostIsPlayer1 = true;
    [Tooltip("Computed at runtime: whether the local player is Player1.")]
    public bool localIsPlayer1 = true;
    [Tooltip("Computed at runtime: whether the local player is Player2.")]
    public bool localIsPlayer2 = false;

    [Header("References (assign in inspector or auto-find)")]
    public TMP_Text timerText;
    public TMP_Text resultText;
    
    [Header("King Towers (ASSIGN THESE IN INSPECTOR)")]
    [Tooltip("Drag the Player King Tower here from the scene")]
    public Transform playerKingTower;
    [Tooltip("Drag the Enemy King Tower here from the scene")]
    public Transform enemyKingTower;

    [Header("Reward UI")]
    public GameObject rewardPanel;
    public TMP_Text rewardText;
    public Button continueButton;

    // Network Variables for multiplayer synchronization.
    // Give server write permission explicitly so only the server can update them.
    private NetworkVariable<float> networkTimeLeft = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> networkMatchActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> networkResultShown = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Local variables (for single player or local read convenience)
    float timeLeft = 0f;
    bool matchActive = false;
    bool resultShown = false;

    void Start()
    {
        FindReferences();
        // Establish sides and toggle AI depending on mode (practice vs online)
        DeterminePlayerSidesAndAIMode();
        StartMatch();
        if (rewardPanel != null) rewardPanel.SetActive(false);
        if (continueButton != null)
        {
            continueButton.gameObject.SetActive(false);
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(OnContinueButton);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // When Netcode initializes, recompute sides based on host/client role
        DeterminePlayerSidesAndAIMode();
    }

    void Update()
    {
        // Use network variables if we're in multiplayer, otherwise use local variables
        bool isActive = IsNetworkActive() ? networkMatchActive.Value : matchActive;
        if (!isActive) return;

        // Only the server should update the timer in multiplayer
        if (IsNetworkActive())
        {
            if (IsServer)
            {
                networkTimeLeft.Value -= Time.deltaTime;
                if (networkTimeLeft.Value < 0f) networkTimeLeft.Value = 0f;

                if (networkTimeLeft.Value <= 0f && networkMatchActive.Value)
                {
                    // call a local server method that resolves the winner and sends client RPC
                    EndMatchByTimeOnServer();
                }
            }

            // Clients (and host) should read the authoritative time variable
            timeLeft = networkTimeLeft.Value;
            matchActive = networkMatchActive.Value;
            resultShown = networkResultShown.Value;
        }
        else
        {
            // Single player mode
            timeLeft -= Time.deltaTime;
            if (timeLeft < 0f) timeLeft = 0f;

            if (timeLeft <= 0f && matchActive)
            {
                EndMatchByTime();
            }
        }

        UpdateTimerUI();
    }

    void FindReferences()
    {
        // UI: Prefer inspector, fallback to scene search
        if (timerText == null)
            timerText = UnityEngine.Object.FindFirstObjectByType<TMP_Text>();
        if (resultText == null)
            resultText = GameObject.Find("ResultText")?.GetComponent<TMP_Text>();
        if (rewardPanel == null)
            rewardPanel = GameObject.Find("RewardPanel");
        if (rewardText == null && rewardPanel != null)
            rewardText = rewardPanel.GetComponentInChildren<TMP_Text>(true);
        if (continueButton == null)
            continueButton = UnityEngine.Object.FindFirstObjectByType<Button>();

        // Towers: Only use inspector assignments (no auto-find fallback)
        // If towers are not assigned, warn the user
        if (playerKingTower == null)
        {
            Debug.LogWarning("Player King Tower is not assigned! Please assign it in the GameManager inspector.");
        }
        if (enemyKingTower == null)
        {
            Debug.LogWarning("Enemy King Tower is not assigned! Please assign it in the GameManager inspector.");
        }
    }

    // Helper method to check if we're in network mode
    private bool IsNetworkActive()
    {
        return NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient);
    }

    // Helper to check if we are in an offline/practice mode
    private bool IsOfflineOrPracticeMode()
    {
        // Prefer GameModeManager when available
        try
        {
            if (GameModeManager.Instance != null)
            {
                return GameModeManager.Instance.IsOfflineMode();
            }
        }
        catch { /* ignore if GameModeManager not present */ }

        // Fallback: if Netcode isn't active, treat as offline/practice
        return !IsNetworkActive();
    }

    private void DeterminePlayerSidesAndAIMode()
    {
        bool offline = IsOfflineOrPracticeMode();
        if (offline)
        {
            localIsPlayer1 = true;
            localIsPlayer2 = false;
            ToggleEnemyBot(true);
        }
        else if (IsNetworkActive())
        {
            bool isHost = NetworkManager.Singleton.IsHost;
            localIsPlayer1 = isHost ? hostIsPlayer1 : !hostIsPlayer1;
            localIsPlayer2 = !localIsPlayer1;
            ToggleEnemyBot(false);
        }
        else
        {
            // Safe default
            localIsPlayer1 = true;
            localIsPlayer2 = false;
            ToggleEnemyBot(true);
        }

        // Persist local side so TowerSceneAutoConfigurator can set factions consistently per client
        try
        {
            PlayerPrefs.SetInt("LocalPlayerIsPlayer1", localIsPlayer1 ? 1 : 0);
            PlayerPrefs.Save();
        }
        catch { /* ignore */ }

        // Re-run tower auto configuration immediately, if present
        try
        {
            var cfg = TowerSceneAutoConfigurator.Instance;
            if (cfg != null)
            {
                cfg.ConfigureIfNeeded();
            }
        }
        catch { /* ignore */ }

        Debug.Log($"[GameManager] Local side: {(localIsPlayer1 ? "Player1" : "Player2")} | EnemyBot: {(offline ? "ENABLED" : "DISABLED")} | Pref(LocalPlayerIsPlayer1)={(PlayerPrefs.GetInt("LocalPlayerIsPlayer1",1))}");
    }

    private void ToggleEnemyBot(bool enable)
    {
        try
        {
            // Include inactive objects so we can enable their scripts too
            var bots = UnityEngine.Object.FindObjectsByType<EnemyBot>(FindObjectsSortMode.None);
            if (bots == null || bots.Length == 0)
            {
                Debug.LogWarning("[GameManager] No EnemyBot found in scene to toggle. For practice mode, add an EnemyBot to the scene.");
                return;
            }
            foreach (var bot in bots)
            {
                if (bot == null) continue;
                // Ensure the GameObject is active as well as the component
                if (bot.gameObject != null) bot.gameObject.SetActive(enable);
                bot.enabled = enable;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameManager] ToggleEnemyBot encountered an issue: {e.Message}");
        }
    }

    public void StartMatch()
    {
        if (IsNetworkActive())
        {
            // Only the server should initiate the match start and set network variables.
            if (IsServer)
            {
                StartMatchServerRpc();
            }
            // Clients do nothing here; StartMatchClientRpc will be invoked by server and sync local state.
        }
        else
        {
            // Single player mode
            timeLeft = Mathf.Max(0f, matchDuration);
            matchActive = true;
            resultShown = false;

            if (resultText != null) resultText.gameObject.SetActive(false);
            if (rewardPanel != null) rewardPanel.SetActive(false);
            if (continueButton != null) continueButton.gameObject.SetActive(false);

            if (Time.timeScale == 0f) Time.timeScale = 1f;

            UpdateTimerUI();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void StartMatchServerRpc()
    {
        // Server sets authoritative variables
        networkTimeLeft.Value = Mathf.Max(0f, matchDuration);
        networkMatchActive.Value = true;
        networkResultShown.Value = false;

        // Notify clients to update local UI / variables
        StartMatchClientRpc();
    }

    [ClientRpc]
    private void StartMatchClientRpc()
    {
        // Sync local variables from the network variables (they were already written by server)
        timeLeft = networkTimeLeft.Value;
        matchActive = networkMatchActive.Value;
        resultShown = networkResultShown.Value;

        if (resultText != null) resultText.gameObject.SetActive(false);
        if (rewardPanel != null) rewardPanel.SetActive(false);
        if (continueButton != null) continueButton.gameObject.SetActive(false);

        if (Time.timeScale == 0f) Time.timeScale = 1f;

        UpdateTimerUI();
    }

    void UpdateTimerUI()
    {
        if (timerText == null) return;
        TimeSpan t = TimeSpan.FromSeconds(Mathf.CeilToInt(timeLeft));
        timerText.text = $"{t.Minutes:D2}:{t.Seconds:D2}";
    }

    public void EndMatchByTime()
    {
        // Single-player/time-expired resolution (non-networked)
        float playerHP = 0f, enemyHP = 0f;
        float playerMax = 0f, enemyMax = 0f;

        if (playerKingTower != null)
            TryGetHealthInfo(playerKingTower, out playerHP, out playerMax);

        if (enemyKingTower != null)
            TryGetHealthInfo(enemyKingTower, out enemyHP, out enemyMax);

        if (playerHP > enemyHP) EndMatch(MatchResult.Win, $"Victory by HP ({Mathf.CeilToInt(playerHP)} vs {Mathf.CeilToInt(enemyHP)})");
        else if (enemyHP > playerHP) EndMatch(MatchResult.Loss, $"Defeat by HP ({Mathf.CeilToInt(playerHP)} vs {Mathf.CeilToInt(enemyHP)})");
        else EndMatch(MatchResult.Draw, $"Draw ({Mathf.CeilToInt(playerHP)} vs {Mathf.CeilToInt(enemyHP)})");
    }

    // Server-only method that resolves result and then notifies clients
    private void EndMatchByTimeOnServer()
    {
        if (!IsServer) return;

        float playerHP = 0f, enemyHP = 0f;
        float playerMax = 0f, enemyMax = 0f;

        if (playerKingTower != null)
            TryGetHealthInfo(playerKingTower, out playerHP, out playerMax);

        if (enemyKingTower != null)
            TryGetHealthInfo(enemyKingTower, out enemyHP, out enemyMax);

        MatchResult result;
        string reason;

        if (playerHP > enemyHP)
        {
            result = MatchResult.Win;
            reason = $"Victory by HP ({Mathf.CeilToInt(playerHP)} vs {Mathf.CeilToInt(enemyHP)})";
        }
        else if (enemyHP > playerHP)
        {
            result = MatchResult.Loss;
            reason = $"Defeat by HP ({Mathf.CeilToInt(playerHP)} vs {Mathf.CeilToInt(enemyHP)})";
        }
        else
        {
            result = MatchResult.Draw;
            reason = $"Draw ({Mathf.CeilToInt(playerHP)} vs {Mathf.CeilToInt(enemyHP)})";
        }

        // Set authoritative network flags and notify clients
        EndMatchNetworkedServerRpc(result, reason);
    }

    // Server sets authoritative state and sends a ClientRpc to tell everyone about the result
    [ServerRpc(RequireOwnership = false)]
    private void EndMatchNetworkedServerRpc(MatchResult result, string reason = "")
    {
        if (networkResultShown.Value) return;

        networkMatchActive.Value = false;
        networkResultShown.Value = true;

        // Let clients handle the UI/pause/etc.
        EndMatchNetworkedClientRpc(result, reason);
    }

    [ClientRpc]
    private void EndMatchNetworkedClientRpc(MatchResult result, string reason = "")
    {
        // Sync local variables from network variables
        matchActive = networkMatchActive.Value;
        resultShown = networkResultShown.Value;

        // Handle the match end locally (UI, rewards)
        HandleLocalMatchEnd(result, reason);
    }

    // Single-player EndMatch method
    public void EndMatch(MatchResult result, string reason = "")
    {
        if (resultShown) return;
        resultShown = true;
        matchActive = false;
        
        HandleLocalMatchEnd(result, reason);
    }

    private void HandleLocalMatchEnd(MatchResult result, string reason = "")
    {
        // This is essentially the same as the original EndMatch (single-player) but called locally by all clients

        // Mark local flags so local UI won't re-run
        resultShown = true;
        matchActive = false;

        // Call MatchEndHandler to award rewards (if present)
        int gold = 0;
        int trophies = 0;
        var matchEndHandler = UnityEngine.Object.FindFirstObjectByType<MatchEndHandler>();
        if (matchEndHandler != null)
        {
            matchEndHandler.OnMatchEnd(result);
            switch (result)
            {
                case MatchResult.Win:
                    gold = matchEndHandler.winGold;
                    trophies = matchEndHandler.winTrophies;
                    break;
                case MatchResult.Loss:
                    gold = matchEndHandler.loseGold;
                    trophies = matchEndHandler.loseTrophies;
                    break;
                case MatchResult.Draw:
                    gold = matchEndHandler.drawGold;
                    trophies = matchEndHandler.drawTrophies;
                    break;
            }
        }
        else
        {
            // fallback values
            switch (result)
            {
                case MatchResult.Win:
                    gold = 100;
                    trophies = 30;
                    break;
                case MatchResult.Loss:
                    gold = 25;
                    trophies = -15;
                    break;
                case MatchResult.Draw:
                    gold = 50;
                    trophies = 10;
                    break;
            }
        }

        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            string statusText = result == MatchResult.Win ? "VICTORY" : result == MatchResult.Draw ? "DRAW" : "DEFEAT";
            resultText.text = string.IsNullOrEmpty(reason) ? statusText : ($"{statusText}\n{reason}");
        }

        if (pauseOnEnd) Time.timeScale = 0f;

        ShowRewardPanel(result, gold, trophies);
    }

    void ShowRewardPanel(MatchResult result, int gold, int trophies)
    {
        if (rewardPanel != null)
        {
            rewardPanel.SetActive(true);
            if (rewardText != null)
            {
                string message = "";
                switch (result)
                {
                    case MatchResult.Win:
                        message = $"You won!\n+{gold} Gold\n+{trophies} Trophies";
                        break;
                    case MatchResult.Loss:
                        message = $"You lost!\n+{gold} Gold\n{trophies} Trophies";
                        break;
                    case MatchResult.Draw:
                        message = $"It's a draw!\n+{gold} Gold\n+{trophies} Trophies";
                        break;
                }
                rewardText.text = message;
            }
            if (continueButton != null) continueButton.gameObject.SetActive(false);
            StartCoroutine(ShowContinueButtonAfterDelay(2f));
        }
    }

    System.Collections.IEnumerator ShowContinueButtonAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (continueButton != null) continueButton.gameObject.SetActive(true);
    }

    void OnContinueButton()
    {
        Time.timeScale = 1f;
        if (!string.IsNullOrEmpty(nextSceneName))
            SceneManager.LoadScene(nextSceneName);
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void WinMatch(string reason = "King destroyed")
    {
        if (IsNetworkActive())
        {
            if (IsServer)
            {
                EndMatchNetworkedServerRpc(MatchResult.Win, reason);
            }
            // if client wants to request a win, you could expose a client->server RPC separately (not implemented here)
        }
        else
        {
            EndMatch(MatchResult.Win, reason);
        }
    }

    public void LoseMatch(string reason = "Player King destroyed")
    {
        if (IsNetworkActive())
        {
            if (IsServer)
            {
                EndMatchNetworkedServerRpc(MatchResult.Loss, reason);
            }
        }
        else
        {
            EndMatch(MatchResult.Loss, reason);
        }
    }

    public void RestartMatch()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnApplicationQuit()
    {
        Time.timeScale = 1f;
    }

    // Attempts to get health info from a tower/unit (reflection-based fallback)
    bool TryGetHealthInfo(Transform t, out float current, out float max)
    {
        current = 0f;
        max = 0f;
        if (t == null) return false;

        var tower = t.GetComponent<Tower>();
        if (tower != null)
        {
            FieldInfo fi = typeof(Tower).GetField("currentHealth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            FieldInfo maxi = typeof(Tower).GetField("maxHealth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null) current = Convert.ToSingle(fi.GetValue(tower));
            if (maxi != null) max = Convert.ToSingle(maxi.GetValue(tower));

            if (current == 0f)
            {
                PropertyInfo piCur = typeof(Tower).GetProperty("currentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (piCur != null) current = Convert.ToSingle(piCur.GetValue(tower));
            }
            if (max == 0f)
            {
                PropertyInfo piMax = typeof(Tower).GetProperty("maxHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (piMax != null) max = Convert.ToSingle(piMax.GetValue(tower));
            }

            if (max == 0f)
            {
                FieldInfo fPublicMax = typeof(Tower).GetField("maxHealth", BindingFlags.Instance | BindingFlags.Public);
                if (fPublicMax != null) max = Convert.ToSingle(fPublicMax.GetValue(tower));
            }

            return (current > 0f || max > 0f);
        }

        var unitHealth = t.GetComponent<UnitHealth>();
        if (unitHealth != null)
        {
            FieldInfo fi = typeof(UnitHealth).GetField("currentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo maxi = typeof(UnitHealth).GetField("maxHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) current = Convert.ToSingle(fi.GetValue(unitHealth));
            if (maxi != null) max = Convert.ToSingle(maxi.GetValue(unitHealth));

            PropertyInfo piC = typeof(UnitHealth).GetProperty("currentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo piM = typeof(UnitHealth).GetProperty("maxHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (current == 0f && piC != null) current = Convert.ToSingle(piC.GetValue(unitHealth));
            if (max == 0f && piM != null) max = Convert.ToSingle(piM.GetValue(unitHealth));

            return (current > 0f || max > 0f);
        }

        var healthComp = t.GetComponent("Health");
        if (healthComp != null)
        {
            Type ht = healthComp.GetType();
            FieldInfo fi = ht.GetField("currentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo maxi = ht.GetField("maxHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) current = Convert.ToSingle(fi.GetValue(healthComp));
            if (maxi != null) max = Convert.ToSingle(maxi.GetValue(healthComp));

            PropertyInfo pip = ht.GetProperty("currentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo pim = ht.GetProperty("maxHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (current == 0f && pip != null) current = Convert.ToSingle(pip.GetValue(healthComp));
            if (max == 0f && pim != null) max = Convert.ToSingle(pim.GetValue(healthComp));

            return (current > 0f || max > 0f);
        }

        Component[] comps = t.GetComponents<Component>();
        foreach (var comp in comps)
        {
            if (comp == null) continue;
            Type ct = comp.GetType();

            var fields = ct.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                string n = f.Name.ToLower();
                if (n.Contains("current") || n.Contains("hp") || n.Contains("health"))
                {
                    object val = f.GetValue(comp);
                    if (val is int) current = Convert.ToSingle((int)val);
                    else if (val is float) current = (float)val;
                }
                if (n.Contains("max") || n.Contains("maxhealth") || n.Contains("maxhp"))
                {
                    object val = f.GetValue(comp);
                    if (val is int) max = Convert.ToSingle((int)val);
                    else if (val is float) max = (float)val;
                }
            }

            var props = ct.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var p in props)
            {
                string n = p.Name.ToLower();
                if (!p.CanRead) continue;
                try
                {
                    if ((n.Contains("current") || n.Contains("hp") || n.Contains("health")) && current == 0f)
                    {
                        var v = p.GetValue(comp, null);
                        if (v is int) current = Convert.ToSingle((int)v);
                        else if (v is float) current = Convert.ToSingle(v);
                    }
                    if ((n.Contains("max") || n.Contains("maxhealth") || n.Contains("maxhp")) && max == 0f)
                    {
                        var v = p.GetValue(comp, null);
                        if (v is int) max = Convert.ToSingle((int)v);
                        else if (v is float) max = Convert.ToSingle(v);
                    }
                }
                catch { }
            }

            if (current > 0f || max > 0f)
                return true;
        }

        return false;
    }
}
