using UnityEngine;

public abstract class Spell : ScriptableObject
{
    public string spellName;
    public Sprite icon;
    public int coinCost = 1;
    public float range = 5f;
    public float duration = 2f;
    public abstract void Cast(Vector3 position, Unit.Faction casterFaction);
}

[CreateAssetMenu(menuName = "CR/Spell/DamageSplash")]
public class DamageSplashSpell : Spell
{
    public float damage = 50f;
    public float splashRadius = 3f;
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        // Find all units in splashRadius and apply damage
        var hits = Physics.OverlapSphere(position, splashRadius);
        foreach (var hit in hits)
        {
            var unit = hit.GetComponent<Unit>();
            var health = hit.GetComponent<UnitHealth>();
            if (unit != null && health != null && unit.faction != casterFaction)
            {
                health.TakeDamage(Mathf.RoundToInt(damage));
            }
        }
    }
}

[CreateAssetMenu(menuName = "CR/Spell/Poison")]
public class PoisonSpell : Spell
{
    public float poisonDamagePerSecond = 10f;
    public float poisonRadius = 3f;
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        var hits = Physics.OverlapSphere(position, poisonRadius);
        foreach (var hit in hits)
        {
            var unit = hit.GetComponent<Unit>();
            var health = hit.GetComponent<UnitHealth>();
            if (unit != null && health != null && unit.faction != casterFaction)
            {
                // Poison: damage over time. You may need to implement this in UnitHealth.
                health.StartCoroutine(ApplyPoisonCoroutine(health, poisonDamagePerSecond, duration));
            }
        }

    }

    private System.Collections.IEnumerator ApplyPoisonCoroutine(UnitHealth health, float dps, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && health != null && health.IsAlive)
        {
            health.TakeDamage(Mathf.RoundToInt(dps));
            yield return new WaitForSeconds(1f);
            elapsed += 1f;
        }
    }
}

[CreateAssetMenu(menuName = "CR/Spell/Clone")]
public class CloneSpell : Spell
{
    public GameObject clonePrefab;
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        // Spawn a clone at the position
        GameObject.Instantiate(clonePrefab, position, Quaternion.identity);
    }
}

[CreateAssetMenu(menuName = "CR/Spell/Mirror")]
public class MirrorSpell : Spell
{
    public GameObject mirrorPrefab;
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        // Spawn a mirrored unit (could be last played card, etc.)
        GameObject.Instantiate(mirrorPrefab, position, Quaternion.identity);
    }
}

[CreateAssetMenu(menuName = "CR/Spell/Freeze")]
public class FreezeSpell : Spell
{
    public float freezeRadius = 3f;
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        var hits = Physics.OverlapSphere(position, freezeRadius);
        foreach (var hit in hits)
        {
            var unit = hit.GetComponent<Unit>();
            if (unit != null && unit.faction != casterFaction)
            {
                // Stun: disable movement/attack. You may need to implement this in Unit.
                unit.StartCoroutine(ApplyStunCoroutine(unit, duration));
            }
        }
    }

    private System.Collections.IEnumerator ApplyStunCoroutine(Unit unit, float duration)
    {
        if (unit == null) yield break;
        var agent = unit.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.isStopped = true;
        // Optionally disable attack logic here
        yield return new WaitForSeconds(duration);
        if (agent != null) agent.isStopped = false;
        // Optionally re-enable attack logic here
    }
}
