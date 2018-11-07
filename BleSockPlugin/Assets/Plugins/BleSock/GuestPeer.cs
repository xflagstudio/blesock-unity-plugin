using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

using UnityEngine;

namespace BleSock
{
    public class GuestPeer : PeerBase
    {
        // Events

        public event Action<string, int> onDiscover;
        public event Action onConnect;
        public event Action onDisconnect;

        // Properties

        public override bool IsBluetoothEnabled
        {
            get
            {
                if (mImplementation != null)
                {
                    return mImplementation.IsBluetoothEnabled;
                }

                return false;
            }
        }

        public override int LocalPlayerId
        {
            get
            {
                return mLocalPlayerId;
            }
        }

        // Methods

        public void Initialize(string protocolIdentifier, string playerName, SynchronizationContext synchronizationContext = null)
        {
            if (mImplementation != null)
            {
                throw new Exception("Already called initialize");
            }

            InitializeInternal(protocolIdentifier, playerName, synchronizationContext);

            mImplementation = new Central();
            mImplementation.onBluetoothRequire += OnBluetoothRequire;
            mImplementation.onReady += OnReady;
            mImplementation.onFail += OnFail;
            mImplementation.onDiscover += OnDiscover;
            mImplementation.onConnect += OnConnect;
            mImplementation.onDisconnect += OnDisconnect;
            mImplementation.onReceive += OnReceive;

            if (!mImplementation.Initialize(mServiceUUID, mUploadUUID, mDownloadUUID))
            {
                mImplementation.Dispose();
                mImplementation = null;
                throw new Exception("Failed to initialize");
            }
        }

        public void StartScan()
        {
            if (!IsReady)
            {
                throw new Exception("Not ready");
            }

            if (mState == State.Scan)
            {
                return;
            }

            if (mImplementation.StartScan())
            {
                mState = State.Scan;
            }
            else
            {
                throw new Exception("Failed to start scan");
            }
        }

        public void StopScan()
        {
            if (!IsReady)
            {
                throw new Exception("Not ready");
            }

            if (mState == State.Scan)
            {
                mImplementation.StopScan();
                mState = State.Ready;
            }
        }

        public void Connect(int deviceId)
        {
            if (!IsReady)
            {
                throw new Exception("Not ready");
            }

            if ((mState != State.Ready) && (mState != State.Scan))
            {
                throw new Exception("Already connection started");
            }

            StopScan();

            if (mImplementation.Connect(deviceId))
            {
                mState = State.Connect;
            }
            else
            {
                throw new Exception("Failed to connect");
            }
        }

        public void Disconnect()
        {
            if (!IsReady)
            {
                throw new Exception("Not ready");
            }

            mImplementation.Disconnect();
        }

        public override void Send(byte[] message, int messageSize, int receiver)
        {
            int address = PrepareSend(message, messageSize, receiver);
            if ((address & ~LocalPlayerId) != 0)
            {
                if (!mImplementation.Send(message, messageSize, address & ~LocalPlayerId))
                {
                    throw new Exception("Failed to send user message");
                }
            }

            if ((address & LocalPlayerId) != 0)
            {
                InvokeOnReceive(message, messageSize, LocalPlayerId);
            }
        }

        public override void Cleanup()
        {
            onDiscover = null;
            onConnect = null;
            onDisconnect = null;

            if (mImplementation != null)
            {
                mImplementation.Dispose();
                mImplementation = null;
            }

            mState = State.Invalid;
            mLocalPlayerId = 0;

            base.Cleanup();
        }

        // Internal

        private Central mImplementation;

        private enum State
        {
            Invalid,
            Ready,
            Scan,
            Connect,
            Authenticate,
            Online,
        }

        private State mState;
        private int mLocalPlayerId = 0;


        private void OnBluetoothRequire()
        {
            Post(() =>
            {
                InvokeOnBluetoothRequire();
            });
        }

        private void OnReady()
        {
            Post(() =>
            {
                mReady = true;

                InvokeOnReady();
            });
        }

        private void OnFail()
        {
            Post(() =>
            {
                if (mState != State.Invalid)
                {
                    mState = State.Ready;
                }

                InvokeOnFail();
            });
        }

