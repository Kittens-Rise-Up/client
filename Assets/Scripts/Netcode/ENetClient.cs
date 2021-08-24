/*
 * Kittens Rise Up is a long term progression MMORPG.
 * Copyright (C) 2021  valkyrienyanko
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 * 
 * Contact valkyrienyanko by joining the Kittens Rise Up discord at
 * https://discord.gg/cDNf8ja or email sebastianbelle074@protonmail.com
 */

using ENet;
using EventType = ENet.EventType;  // fixes CS0104 ambigous reference between the same thing in UnityEngine
using Event = ENet.Event;          // fixes CS0104 ambigous reference between the same thing in UnityEngine

using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

using Common.Networking.Packet;
using Common.Networking.IO;
using TMPro;
using KRU.Game;

namespace KRU.Networking 
{
    public class ENetClient : MonoBehaviour
    {
        // Unity Inspector Variables
        public string ip = "127.0.0.1";
        public ushort port = 25565;

        public Transform menuTranform;
        private UIMenu menuScript;

        public Transform loginTransform;
        private UILogin loginScript;

        public Transform terminalTransform;
        private UITerminal terminalScript;

        public Transform gameTransform;
        private KRUGame gameScript;
        private Player player;

        // Non-Inspector
        public const int CLIENT_VERSION_MAJOR = 0;
        public const int CLIENT_VERSION_MINOR = 1;
        public const int CLIENT_VERSION_PATCH = 0;

        private const int PACKET_SIZE_MAX = 1024;

        private readonly ConcurrentQueue<UnityInstructions> unityInstructions = new ConcurrentQueue<UnityInstructions>(); // Need a way to communicate with the Unity thread from the ENet thread
        private readonly ConcurrentQueue<ENetInstructionOpcode> ENetInstructions = new ConcurrentQueue<ENetInstructionOpcode>(); // Need a way to communicate with the ENet thread from the Unity thread
        private readonly ConcurrentQueue<ClientPacket> outgoing = new ConcurrentQueue<ClientPacket>(); // The packets that are sent to the server

        private const byte channelID = 0; // The channel all networking traffic will be going through
        private const int maxFrames = 30; // The games FPS cap

        private readonly uint pingInterval = 1000; // Pings are used both to monitor the liveness of the connection and also to dynamically adjust the throttle during periods of low traffic so that the throttle has reasonable responsiveness during traffic spikes.
        private readonly uint timeout = 5000; // Will be ignored if maximum timeout is exceeded
        private readonly uint timeoutMinimum = 5000; // The timeout for server not sending the packet to the client sent from the server
        private readonly uint timeoutMaximum = 5000; // The timeout for server not receiving the packet sent from the client

        private Peer peer;

        private Thread workerThread;
        private bool runningNetCode;
        private bool tryingToConnect;
        private bool connectedToServer;

        private TMP_InputField inputField;

        private void Start()
        {
            Application.targetFrameRate = maxFrames;
            Application.runInBackground = true;
            DontDestroyOnLoad(gameObject);

            menuScript = menuTranform.GetComponent<UIMenu>();
            loginScript = loginTransform.GetComponent<UILogin>();
            terminalScript = terminalTransform.GetComponent<UITerminal>();
            gameScript = gameTransform.GetComponent<KRUGame>();
            player = gameScript.Player;

            // Make sure queues are completely drained before starting
            if (outgoing != null) while (outgoing.TryDequeue(out _)) ;
            if (unityInstructions != null) while (unityInstructions.TryDequeue(out _)) ;
            if (ENetInstructions != null) while (ENetInstructions.TryDequeue(out _)) ;
        }

        public void Connect() 
        {
            if (tryingToConnect || connectedToServer)
                return;

            tryingToConnect = true;
            workerThread = new Thread(ThreadWorker);
            workerThread.Start();
        }

        public void Disconnect() 
        {
            ENetInstructions.Enqueue(ENetInstructionOpcode.CancelConnection);
        }

        public bool IsConnected() => connectedToServer;

