using UnityEngine;

[CreateAssetMenu(fileName = "CalamityProfile_New", menuName = "Calamity/Object Profile")]
public class CalamityProfile : ScriptableObject
{
    [TextArea(2, 4)]
    public string description = "Describe this profile...";

    public CalamityObjectSpawner.SpawnParameters parameters = new CalamityObjectSpawner.SpawnParameters();
}