# BasicMultiplayer Arena (Network Prototype)

This repository serves as a technical prototype demonstrating the implementation of **Unity Netcode for GameObjects (NGO)** in a 1v1 context. 

The primary objective of this project was to explore server-authoritative architecture and state synchronization using Unity's modern networking stack, moving beyond local-only gameplay logic.

> **Status:** 🚧 Work in Progress 🚧  
> This project is currently in active development. Features may be incomplete, and bugs are expected.

## 🎮 Project Overview

Two players compete in an enclosed arena to score goals against each other. The game utilizes a server-authoritative architecture where the server manages the game state, ball physics, and score validation to prevent cheating and ensure synchronization.

### Key Features
* **Server-Authoritative Loop:** Centralized game manager controls the flow: `WaitingForPlayers` → `Countdown` → `Playing` → `RoundEnd`.
* **Network Synchronization:** Real-time syncing of player positions, ball physics, and game states using `NetworkVariable` and `NetworkList`.
* **Score & Win Conditions:** First player to reach the target score (default: 3) wins the match.
* **Input System:** Integrated with the new Unity Input System for responsive player control.
* **Dev Tools:** Configured with **ParrelSync** to easily test Host/Client interactions on a single machine.

## 📅 Roadmap (To-Do)

* [ ] **UI Overhaul:** Replace the temporary `OnGUI` debug display with a proper Unity UI (Canvas) system for the Main Menu, Scoreboard, and Game Over screens.
* [ ] **Physics Improvements:** Implement client-side prediction and reconciliation to smooth out ball movement over higher latency.
* [ ] **Match Timer:** Finalize the visual representation of the round timer.
* [ ] **Visual Polish:** Add particle effects for goals and better player visual feedback.

## 🐛 Known Issues

* **Connection Jitter:** Player movement may jitter slightly on unstable connections (reconciliation pending).
* **State Reset:** Occasionally, the ball position may not reset perfectly if the round ends during a high-velocity collision.
* **UI:** The current UI is developer-programmer art and is intended for debugging purposes only.
