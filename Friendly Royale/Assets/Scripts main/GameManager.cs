using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    [Header("Match Settings")]
    public float matchDuration = 180f;
    public bool pauseOnEnd = true;

    [Header("Scene Settings")]
    [Tooltip("Scene to load after match ends.")]
    public string nextSceneName = "MainMenu";

    [Header("References (assign in inspector or auto-find)")]
    public TMP_Text timerText;
    public TMP_Text resultText;
    public Transform playerKingTower;
    public Transform enemyKingTower;

    [Header("Reward UI")]
    public GameObject rewardPanel;
    public TMP_Text rewardText;
    public Button continueButton;

    float timeLeft = 0f;
    bool matchActive = false;
    bool resultShown = false;

    void Start()
    {
        FindReferences();
        StartMatch();
        if (rewardPanel != null) rewardPanel.SetActive(false);
        if (continueButton != null)
        {
            continueButton.gameObject.SetActive(false);
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(OnContinueButton);
        }
    }

    void FindReferences()
    {
        // UI: Prefer inspector, fallback to scene search
        if (timerText == null)
            timerText = FindObjectOfType<TMP_Text>(true); // fallback: first TMP_Text found
        if (resultText == null)
            resultText = GameObject.Find("ResultText")?.GetComponent<TMP_Text>();
        if (rewardPanel == null)
            rewardPanel = GameObject.Find("RewardPanel");
        if (rewardText == null && rewardPanel != null)
            rewardText = rewardPanel.GetComponentInChildren<TMP_Text>(true);
        if (continueButton == null)
            continueButton = FindObjectOfType<Button>(true);

        // Towers: Prefer inspector, fallback to tag search
        if (playerKingTower == null)
        {
            var playerTowerObj = GameObject.FindWithTag("PlayerKingTower");
            if (playerTowerObj != null) playerKingTower = playerTowerObj.transform;
        }
        if (enemyKingTower == null)
        {
            var enemyTowerObj = GameObject.FindWithTag("EnemyKingTower");
            if (enemyTowerObj != null) enemyKingTower = enemyTowerObj.transform;
        }
    }

    void Update()
    {
        if (!matchActive) return;

        timeLeft -= Time.deltaTime;
        if (timeLeft < 0f) timeLeft = 0f;

        UpdateTimerUI();

        if (timeLeft <= 0f)
        {
            EndMatchByTime();
        }
    }

    public void StartMatch()
    {
        timeLeft = Mathf.Max(0f, matchDuration);
        matchActive = true;
        resultShown = false;

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
        float playerHP = 0f, enemyHP = 0f;
        float playerMax = 0f, enemyMax = 0f;

        if (playerKingTower != null)
            TryGetHealthInfo(playerKingTower, out playerHP, out playerMax);

        if (enemyKingTower != null)
            TryGetHealthInfo(enemyKingTower, out enemyHP, out enemyMax);

        if (playerHP > enemyHP) EndMatch(true, $"Victory by HP ({Mathf.CeilToInt(playerHP)} vs {Mathf.CeilToInt(enemyHP)})");
        else if (enemyHP > playerHP) EndMatch(false, $"Defeat by HP ({Mathf.CeilToInt(playerHP)} vs {Mathf.CeilToInt(enemyHP)})");
        else EndMatch(false, "Draw — treat as Defeat");
    }

    public void EndMatch(bool playerWon, string reason = "")
    {
        if (resultShown) return;

        matchActive = false;
        resultShown = true;

        // Call MatchEndHandler to award rewards

        int gold = 0;
        int trophies = 0;
        var matchEndHandler = FindFirstObjectByType<MatchEndHandler>();
        if (matchEndHandler != null)
        {
            matchEndHandler.OnMatchEnd(playerWon);
            if (playerWon)
            {
                gold = matchEndHandler.winGold;
                trophies = matchEndHandler.winTrophies;
            }
            else
            {
                gold = matchEndHandler.loseGold;
                trophies = 0;
            }
        }
        else
        {
            // fallback to previous hardcoded values
            gold = playerWon ? 100 : 25;
            trophies = playerWon ? 30 : 0;
        }

        if (resultText != null)
        {
            resultText.gameObject.SetActive(true);
            resultText.text = playerWon ? $"VICTORY\n{reason}" : $"DEFEAT\n{reason}";
        }

        if (pauseOnEnd) Time.timeScale = 0f;

        ShowRewardPanel(playerWon, gold, trophies);
    }

    void ShowRewardPanel(bool playerWon, int gold, int trophies)
    {
        if (rewardPanel != null)
        {
            rewardPanel.SetActive(true);
            if (rewardText != null)
            {
                rewardText.text = playerWon
                    ? $"You won!\n+{gold} Gold\n+{trophies} Trophies"
                    : $"You lost!\n+{gold} Gold";
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
        EndMatch(true, reason);
    }

    public void LoseMatch(string reason = "Player King destroyed")
    {
        EndMatch(false, reason);
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

    // Attempts to get health info from a tower/unit
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