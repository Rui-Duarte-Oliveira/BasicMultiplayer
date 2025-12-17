using BasicMultiplayer.Ball;
using BasicMultiplayer.Player;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace BasicMultiplayer.Core
{
    /// <summary>
    /// Server-authoritative manager for the game lifecycle.
    /// Coordinates game states (Waiting, Countdown, Playing, RoundEnd) and synchronizes 
    /// scoring and timers across all clients via NetworkVariables.
    /// </summary>
    public class GameManager : NetworkBehaviour
    {
        public enum GameState
        {
            WaitingForPlayers,  //Lobby - waiting for 2 players
            Countdown,          //Pre-round countdown (3, 2, 1...)
            Playing,            //Active gameplay
            RoundEnd,           //Goal scored - brief pause
            GameEnd             //Match complete - show results
        }

        [Header("Game Settings")]
        [SerializeField] private int _scoreToWin = 3;
        [SerializeField] private float _roundDuration = 120f; //2 minutes
        [SerializeField] private float _countdownDuration = 3f;
        [SerializeField] private float _roundEndPauseDuration = 2f;

        [Header("References")]
        [SerializeField] private ArenaBall _ball;
        [SerializeField] private Transform[] _playerSpawnPoints;

        /// <summary>
        /// Synchronized game state. 
        /// Monitored by clients to handle UI transitions and local gameplay logic.
        /// </summary>
        public NetworkVariable<GameState> CurrentState = new NetworkVariable<GameState>(
            GameState.WaitingForPlayers,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>
        /// Synchronized player scores. Index 0 corresponds to Player 1, Index 1 to Player 2.
        /// Changes are automatically replicated to all clients for UI updates.
        /// </summary>
        public NetworkList<int> PlayerScores;

        /// <summary>
        /// Remaining time in the current round (seconds).
        /// Updates every second on server, syncs to clients.
        /// </summary>
        public NetworkVariable<float> RoundTimeRemaining = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>
        /// Countdown timer for pre-round (3, 2, 1...).
        /// </summary>
        public NetworkVariable<int> CountdownValue = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>
        /// Index of the winning player (-1 if no winner yet).
        /// </summary>
        public NetworkVariable<int> WinnerIndex = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private float _stateTimer;
        private List<PlayerNetworkController> _connectedPlayers = new List<PlayerNetworkController>();
        private int _requiredPlayers = 2;

        public static GameManager Instance { get; private set; }

        private void Awake()
        {
            //Simple singleton - in production, use a more robust pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            //Initialize NetworkList (MUST be done in Awake, before network spawn)
            PlayerScores = new NetworkList<int>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            //Subscribe to state changes for logging/debugging
            CurrentState.OnValueChanged += OnGameStateChanged;

            if (IsServer)
            {
                //Initialize scores
                PlayerScores.Add(0); //Player 0
                PlayerScores.Add(0); //Player 1

                //Subscribe to ball events
                if (_ball != null)
                {
                    _ball.OnGoalScored += HandleGoalScored;
                }

                //Subscribe to player connections
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

                Debug.Log("[GameManager] Server initialized - waiting for players");
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            CurrentState.OnValueChanged -= OnGameStateChanged;

            if (IsServer)
            {
                if (_ball != null)
                {
                    _ball.OnGoalScored -= HandleGoalScored;
                }

                if (NetworkManager.Singleton != null)
                {
                    NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                    NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
                }
            }
        }

        private void Update()
        {
            //Only server runs game logic
            if (!IsServer) 
                return;

            //State machine update
            switch (CurrentState.Value)
            {
                case GameState.WaitingForPlayers:
                    UpdateWaitingState();
                    break;

                case GameState.Countdown:
                    UpdateCountdownState();
                    break;

                case GameState.Playing:
                    UpdatePlayingState();
                    break;

                case GameState.RoundEnd:
                    UpdateRoundEndState();
                    break;

                case GameState.GameEnd:
                    //Do nothing - wait for restart
                    break;
            }
        }

        private void UpdateWaitingState()
        {
            //Check if we have enough players
            if (_connectedPlayers.Count >= _requiredPlayers)
            {
                StartCountdown();
            }
        }

        private void UpdateCountdownState()
        {
            _stateTimer -= Time.deltaTime;

            //Update countdown value (rounded up)
            int newCountdown = Mathf.CeilToInt(_stateTimer);
            if (newCountdown != CountdownValue.Value && newCountdown >= 0)
            {
                CountdownValue.Value = newCountdown;
                OnCountdownTickClientRpc(newCountdown);
            }

            if (_stateTimer <= 0f)
            {
                StartRound();
            }
        }

        private void UpdatePlayingState()
        {
            _stateTimer -= Time.deltaTime;
            RoundTimeRemaining.Value = Mathf.Max(0f, _stateTimer);

            //Time expired - draw or sudden death
            if (_stateTimer <= 0f)
            {
                EndRoundByTimeout();
            }
        }

        private void UpdateRoundEndState()
        {
            _stateTimer -= Time.deltaTime;

            if (_stateTimer <= 0f)
            {
                //Check for game winner
                if (CheckForGameWinner(out int winnerIndex))
                {
                    EndGame(winnerIndex);
                }
                else
                {
                    //Start next round
                    StartCountdown();
                }
            }
        }

        private void StartCountdown()
        {
            CurrentState.Value = GameState.Countdown;
            _stateTimer = _countdownDuration;
            CountdownValue.Value = Mathf.CeilToInt(_countdownDuration);

            //Reset player positions
            ResetPlayerPositions();

            //Reset ball
            _ball?.ResetForNewRound();
            _ball?.Freeze(); //Keep frozen during countdown

            Debug.Log("[GameManager] Starting countdown...");
        }

        private void StartRound()
        {
            CurrentState.Value = GameState.Playing;
            _stateTimer = _roundDuration;
            RoundTimeRemaining.Value = _roundDuration;

            //Activate ball with starting impulse
            _ball?.ResetForNewRound();
            _ball?.ApplyStartingImpulse();

            OnRoundStartClientRpc();
            Debug.Log("[GameManager] Round started!");
        }

        private void EndRoundByTimeout()
        {
            //Time's up - could implement sudden death or draw
            CurrentState.Value = GameState.RoundEnd;
            _stateTimer = _roundEndPauseDuration;

            _ball?.Freeze();

            OnRoundEndClientRpc(-1); //-1 = timeout/draw
            Debug.Log("[GameManager] Round ended by timeout");
        }

        private void EndGame(int winnerIndex)
        {
            CurrentState.Value = GameState.GameEnd;
            WinnerIndex.Value = winnerIndex;

            OnGameEndClientRpc(winnerIndex);
            Debug.Log($"[GameManager] Game Over! Player {winnerIndex} wins!");
        }

        /// <summary>
        /// Called when the ball enters a goal zone.
        /// Server-only handler.
        /// </summary>
        private void HandleGoalScored(int scoringPlayerIndex)
        {
            if (CurrentState.Value != GameState.Playing) return;

            //Update score
            if (scoringPlayerIndex >= 0 && scoringPlayerIndex < PlayerScores.Count)
            {
                PlayerScores[scoringPlayerIndex] = PlayerScores[scoringPlayerIndex] + 1;
            }

            //Transition to round end
            CurrentState.Value = GameState.RoundEnd;
            _stateTimer = _roundEndPauseDuration;

            //Broadcast goal to all clients
            OnGoalScoredClientRpc(scoringPlayerIndex, PlayerScores[0], PlayerScores[1]);

            Debug.Log($"[GameManager] Goal! Player {scoringPlayerIndex} scores. Score: {PlayerScores[0]} - {PlayerScores[1]}");
        }

        private void OnClientConnected(ulong clientId)
        {
            //Wait a frame for player to spawn
            StartCoroutine(RegisterPlayerDelayed(clientId));
        }

        private System.Collections.IEnumerator RegisterPlayerDelayed(ulong clientId)
        {
            yield return new WaitForSeconds(0.5f);

            //Find the player's NetworkObject
            foreach (var player in FindObjectsByType<PlayerNetworkController>(FindObjectsSortMode.None))
            {
                if (player.OwnerClientId == clientId && !_connectedPlayers.Contains(player))
                {
                    _connectedPlayers.Add(player);
                    Debug.Log($"[GameManager] Player registered. Total: {_connectedPlayers.Count}");
                    break;
                }
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            //Remove disconnected player
            _connectedPlayers.RemoveAll(p => p == null || p.OwnerClientId == clientId);

            Debug.Log($"[GameManager] Player disconnected. Remaining: {_connectedPlayers.Count}");

            // Return to waiting if not enough players
            if (_connectedPlayers.Count < _requiredPlayers && CurrentState.Value != GameState.WaitingForPlayers)
            {
                CurrentState.Value = GameState.WaitingForPlayers;
                _ball?.Freeze();
            }
        }

        private void ResetPlayerPositions()
        {
            for (int i = 0; i < _connectedPlayers.Count; i++)
            {
                var player = _connectedPlayers[i];
                if (player != null && i < _playerSpawnPoints.Length)
                {
                    Vector3 spawnPos = _playerSpawnPoints[player.PlayerIndex.Value].position;
                    player.ResetPositionClientRpc(spawnPos);
                }
            }
        }

        private bool CheckForGameWinner(out int winnerIndex)
        {
            winnerIndex = -1;

            for (int i = 0; i < PlayerScores.Count; i++)
            {
                if (PlayerScores[i] >= _scoreToWin)
                {
                    winnerIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Broadcasts a game event to all clients to trigger local UI and audio-visual feedback.
        /// </summary>
        [ClientRpc]
        private void OnCountdownTickClientRpc(int value)
        {
            //Play countdown sound, update UI
            Debug.Log($"[GameManager] Countdown: {value}");
        }

        [ClientRpc]
        private void OnRoundStartClientRpc()
        {
            //Play round start sound, enable controls
            Debug.Log("[GameManager] ROUND START!");
        }

        [ClientRpc]
        private void OnGoalScoredClientRpc(int scoringPlayer, int score0, int score1)
        {
            //Play goal sound, show celebration
            Debug.Log($"[GameManager] GOAL! Player {scoringPlayer} scores! ({score0} - {score1})");
        }

        [ClientRpc]
        private void OnRoundEndClientRpc(int winningPlayer)
        {
            //Show round end UI
            Debug.Log($"[GameManager] Round Over. Winner: {(winningPlayer >= 0 ? $"Player {winningPlayer}" : "Draw")}");
        }

        [ClientRpc]
        private void OnGameEndClientRpc(int winnerIndex)
        {
            //Show game over screen
            Debug.Log($"[GameManager] GAME OVER! Player {winnerIndex} WINS!");
        }

        private void OnGameStateChanged(GameState previousValue, GameState newValue)
        {
            Debug.Log($"[GameManager] State changed: {previousValue} → {newValue}");
        }

        /// <summary>
        /// Requests a game restart from the server. 
        /// Configured to allow any connected client to trigger a reset regardless of ownership.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RestartGameRpc()
        {
            //Reset scores
            for (int i = 0; i < PlayerScores.Count; i++)
            {
                PlayerScores[i] = 0;
            }

            WinnerIndex.Value = -1;

            //Start new game
            if (_connectedPlayers.Count >= _requiredPlayers)
            {
                StartCountdown();
            }
            else
            {
                CurrentState.Value = GameState.WaitingForPlayers;
            }

            Debug.Log("[GameManager] Game restarted");
        }

        private void OnGUI()
        {
            //Simple debug UI
            //Replace with proper UI later
            GUILayout.BeginArea(new Rect(Screen.width - 220, 10, 210, 200));
            GUILayout.BeginVertical("box");

            GUILayout.Label($"State: {CurrentState.Value}");

            if (PlayerScores != null && PlayerScores.Count >= 2)
            {
                GUILayout.Label($"Score: {PlayerScores[0]} - {PlayerScores[1]}");
            }

            if (CurrentState.Value == GameState.Playing)
            {
                GUILayout.Label($"Time: {Mathf.CeilToInt(RoundTimeRemaining.Value)}s");
            }
            else if (CurrentState.Value == GameState.Countdown)
            {
                GUILayout.Label($"Starting in: {CountdownValue.Value}");
            }
            else if (CurrentState.Value == GameState.GameEnd)
            {
                GUILayout.Label($"WINNER: Player {WinnerIndex.Value}!");

                if (GUILayout.Button("Restart Game"))
                {
                    RestartGameRpc();
                }
            }

            GUILayout.Label($"Players: {_connectedPlayers.Count}/{_requiredPlayers}");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}