# BasicMultiplayer (Network Prototype)

This repository serves as a technical prototype demonstrating the implementation of **Unity Netcode for GameObjects (NGO)** in a 1v1 context. 

The primary objective of this project was to explore server-authoritative architecture and state synchronization using Unity's modern networking stack, moving beyond local-only gameplay logic.

## 🎯 Project Objectives

This project exists to validate and demonstrate competency in the following networking concepts:

* **Server-Authoritative Architecture:** Establishing a "Source of Truth" where the server validates actions rather than trusting the client blindly.
* **State Synchronization:** Implementing `NetworkVariable` and `NetworkTransform` to ensure consistent game state across connected clients.
* **Remote Procedure Calls (RPCs):** Utilizing `[ServerRpc]` for client-to-server requests (inputs) and `[ClientRpc]` for server-to-client updates (events).
* **Connection Lifecycle:** Managing the NetworkManager state to handle Host vs. Client connection logic.

## ⚙️ Technical Implementation

The prototype focuses on the core mechanics required for a functional networked session:

* **NetworkManager Configuration:** Setup of the transport layer (Unity Transport) and prefab registration.
* **Player Replication:** Handling player spawning and ownership assignment upon connection.
* **Movement Sync:** Logic to handle input processing on the owner's client, sending input data to the server, and replicating the resulting transform to other clients.
* **Lag Compensation (Basic):** Implementation of interpolation settings to smooth out movement for remote clients.

---

*This project is part of my development portfolio demonstrating backend and networking capabilities within the Unity ecosystem.*
