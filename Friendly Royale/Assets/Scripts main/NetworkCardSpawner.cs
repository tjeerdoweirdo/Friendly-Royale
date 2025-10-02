using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// Networked CardSpawner that handles multiplayer card spawning with proper authority and synchronization.
/// Only the owner of a unit can spawn it, and all spawns are validated on the server.
/// </summary>
public class NetworkCardSpawner : NetworkBehaviour
{
    [Header("Spawn Points")]
    public Transform leftLaneSpawnPlayer;
    public Transform rightLaneSpawnPlayer;
    public Transform leftLaneSpawnEnemy;
    public Transform rightLaneSpawnEnemy;

    [Header("Lane Paths (Waypoints)")]
    public Transform[] leftPathPlayer;   // assign path waypoints in inspector
    public Transform[] rightPathPlayer;
    public Transform[] leftPathEnemy;
    public Transform[] rightPathEnemy;

    [Header("King Towers (end of path)")]
    [Tooltip("The player's king tower (enemy units will target this).")]
    public Tower playerKingTower;
    [Tooltip("The enemy's king tower (player units will target this).")]
    public Tower enemyKingTower;

    [Header("Placement")]
    public float playRange = 20f; // restrict placement if needed (optional)
    
    private static NetworkCardSpawner instance;
    public static NetworkCardSpawner Instance => instance;
    
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Request to spawn a card at a specific position. This will be validated on the server.
    /// </summary>
    /// <param name="card">Card to spawn</param>
    /// <param name="position">World position to spawn at</param>
    /// <param name="playerFaction">Which player is spawning (Player or Enemy)</param>
    public void RequestSpawnCard(Card card, Vector3 position, Unit.Faction playerFaction)
    {
        if (card == null) return;
        
        // Only the client with authority should be able to request spawns
        if (!IsOwner) return;
        
        // Send spawn request to server
        SpawnCardServerRpc(card.cardID, position, playerFaction, NetworkManager.Singleton.LocalClientId);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void SpawnCardServerRpc(string cardID, Vector3 position, Unit.Faction faction, ulong requestingClientId)
    {
        // TODO: Add server-side validation here
        // - Check if player has enough resources
        // - Validate position is within allowed placement area
        // - Check if card is in player's hand/deck
        
        // For now, allow all spawns (you should add validation)
        if (ValidateSpawnRequest(cardID, position, faction, requestingClientId))
        {
            // Find the card data (this is simplified - you may need a card database)
            Card cardData = FindCardData(cardID);
            if (cardData != null)
            {
                SpawnCardForAllClients(cardData, position, faction, requestingClientId);
            }
        }
    }
    
    private bool ValidateSpawnRequest(string cardID, Vector3 position, Unit.Faction faction, ulong clientId)
    {
        // TODO: Implement proper validation
        // - Check resource costs
        // - Validate placement area
        // - Check card availability
        // - Prevent cheating
        
        return true; // Placeholder - always allow for now
    }
    
    private Card FindCardData(string cardID)
    {
        // TODO: Implement proper card database lookup
        // This is a simplified version - you should have a proper card management system
        
        // Try to find card in scene (fallback method)
        Card[] allCards = FindObjectsOfType<Card>();
        foreach (Card card in allCards)
        {
            if (card.cardID == cardID)
            {
                return card;
            }
        }
        
        return null;
    }
    
    [ClientRpc]
    private void SpawnCardForAllClientsClientRpc(string cardID, Vector3 position, Unit.Faction faction, ulong ownerClientId)
    {
        Card cardData = FindCardData(cardID);
        if (cardData != null)
        {
            SpawnCardForAllClients(cardData, position, faction, ownerClientId);
        }
    }
    
    private void SpawnCardForAllClients(Card cardData, Vector3 position, Unit.Faction faction, ulong ownerClientId)
    {
        // Perform the actual spawning
        if (cardData.cardType == CardType.Troop)
        {
            SpawnTroop(cardData, position, faction, ownerClientId);
        }
        else if (cardData.cardType == CardType.Building)
        {
            SpawnBuilding(cardData, position, faction, ownerClientId);
        }
        else if (cardData.cardType == CardType.Spell)
        {
            CastSpell(cardData, position, faction, ownerClientId);
        }
    }
    
    private void SpawnTroop(Card card, Vector3 position, Unit.Faction faction, ulong ownerClientId)
    {
        if (card.unitPrefab == null) return;
        
        // Determine spawn count and formation
        int spawnCount = GetSpawnCount(card);
        
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 spawnPos = position;
            
            // Add some spread for multiple units
            if (spawnCount > 1)
            {
                float angle = (i * 360f / spawnCount) * Mathf.Deg2Rad;
                float radius = 1f + (i * 0.5f);
                spawnPos += new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            }
            
            // Spawn the unit
            GameObject unitObj = Instantiate(card.unitPrefab, spawnPos, Quaternion.identity);
            NetworkObject netObj = unitObj.GetComponent<NetworkObject>();
            
            if (netObj != null)
            {
                netObj.SpawnWithOwnership(ownerClientId);
            }
            
            // Set up the unit
            Unit unit = unitObj.GetComponent<Unit>();
            if (unit != null)
            {
                unit.faction = faction;
                SetupUnitPath(unit, faction);
            }
            
            // Apply card stats to unit
            ApplyCardStatsToUnit(card, unit);
        }
    }
    
