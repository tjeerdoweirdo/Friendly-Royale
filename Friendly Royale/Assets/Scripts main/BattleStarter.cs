using UnityEngine;
using System.Collections;

public class BattleStarter : MonoBehaviour
{
    [Tooltip("Optional: assign in inspector. If null, this script will try to find the DeckManager at runtime.")]
    public DeckManager deckManager;

    [Tooltip("Delay before starting the initial draw")]
    public float preDrawDelay = 0.2f;

    [Tooltip("How long (seconds) to wait for a DeckManager to appear before giving up. Set to 0 to try only once.")]
    public float maxWaitForDeckManager = 5f;

    IEnumerator Start()
    {
        // quick check for already assigned or singleton instance
        if (deckManager == null) deckManager = DeckManager.Instance;

        // if still null, wait a short while for DeckManager to become available (useful when it's created in another scene)
        if (deckManager == null)
        {
            // if maxWaitForDeckManager <= 0, do a single immediate try
            if (maxWaitForDeckManager <= 0f)
            {
                TryFindDeckManagerOnce();
            }
            else
            {
                yield return StartCoroutine(WaitForDeckManager(maxWaitForDeckManager));
            }
        }

        if (deckManager == null)
        {
            Debug.LogWarning("[BattleStarter] Could not find DeckManager. Initial draw aborted.");
            yield break;
        }

        // --- Reset deck and hand for new match ---
        deckManager.ResetForNewMatch();

        // small delay before drawing
        yield return new WaitForSeconds(preDrawDelay);

        // start the initial draw coroutine on the deck manager
        yield return StartCoroutine(deckManager.InitialDraw());
    }

    void TryFindDeckManagerOnce()
    {
        // try singleton first, then scene search fallback
        if (DeckManager.Instance != null)
        {
            deckManager = DeckManager.Instance;
            return;
        }

        var dm = FindObjectOfType<DeckManager>();
        if (dm != null) deckManager = dm;
    }

    IEnumerator WaitForDeckManager(float timeout)
    {
        float timer = 0f;

        // keep checking until timeout
        while (timer < timeout)
        {
            if (DeckManager.Instance != null)
            {
                deckManager = DeckManager.Instance;
                Debug.Log("[BattleStarter] Linked to DeckManager.Instance.");
                yield break;
            }

            // extra attempt to find by type in the scene (in case singleton wasn't set for some reason)
            var dm = FindObjectOfType<DeckManager>();
            if (dm != null)
            {
                deckManager = dm;
                Debug.Log("[BattleStarter] Found DeckManager in scene via FindObjectOfType.");
                yield break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        // final attempt after timeout
        if (DeckManager.Instance != null) deckManager = DeckManager.Instance;
        else deckManager = FindObjectOfType<DeckManager>();
    }
}