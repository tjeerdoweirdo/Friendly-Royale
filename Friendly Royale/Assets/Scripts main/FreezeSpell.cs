using UnityEngine;

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