        private void ThreadWorker() 
        {
            Library.Initialize();

            using Host client = new Host();
            var address = new Address();
            address.SetHost(ip);
            address.Port = port;
            client.Create();

            peer = client.Connect(address);
            peer.PingInterval(pingInterval);
            peer.Timeout(timeout, timeoutMinimum, timeoutMaximum);
            Debug.Log("Attempting to connect...");

            runningNetCode = true;
            while (runningNetCode)
            {
                var polled = false;

                // ENet Instructions (from Unity Thread)
                while (ENetInstructions.TryDequeue(out ENetInstructionOpcode result))
                {
                    if (result == ENetInstructionOpcode.CancelConnection)
                    {
                        Debug.Log("Cancel connection");
                        connectedToServer = false;
                        tryingToConnect = false;
                        runningNetCode = false;
                        break;
                    }
                }

                // Sending data
                while (outgoing.TryDequeue(out ClientPacket clientPacket)) 
                {
                    switch ((ClientPacketOpcode)clientPacket.Opcode) 
                    {
                        case ClientPacketOpcode.Login:
                            Debug.Log("Sending login request to game server..");

                            Send(clientPacket, PacketFlags.Reliable);

                            break;
                        case ClientPacketOpcode.PurchaseItem:
                            Debug.Log("Sending purchase item request to game server..");

                            Send(clientPacket, PacketFlags.Reliable);

                            break;
                    }
                }

                // Receiving Data
                while (!polled)
                {
                    if (client.CheckEvents(out Event netEvent) <= 0)
                    {
                        if (client.Service(15, out netEvent) <= 0)
                            break;

                        polled = true;
                    }

                    switch (netEvent.Type)
                    {
                        case EventType.None:
                            Debug.Log("Nothing");
                            break;

                        case EventType.Connect:
                            // Successfully connected to the game server
                            Debug.Log("Client connected to game server");

                            // Send login request
                            var clientPacket = new ClientPacket((byte)ClientPacketOpcode.Login, new WPacketLogin { 
                                Username = loginScript.username,
                                VersionMajor = CLIENT_VERSION_MAJOR,
                                VersionMinor = CLIENT_VERSION_MINOR,
                                VersionPatch = CLIENT_VERSION_PATCH
                            });

                            outgoing.Enqueue(clientPacket);

                            // Keep track of networking logic
                            tryingToConnect = false;
                            connectedToServer = true;
                            break;

                        case EventType.Disconnect:
                            Debug.Log(netEvent.Data);
                            Debug.Log("Client disconnected from server");
                            connectedToServer = false;
                            break;

                        case EventType.Timeout:
                            Debug.Log("Client connection timeout to game server");
                            tryingToConnect = false;
                            connectedToServer = false;
                            unityInstructions.Enqueue(new UnityInstructions(UnityInstructionOpcode.NotifyUserOfTimeout));
                            unityInstructions.Enqueue(new UnityInstructions(UnityInstructionOpcode.LoadSceneForDisconnectTimeout));
                            break;

                        case EventType.Receive:
                            var packet = netEvent.Packet;
                            Debug.Log("Packet received from server - Channel ID: " + netEvent.ChannelID + ", Data length: " + packet.Length);

                            var readBuffer = new byte[PACKET_SIZE_MAX];
                            var packetReader = new PacketReader(readBuffer);
                            //packetReader.BaseStream.Position = 0;

                            netEvent.Packet.CopyTo(readBuffer);

                            var opcode = (ServerPacketOpcode)packetReader.ReadByte();

                            if (opcode == ServerPacketOpcode.LoginResponse) 
                            {
                                var data = new RPacketLogin();
                                data.Read(packetReader);

                                if (data.LoginOpcode == LoginResponseOpcode.VersionMismatch)
                                {
                                    var serverVersion = $"{data.VersionMajor}.{data.VersionMinor}.{data.VersionPatch}";
                                    var clientVersion = $"{CLIENT_VERSION_MAJOR}.{CLIENT_VERSION_MINOR}.{CLIENT_VERSION_PATCH}";

                                    var cmd = new UnityInstructions();
                                    cmd.Set(UnityInstructionOpcode.ServerResponseMessage, 
                                        $"Version mismatch. Server ver. {serverVersion} Client ver. {clientVersion}");

                                    unityInstructions.Enqueue(cmd);
                                }

                                if (data.LoginOpcode == LoginResponseOpcode.LoginSuccess)
                                {
                                    // Load the main game 'scene'
                                    unityInstructions.Enqueue(new UnityInstructions(UnityInstructionOpcode.LoadMainScene));

                                    // Update player values
                                    player.Gold = data.Gold;
                                    player.StructureHuts = data.StructureHut;

                                    unityInstructions.Enqueue(new UnityInstructions (UnityInstructionOpcode.LoginSuccess));
                                }
                            }

                            if (opcode == ServerPacketOpcode.PurchasedItem) 
                            {
                                var data = new RPacketPurchaseItem();
                                data.Read(packetReader);

                                var itemResponseOpcode = data.PurchaseItemResponseOpcode;

                                if (itemResponseOpcode == PurchaseItemResponseOpcode.NotEnoughGold) 
                                {
                                    var cmd = new UnityInstructions();
                                    cmd.Set(UnityInstructionOpcode.LogMessage, $"You do not have enough gold for {(ItemType)data.ItemId}.");

                                    unityInstructions.Enqueue(cmd);

                                    // Update the player gold
                                    player.Gold = data.Gold;
                                }

                                if (itemResponseOpcode == PurchaseItemResponseOpcode.Purchased) 
                                {
                                    var cmd = new UnityInstructions();
                                    cmd.Set(UnityInstructionOpcode.LogMessage, $"Bought {(ItemType)data.ItemId} for 25 gold.");

                                    unityInstructions.Enqueue(cmd);

                                    // Update the player gold
                                    player.Gold = data.Gold;

                                    // Update the items
                                    switch ((ItemType)data.ItemId) 
                                    {
                                        case ItemType.Hut:
                                            player.StructureHuts++;
                                            break;
                                        case ItemType.Farm:
                                            break;
                                    }
                                }
                            }

                            packetReader.Dispose();
                            packet.Dispose();
                            break;
                    }
                }
            }

            client.Flush();
            client.Dispose();

            Library.Deinitialize();
        }

