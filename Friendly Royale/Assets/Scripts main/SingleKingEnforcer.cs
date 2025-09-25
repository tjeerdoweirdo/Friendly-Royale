using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Optional helper: ensure only one King per owner exists at runtime.
/// Converts the first found Princess Tower into a KingTower and destroys extras.
/// Useful to quickly migrate scenes that still have two princess towers.
/// </summary>
public class SingleKingEnforcer : MonoBehaviour
{
    void Start()
    {
        var towers = FindObjectsOfType<Tower>();
        var kept = new Dictionary<string, bool>(); // ownerTag -> kept?

        foreach (var t in towers)
        {
            // Only act on princess towers (named as such). Adjust condition if your tower naming differs.
            if (t.towerName != "Princess Tower") continue;

            string owner = string.IsNullOrEmpty(t.ownerTag) ? "Player" : t.ownerTag;

            if (!kept.ContainsKey(owner))
            {
                // Keep & convert to KingTower if necessary
                kept[owner] = true;
                var king = t.GetComponent<KingTower>();
                if (king == null)
                {
                    // add KingTower and copy relevant fields
                    king = t.gameObject.AddComponent<KingTower>();

                    // copy some fields from base Tower to KingTower instance
                    king.towerName = owner + " King Tower";
                    king.maxHealth = t.maxHealth;
                    king.attackRange = t.attackRange;
                    king.attackCooldown = t.attackCooldown;
                    king.damagePerShot = t.damagePerShot;
                    king.ownerTag = t.ownerTag;
                    king.healthBarPrefab = t.healthBarPrefab;
                    king.faction = t.faction;
                    king.isEnemyKingTower = (owner != "Player");
                    king.isPlayerKing = (owner == "Player");
                }
                else
                {
                    t.towerName = owner + " King Tower";
                }
            }
            else
            {
                Debug.Log($"SingleKingEnforcer: Removing extra Princess Tower owned by '{owner}'.");
                Destroy(t.gameObject);
            }
        }
    }
}
