using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class ChestUI : MonoBehaviour
{
    public ChestSO chest;
    public TMP_Text titleText;
    public TMP_Text timerText;
    public Button openButton; // open now (cost)
    public Button startUnlockButton; // start timed unlock
    public int instantOpenCostGold = 100; // cost to open instantly

    private float unlockTimer = 0f;
    private bool unlocking = false;

    void Start()
    {
        if (titleText) titleText.text = chest.displayName;
        UpdateTimerUI();
        openButton.onClick.RemoveAllListeners();
        openButton.onClick.AddListener(OnInstantOpenClicked);
        startUnlockButton.onClick.RemoveAllListeners();
        startUnlockButton.onClick.AddListener(OnStartUnlockClicked);
    }

    void Update()
    {
        if (!unlocking) return;
        unlockTimer -= Time.deltaTime;
        if (unlockTimer <= 0f)
        {
            unlocking = false;
            unlockTimer = 0f;
            OnChestUnlocked();
        }
        UpdateTimerUI();
    }

    void UpdateTimerUI()
    {
        if (timerText == null) return;
        if (unlocking)
        {
            int sec = Mathf.CeilToInt(unlockTimer);
            timerText.text = $"Unlocking: {sec}s";
        }
        else
        {
            timerText.text = "Locked";
        }
    }

    public void OnStartUnlockClicked()
    {
        if (unlocking) return;
        unlocking = true;
        unlockTimer = chest.openTimeSeconds;
    }

    public void OnInstantOpenClicked()
    {
        if (!PlayerProgress.Instance.SpendGold(instantOpenCostGold))
        {
            Debug.Log("Not enough gold to open instantly.");
            return;
        }
        GiveChestRewards();
    }

    void OnChestUnlocked()
    {
        GiveChestRewards();
    }

    void GiveChestRewards()
    {
        var arenaID = DeckManager.Instance != null && DeckManager.Instance.selectedArena != null ? DeckManager.Instance.selectedArena.arenaID : "default";
        var reward = ChestManager.Instance.OpenChest(chest, arenaID);
        // show a quick log — you should show a UI panel listing gold and shards
        Debug.Log($"Chest opened: +{reward.gold} gold");
        foreach (var s in reward.shards)
        {
            Debug.Log($"+{s.shards} shards for {s.cardName}");
        }
        // optional: display UI popup with reward details (left as exercise)
    }
}
