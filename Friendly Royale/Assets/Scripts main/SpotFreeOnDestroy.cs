using UnityEngine;

/// <summary>
/// Attach to a building to notify CardSpawner to free a special building spot when destroyed.
/// </summary>
public class SpotFreeOnDestroy : MonoBehaviour
{
    private CardSpawner spawner;
    private int spotIndex;
    private bool initialized = false;

    /// <summary>
    /// Initialize with the spawner and spot index to free.
    /// </summary>
    public void Init(CardSpawner spawner, int spotIndex)
    {
        this.spawner = spawner;
        this.spotIndex = spotIndex;
        initialized = true;
    }

    private void OnDestroy()
    {
        if (initialized && spawner != null)
        {
            spawner.FreeSpecialBuildingSpot(spotIndex, this.gameObject);
        }
    }
}