        private void Update()
        {
            if (!runningNetCode)
                return;

            while (unityInstructions.TryDequeue(out UnityInstructions result)) 
            {
                foreach (var cmd in result.Instructions) 
                {
                    if (cmd.Key == UnityInstructionOpcode.NotifyUserOfTimeout) 
                    {
                        loginScript.btnConnect.interactable = true;
                        loginScript.loginFeedbackText.text = "Client connection timeout to game server";
                    }

                    if (cmd.Key == UnityInstructionOpcode.ServerResponseMessage) 
                    {
                        loginScript.loginFeedbackText.text = (string)cmd.Value[0];
                    }

                    if (cmd.Key == UnityInstructionOpcode.LogMessage) 
                    {
                        terminalScript.Log((string)cmd.Value[0]);
                    }

                    if (cmd.Key == UnityInstructionOpcode.LoadSceneForDisconnectTimeout) 
                    {
                        menuScript.LoadTimeoutDisconnectScene();
                        menuScript.gameScript.Player.InGame = false;
                    }

                    if (cmd.Key == UnityInstructionOpcode.LoadMainScene) 
                    {
                        menuScript.FromConnectingToMainScene();
                        loginScript.loginFeedbackText.text = "";
                        loginScript.btnConnect.interactable = true;
                        menuScript.gameScript.Player.InGame = true;
                    }

                    if (cmd.Key == UnityInstructionOpcode.LoginSuccess) 
                    {
                        StartCoroutine(gameScript.GameLoop);
                    }
                }
            }
        }

        public void PurchaseItem(int itemId) 
        {
            var data = new WPacketPurchaseItem { ItemID = (ushort)itemId };
            var clientPacket = new ClientPacket((byte)ClientPacketOpcode.PurchaseItem, data);

            outgoing.Enqueue(clientPacket);
        }

        private void Send(GamePacket gamePacket, PacketFlags packetFlags)
        {
            var packet = default(Packet);
            packet.Create(gamePacket.Data, packetFlags);
            peer.Send(channelID, ref packet);
        }
    }

    public class UnityInstructions 
    {
        public Dictionary<UnityInstructionOpcode, List<object>> Instructions { get; set; }

        public UnityInstructions() 
        {
            Instructions = new Dictionary<UnityInstructionOpcode, List<object>>();
        }

        public UnityInstructions(UnityInstructionOpcode opcode) 
        {
            Instructions = new Dictionary<UnityInstructionOpcode, List<object>>
            {
                [opcode] = null
            };
        }

        public void Set(UnityInstructionOpcode opcode, params object[] data) 
        {
            Instructions[opcode] = new List<object>(data);
        }
    }

    public enum UnityInstructionOpcode
    {
        LoadSceneForDisconnectTimeout,
        LoadMainScene,
        LogMessage,
        ServerResponseMessage,
        NotifyUserOfTimeout,
        LoginSuccess
    }

    public enum ENetInstructionOpcode 
    {
        CancelConnection
    }
}