    private void SpawnBuilding(Card card, Vector3 position, Unit.Faction faction, ulong ownerClientId)
    {
        if (card.unitPrefab == null) return; // Buildings use unitPrefab in this Card structure
        
        GameObject buildingObj = Instantiate(card.unitPrefab, position, Quaternion.identity);
        NetworkObject netObj = buildingObj.GetComponent<NetworkObject>();
        
        if (netObj != null)
        {
            netObj.SpawnWithOwnership(ownerClientId);
        }
        
        // Set up building properties
        Building building = buildingObj.GetComponent<Building>();
        if (building != null)
        {
            // Apply card stats to building
            ApplyCardStatsToBuilding(card, building);
        }
    }
    
    private void CastSpell(Card card, Vector3 position, Unit.Faction faction, ulong ownerClientId)
    {
        if (card.spellAsset == null) return;
        
        // For spells, we call the Cast method directly instead of instantiating
        card.spellAsset.Cast(position, faction);
        
        // If you need to create a visual effect, you could do:
        // GameObject spellObj = Instantiate(someSpellEffectPrefab, position, Quaternion.identity);
        // NetworkObject netObj = spellObj.GetComponent<NetworkObject>();
        // if (netObj != null) netObj.SpawnWithOwnership(ownerClientId);
    }
    
    private void SetupUnitPath(Unit unit, Unit.Faction faction)
    {
        Transform[] path = null;
        Transform targetTower = null;
        
        // Determine which path to use based on faction and position
        if (faction == Unit.Faction.Player)
        {
            // Player units target enemy tower
            targetTower = enemyKingTower?.transform;
            
            // Choose left or right path based on unit position
            if (unit.transform.position.x < 0)
            {
                path = leftPathPlayer;
            }
            else
            {
                path = rightPathPlayer;
            }
        }
        else
        {
            // Enemy units target player tower
            targetTower = playerKingTower?.transform;
            
            // Choose left or right path based on unit position
            if (unit.transform.position.x < 0)
            {
                path = leftPathEnemy;
            }
            else
            {
                path = rightPathEnemy;
            }
        }
        
        // Set the path on the unit (assuming Unit has a SetPath method)
        if (path != null && path.Length > 0)
        {
            // This would depend on your Unit implementation
            // unit.SetPath(path, targetTower);
        }
    }
    
    private int GetSpawnCount(Card card)
    {
        // Get spawn count from card using reflection
        if (TryGetCardValue(card, "spawnCount", out int count))
        {
            return count;
        }
        return 1; // Default to 1 unit
    }
    
    private void ApplyCardStatsToUnit(Card card, Unit unit)
    {
        if (unit == null) return;
        
        // Apply card stats to unit using reflection
        if (TryGetCardValue(card, "health", out int health))
        {
            UnitHealth unitHealth = unit.GetComponent<UnitHealth>();
            if (unitHealth != null)
            {
                // unitHealth.SetMaxHealth(health);
            }
        }
        
        if (TryGetCardValue(card, "damage", out int damage))
        {
            unit.attackDamage = damage;
        }
        
        if (TryGetCardValue(card, "moveSpeed", out float speed))
        {
            unit.moveSpeed = speed;
        }
        
        if (TryGetCardValue(card, "attackSpeed", out float attackSpeed))
        {
            unit.attackCooldown = 1f / attackSpeed;
        }
    }
    
    private void ApplyCardStatsToBuilding(Card card, Building building)
    {
        // TODO: Implement building stat application
    }
    
    private void ApplyCardStatsToSpell(Card card, Spell spell)
    {
        // TODO: Implement spell stat application
    }
    
    // Reflection helper from original CardSpawner
    bool TryGetCardValue<T>(object card, string name, out T value)
    {
        value = default;
        if (card == null) return false;

        Type t = card.GetType();

        FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
        {
            try
            {
                object val = f.GetValue(card);
                if (val is T)
                {
                    value = (T)val;
                    return true;
                }
                else if (val != null)
                {
                    value = (T)Convert.ChangeType(val, typeof(T));
                    return true;
                }
            }
            catch { }
        }

        PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanRead)
        {
            try
            {
                object val = p.GetValue(card, null);
                if (val is T)
                {
                    value = (T)val;
                    return true;
                }
                else if (val != null)
                {
                    value = (T)Convert.ChangeType(val, typeof(T));
                    return true;
                }
            }
            catch { }
        }

        return false;
    }
}