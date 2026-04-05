# Ad Natura - Wardens of the Wild

---

**Developer Names:**
* Felix Hurst
* Marcos Hernandez-Rivero
* BoWen Liu
* Andy Liang

**Supervisor Names:**
* Dr. Stephen Kelly
* Michelle Bunton

**Date of Project Start:** September 2025

**Final Project EXPO Presentation:** April 2026

---

## Project Overview

This project is a Capstone design project for SFWRENG 4G06, planned and developed by our team. Our project delivers a unique and exciting take on the classic 2D puzzle-platformer genre by introducing smart, interactable environments, including slime mold life and destructible terrain. The story follows a young girl's journey in an apocalyptic world, on a mission to restore the environment.

The primary goal of this project is to offer an experience of humans interacting with the non-human, showcasing the queer and unexplainable characteristics of the slime mold. We intend to elicit emotions of curiosity, empathy, and hope from the player. Ultimately, we wish to send a message that our precious environment must be protected.

---

## Key Features

**Player tools:**
* Water Shooter - For helping slime decompose wood
* Impact Rounds - For breaking brittle obstacles
* Wind Fan - For blowing slime spores

**Slime mold mechanics:**
* Water attraction
* Wood decomposition
* Spore reproduction
* Light aversion
* Death when contacting pollution sources
* Coating walls, making them climbable

**Other features:**
* Destructible terrain / objects
* Physically simulated water flow
* Physically simulated wind and particle blowing

---

## Source Code and Asset Directory

Our source code and assets are located in the `src` folder. Because Unity projects are very large
in file size, we opted to include only the files that are specific to our project, excluding the
Unity library folder among other unnecessary folders.

**Most importantly, our source code, in the form of C# scripts, is located at `src/Assets/Scripts/`.**

### `src` Directory Breakdown

**Assets:**
* **Animations:** All animation files
* **Backgrounds:** Background PNG files for each level, and for a few environment objects
* **Configs:** Configurations for certain objects
* **Materials:** Physics materials for certain objects
* **Prefabs:** Prefabricated objects
* **Resources:** Contains music and sound effect files, as well as the slime mold's compute shader
* **Scenes:** Contains the Unity scenes for each of the game's cutscenes and levels
* **Scripts:** All C# scripts used by the game, including controllers, managers and progression logic
* **Settings:** Settings for certain assets
* **Shaders:** Shaders for certain objects
* **Sprites:** Sprite PNG files for most game objects
* **TextMesh Pro:** Data for TextMesh Pro UI objects, including fonts and other resources
* Miscellaneous metadata and other data files

Note that all *.meta files are simply metadata files used by the Unity Editor.

**Packages:** Basic package data.

**ProjectSettings:** Unity project settings, including product name, player input configurations, resources to include in builds, tags and layers, and more.

---

## Documentation Directory

Our documentation is located in the `docs` folder. This includes all deliverables for the SFWRENG 4G06 course. For more details, please review the README.md files inside the folder.