        private void OnDiscover(string deviceName, int deviceId)
        {
            Post(() =>
            {
                if (mState != State.Scan)
                {
                    return;
                }

                if (onDiscover != null)
                {
                    try
                    {
                        onDiscover(deviceName, deviceId);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            });
        }

        private void OnConnect()
        {
            Post(() =>
            {
                if (mState != State.Connect)
                {
                    return;
                }

                mState = State.Authenticate;
            });
        }

        private void OnDisconnect()
        {
            Post(() =>
            {
                if (mState == State.Invalid)
                {
                    return;
                }

                bool disconnected = (mState == State.Online);

                mState = State.Ready;
                mLocalPlayerId = 0;
                mPlayers.Clear();

                if (disconnected)
                {
                    if (onDisconnect != null)
                    {
                        try
                        {
                            onDisconnect();
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
                else
                {
                    OnFail();
                }
            });
        }

        private void OnReceive(byte[] message, int sender)
        {
            Post(() =>
            {
                if (mState == State.Invalid)
                {
                    return;
                }

                if (sender == 0)
                {
                    var buffer = new Buffer(message, message.Length);
                    byte msgType = buffer.ReadByte();

                    switch (msgType)
                    {
                        case SYSMSG_REQUEST_AUTHENTICATION:
                            OnReceive_RequestAuthentication(buffer);
                            break;

                        case SYSMSG_ACCEPT_AUTHENTICATION:
                            OnReceive_AcceptAuthentication(buffer);
                            break;

                        case SYSMSG_PLAYER_JOIN:
                            OnReceive_PlayerJoin(buffer);
                            break;

                        case SYSMSG_PLAYER_LEAVE:
                            OnReceive_PlayerLeave(buffer);
                            break;


                        default:
                            Debug.LogWarningFormat("Invalid message type: {0}", msgType);
                            mImplementation.Disconnect();
                            break;
                    }
                }
                else
                {
                    InvokeOnReceive(message, message.Length, sender);
                }
            });
        }

        private void OnReceive_RequestAuthentication(Buffer buffer)
        {
            if (mState != State.Authenticate)
            {
                Debug.LogWarningFormat("Invalid state: {0}", mState);
                return;
            }

            var bytes = new byte[32];
            Array.Copy(mAuthData, 0, bytes, 0, 16);
            buffer.ReadBuffer(bytes, 16, 16);

            var hash = new SHA256Managed().ComputeHash(bytes);

            // SYSMSG_RESPOND_AUTHENTICATION

            mMessageBuffer.Clear();
            mMessageBuffer.Write(SYSMSG_RESPOND_AUTHENTICATION);
            mMessageBuffer.WriteBuffer(hash, 0, 32);
            mMessageBuffer.Write(mLocalPlayerName);

            if (!mImplementation.Send(mMessageBuffer.RawData, mMessageBuffer.Size, 0))
            {
                Debug.LogWarning("Failed to send SYSMSG_RESPOND_AUTHENTICATION");
                mImplementation.Disconnect();
            }
        }

        private void OnReceive_AcceptAuthentication(Buffer buffer)
        {
            if (mState != State.Authenticate)
            {
                Debug.LogWarningFormat("Invalid state: {0}", mState);
                return;
            }

            mLocalPlayerId = buffer.ReadUInt16();
            int numPlayers = buffer.ReadByte();

            for (int i = 0; i < numPlayers; ++i)
            {
                int playerId = buffer.ReadUInt16();
                var playerName = buffer.ReadString();

                var player = new Player(playerId, playerName);
                mPlayers.Add(player);
            }

            var localPlayer = new Player(mLocalPlayerId, mLocalPlayerName);
            mPlayers.Add(localPlayer);

            mImplementation.Accept();

            mState = State.Online;

            if (onConnect != null)
            {
                try
                {
                    onConnect();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void OnReceive_PlayerJoin(Buffer buffer)
        {
            if (mState != State.Online)
            {
                Debug.LogWarningFormat("Invalid state: {0}", mState);
                return;
            }

            int playerId = buffer.ReadUInt16();
            var playerName = buffer.ReadString();
            var player = new Player(playerId, playerName);
            mPlayers.Add(player);

            InvokeOnPlayerJoin(player);
        }

        private void OnReceive_PlayerLeave(Buffer buffer)
        {
            if (mState != State.Online)
            {
                Debug.LogWarningFormat("Invalid state: {0}", mState);
                return;
            }

            int playerId = buffer.ReadUInt16();

            var player = mPlayers.Where(p => p.PlayerId == playerId).FirstOrDefault();
            if (player == null)
            {
                Debug.LogWarningFormat("Invalid playerId: {0}", playerId);
                return;
            }

            mPlayers.Remove(player);

            InvokeOnPlayerLeave(player);
        }
    }
}
