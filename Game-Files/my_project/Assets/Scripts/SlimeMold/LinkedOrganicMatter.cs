using UnityEngine;
using System.Collections.Generic;

public class LinkedOrganicMatter : MonoBehaviour
{
    [Header("Link Settings")]
    public List<OrganicMatter> linkedParts = new List<OrganicMatter>();

    private OrganicMatter myOrganicMatter;
    private static bool isSyncing = false; // Static ensures only one sync happens globally at a time

    void Awake() => myOrganicMatter = GetComponent<OrganicMatter>();

    public void TakeLinkedDamage(float amount)
    {
        if (isSyncing) return;
        isSyncing = true;

        // Damage self
        if (myOrganicMatter != null)
            myOrganicMatter.TakeDecompositionDamage(amount);

        // Damage all siblings
        foreach (var part in linkedParts)
        {
            if (part != null && part != myOrganicMatter)
                part.TakeDecompositionDamage(amount);
        }

        isSyncing = false;
    }
}