using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace BasicMultiplayer.Core
{
    /// <summary>
    /// Controls the initial connection UI. 
    /// Interfaces with the NetworkManager to initialize Host, Client, or Server sessions.
    /// </summary>
    public class NetworkConnectUI : MonoBehaviour
    {
        [Header("Connection Settings")]
        [SerializeField] private string _ipAddress = "127.0.0.1";
        [SerializeField] private ushort _port = 7777;
        
        [Header("UI Settings")]
        [SerializeField] private bool _showDebugUI = true;
        
        private bool _isConnecting;
        private string _statusMessage = "Not Connected";

        private void Start()
        {
            // Subscribe to connection events for status updates
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            }
        }

        private void OnDestroy()
        {
            //Always unsubscribe to prevent memory leaks and null reference errors
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
                NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            }
        }

        /// <summary>
        /// Starts as Host (Server + Client). Use this for the player who creates the game.
        /// </summary>
        public void StartHost()
        {
            if (_isConnecting) 
                return;
            
            _isConnecting = true;
            _statusMessage = "Starting Host...";
            
            //Configure transport before starting
            ConfigureTransport();
            
            bool success = NetworkManager.Singleton.StartHost();
            if (!success)
            {
                _statusMessage = "Failed to start host!";
                _isConnecting = false;
            }
        }

        /// <summary>
        /// Joins as a Client to an existing Host.
        /// </summary>
        public void StartClient()
        {
            if (_isConnecting) return;
            
            _isConnecting = true;
            _statusMessage = $"Connecting to {_ipAddress}:{_port}...";
            
            //Configure transport before connecting
            ConfigureTransport();
            
            bool success = NetworkManager.Singleton.StartClient();
            if (!success)
            {
                _statusMessage = "Failed to start client!";
                _isConnecting = false;
            }
        }

        /// <summary>
        /// Starts as dedicated Server only (no local player).
        /// Useful for testing or dedicated server builds.
        /// </summary>
        public void StartServer()
        {
            if (_isConnecting) return;
            
            _isConnecting = true;
            _statusMessage = "Starting Server...";
            
            ConfigureTransport();
            
            bool success = NetworkManager.Singleton.StartServer();
            if (!success)
            {
                _statusMessage = "Failed to start server!";
                _isConnecting = false;
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        public void Disconnect()
        {
            NetworkManager.Singleton.Shutdown();
            _statusMessage = "Disconnected";
            _isConnecting = false;
        }

        /// <summary>
        /// Updates the UnityTransport configuration with the specified connection details.
        /// </summary>
        private void ConfigureTransport()
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData(_ipAddress, _port);
            }
        }

        private void OnServerStarted()
        {
            _statusMessage = $"Server started on port {_port}";
            Debug.Log($"[NetworkConnectUI] Server started successfully");
        }

        private void OnClientConnected(ulong clientId)
        {
            _isConnecting = false;
            
            //Check if this is the local client
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                _statusMessage = $"Connected! Client ID: {clientId}";
                Debug.Log($"[NetworkConnectUI] Local client connected with ID: {clientId}");
            }
            else
            {
                Debug.Log($"[NetworkConnectUI] Remote client connected with ID: {clientId}");
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                _statusMessage = "Disconnected from server";
                _isConnecting = false;
            }
        }

        private void OnGUI()
        {
            if (!_showDebugUI) 
                return;
            
            GUILayout.BeginArea(new Rect(10, 10, 300, 400));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label("=== Physics Arena 1v1 ===");
            GUILayout.Label($"Status: {_statusMessage}");
            GUILayout.Space(10);
            
            //Only show connection buttons if not connected
            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                GUILayout.Label("IP Address:");
                _ipAddress = GUILayout.TextField(_ipAddress);
                
                GUILayout.Space(5);
                
                if (GUILayout.Button("Start Host (Create Game)", GUILayout.Height(40)))
                {
                    StartHost();
                }
                
                if (GUILayout.Button("Join as Client", GUILayout.Height(40)))
                {
                    StartClient();
                }
                
                GUILayout.Space(10);
                
                if (GUILayout.Button("Start Dedicated Server"))
                {
                    StartServer();
                }
            }
            else
            {
                //Show connected info
                GUILayout.Label($"Mode: {GetConnectionMode()}");
                GUILayout.Label($"Connected Clients: {NetworkManager.Singleton.ConnectedClientsIds.Count}");
                
                GUILayout.Space(10);
                
                if (GUILayout.Button("Disconnect", GUILayout.Height(30)))
                {
                    Disconnect();
                }
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private string GetConnectionMode()
        {
            if (NetworkManager.Singleton.IsHost) 
                return "Host";

            if (NetworkManager.Singleton.IsServer) 
                return "Server";

            if (NetworkManager.Singleton.IsClient) 
                return "Client";

            return "None";
        }
    }
}