# Slime Mold Implementation

Physarum-based slime mold that moves toward water sources. Uses GPU compute shaders for performance.

Based on Sopiro/Slime-Simulation (MIT), modified for water attraction.

## Files

- `Slime.cs` - Core simulation from Sopiro
- `Slime.compute` - Modified with water attraction logic
- `SlimeMoldManager.cs` - Integrates water sources with simulation
- `WaterSource.cs` - Marks GameObjects as attraction points

## Setup

1. Create empty GameObject "SlimeMoldSystem"
2. Add Slime + SlimeMoldManager components
3. Assign Slime.compute to Shader field
4. Create test water objects and add WaterSource component

**Slime settings:**
- Shader: Slime.compute
- Width/Height: 512
- Num Agents: 10000 (start low, increase later)
- Move Speed: 50
- Other params: keep defaults

**SlimeMoldManager:**
- Attraction Strength: 10
- Auto Find Water Sources: checked
- World Bounds: match your level size

**WaterSource (on each water object):**
- Attraction Strength: 1.0
- Attraction Radius: 20

## Testing

Press play. Slime spawns at center and moves toward water sources.

**If nothing shows:** check Slime.compute is assigned, world bounds are correct
**If no attraction:** increase Attraction Strength to 50-100
**If laggy:** reduce agents to 5000 or resolution to 256x256

## Recommended Values

**Fast testing:**
- 5k agents, 256x256, attraction 20

**Production:**
- 50k+ agents, 512x512 or 1024x1024, attraction 10

## Integration with Debris System

When water is released:
```csharp
GameObject waterObj = Instantiate(waterPrefab, position, Quaternion.identity);
waterObj.AddComponent<WaterSource>();
waterObj.GetComponent<WaterSource>().attractionRadius = 25f;
```

Water sources auto-update every frame.

## TODO
- Test with static water sources
- Add light/heat sources later (similar pattern)
- Hook into debris system
- Make slime pathways traversable

## Notes
- Based on Sopiro/Slime-Simulation (MIT)
- Uses GPU compute shaders
- Agents sense in 3 directions and steer toward water + trails
