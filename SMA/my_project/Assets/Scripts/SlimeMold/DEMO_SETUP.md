# Slime Mold Demo Setup Guide

Quick setup guide for tomorrow's demo presentation.

## Quick Start (15 minutes)

### 1. Create Demo Scene

1. In Unity: `File > New Scene`
2. Save as `SlimeMoldDemo.unity` in `Assets/Scenes/`
3. Delete default objects (keep Main Camera)

### 2. Set up Camera

- Select Main Camera
- Set Position: (0, 0, -10)
- Set Projection: Orthographic
- Set Size: 30 (adjustbased on your preference)
- Set Background: Black (#000000)

### 3. Create Slime Mold System

1. Create empty GameObject: "SlimeMoldSystem"
2. Position: (0, 0, 0)
3. Add Component: **Slime**
   - Shader: Drag `Slime.compute` here
   - Width: 512
   - Height: 512
   - Num Agents: 10000
   - Move Speed: 50
   - Diffuse Speed: 10
   - Evaporate Speed: 0.3
   - Keep other defaults

4. Add Component: **SlimeMoldManager**
   - Water Attraction Strength: 10
   - Light Attraction Strength: 10
   - Auto Find Sources: checked
   - Attraction Map Resolution: 512 x 512
   - World Bounds: X=-50, Y=-50, W=100, H=100

5. Add Component: **DemoController**
   - Slime Manager: Drag SlimeMoldSystem
   - Slime Simulation: Drag SlimeMoldSystem
   - Main Camera: Drag Main Camera
   - Source Default Radius: 20
   - Source Default Strength: 1.0

### 4. Create UI Canvas

1. Right click Hierarchy > UI > Canvas
2. Canvas settings:
   - Render Mode: Screen Space - Overlay
   - UI Scale Mode: Scale With Screen Size
   - Reference Resolution: 1920 x 1080

3. Right click Canvas > UI > Panel (name it "ControlPanel")
   - Anchor: Top Right
   - Position: adjust to right side
   - Width: 300
   - Height: 600
   - Color: Semi-transparent black (0, 0, 0, 200)

### 5. Add UI Controls

Inside ControlPanel, create:

**Text Header:**
- Right click ControlPanel > UI > Text - TextMeshPro
- Name: "Title"
- Text: "SLIME MOLD DEMO"
- Font Size: 24
- Alignment: Center
- Position at top

**Sliders** (add these in order):

For each slider:
- Right click > UI > Slider
- Set Min/Max values
- Connect to DemoController

1. **Agent Count** (disabled for now, note in UI)
   - Name: "AgentCountSlider"
   - Min: 5000, Max: 100000, Value: 10000

2. **Water Strength**
   - Name: "WaterStrengthSlider"
   - Min: 0, Max: 100, Value: 10

3. **Light Strength**
   - Name: "LightStrengthSlider"
   - Min: 0, Max: 100, Value: 10

4. **Move Speed** (disabled for now)
   - Name: "MoveSpeedSlider"
   - Min: 10, Max: 200, Value: 50

5. **Evaporate Speed** (disabled for now)
   - Name: "EvaporateSpeedSlider"
   - Min: 0.1, Max: 2.0, Value: 0.3

For each slider, add a Text label above it showing the parameter name and value.

**Preset Buttons:**

Add 3 buttons:
- Right click > UI > Button - TextMeshPro

1. "Cooperation" button
   - OnClick: DemoController.SetCooperationPreset()

2. "Competition" button
   - OnClick: DemoController.SetCompetitionPreset()

3. "Fluid" button
   - OnClick: DemoController.SetFluidPreset()

**FPS Counter:**
- Text - TextMeshPro at bottom
- Name: "FPSText"

**Instructions Panel:**

Add another panel with text:
```
LEFT CLICK - Place Water Source (blue)
RIGHT CLICK - Place Light Source (yellow)
MIDDLE CLICK - Remove Nearby Sources
SPACE - Reset Simulation
```

### 6. Connect UI to DemoController

Select SlimeMoldSystem:
- In DemoController component:
  - Drag each slider to corresponding field
  - Drag each text label to corresponding text field
  - Drag FPSText to fps field

### 7. Test

1. Press Play
2. Left click to place water sources (blue gizmos)
3. Right click to place light sources (yellow gizmos)
4. Watch slime move toward both types
5. Adjust sliders to see behavior change
6. Try presets

## Controls Summary

- **Left Click**: Place water source
- **Right Click**: Place light source
- **Middle Click**: Remove source
- **Space**: Reset

## Presets Explained

**Cooperation** (Water:15, Light:15)
- Balanced attraction to both resources
- Demonstrates non-hierarchical navigation
- Multiple pathways emerge

**Competition** (Water:50, Light:5)
- Strongly prioritizes water
- Shows focused optimization
- Compares to traditional pathfinding

**Fluid** (Water:10, Light:30)
- Light-seeking behavior
- Demonstrates flexible response
- Shows identity fluidity

## Demo Talking Points

### The "Queer" Aspect

Traditional pathfinding (A*, Dijkstra):
- One "best" path
- Hierarchical (priority queue)
- Goal from start
- Binary success/failure

Slime Mold Jamming Algorithm:
- **Multiple valid paths** emerge
- **Non-hierarchical** - agents balance competing desires without preset priority
- **Process over product** - pathway formation is valuable, not just destination
- **Fluid identity** - behavior shifts based on environmental context
- **Collective action** - trails help others, distributed intelligence

### Key Demo Moments

1. **Start with empty scene** - show agents spawning
2. **Place single water source** - watch convergence
3. **Add light source on opposite side** - watch splitting/balancing
4. **Adjust sliders live** - show non-deterministic response
5. **Try presets** - contrast cooperation vs competition

### Narrative

"Unlike A* which asks 'what is THE optimal route?', the Slime Mold Jamming Algorithm asks 'how do communities navigate shared space with multiple, competing desires?' This is queer because it refuses singular optimization in favor of collective thriving through non-hierarchical, embodied exploration."

## Troubleshooting

**Nothing appears:**
- Check Slime.compute is assigned
- Check World Bounds contains camera view
- Check camera is orthographic

**No attraction:**
- Increase strength sliders to 50-100
- Make sure Auto Find Sources is checked
- Check sources have showGizmo enabled (you should see colored spheres)

**Performance issues:**
- Reduce Num Agents to 5000
- Reduce resolution to 256x256
- Increase Evaporate Speed to 1.0

**Sources not placing:**
- Check DemoController has references set
- Check camera is assigned
- Try adjusting camera position

## Advanced: Visual Polish

If time allows:

1. **Glow effects on sources:**
   - Add Light2D components to source GameObjects
   - Color: Blue for water, Yellow for light
   - Intensity: 0.5

2. **Colored trails:**
   - Modify trail rendering (requires shader work)
   - Blue tint for water-seeking, yellow for light-seeking

3. **Particles at sources:**
   - Add ParticleSystem to source prefabs
   - Small, slow particles
   - Matching colors

## Files Modified/Created

- NEW: `LightSource.cs`
- NEW: `DemoController.cs`
- NEW: `DEMO_SETUP.md` (this file)
- MODIFIED: `Slime.compute` (dual attraction)
- MODIFIED: `SlimeMoldManager.cs` (supports both sources)

## Estimated Setup Time

- Scene creation: 5 min
- UI setup: 8 min
- Testing/tuning: 2 min
- **Total: 15 minutes**
