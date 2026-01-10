# Project

This file explains the project structure and how files are organized.
This file should **never** be updated by an agent.

The project is not organized into folders by type. Instead we group functionality by feature. For example:
- The implementation of a "Fish" entity would be somewhere in the project in a "Fish" folder.
- That "Fish" folder could contain a "Fish.tscn" scene.
- That "Fish" folder could contain audio for the fish.
- That "Fish" folder could contain textures and animations for the fish.
- That "Fish" folder could contain C# logic for the fish or gdscript logic.

Folder at the lowest level can then be organized by type again. For example:
- That "Fish" folder COULD have an "Audio" subfolder.
- That "Fish" folder COULD have a "Textures" subfolder.

## Folders

Top level folders in the project still exist.
All folders in the project start with an uppcer case letter.

- "Game/Boat" is the root folder for all kinds of systems and entities related to the gameplay (domain logic). For example:
    - A "Fish" entity would be part of "Game" and could, for example live in a "Entities" subfolder.
    - A "Boat" entity would be part of "Game" and could, for example, live in an "Entities" subfolder.
    - An "Ocean" system would be part of "Game" and would use the "General Fuild Simulation" from "Core".
- "Game/Core" contains systems that have no ties to the gameplay (domain logic). For example:
    - A "Timer Manager System" would be part of "Game/Core"
    - An "Entity Component System" would be part of "Game/Core"
    - A "Screenshot System" would be part of "Game/Core"
    - A "General Fluid Simulation" would be part of "Game/Core"
- "Game/Utilities" holds utilities used by both "Game" and "Game/Core".

## Files

Files always start with upper case letters.
