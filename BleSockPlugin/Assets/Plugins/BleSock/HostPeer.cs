using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using UnityEngine;

namespace BleSock
{
    public class HostPeer : PeerBase
    {
        // Properties

        public bool IsAdvertising
        {
            get
            {
                return mAdvertising;
            }
        }

        public int MaximumPlayers
        {
            get
            {
                return mMaximumPlayers;
            }

            set
            {
                mMaximumPlayers = Math.Max(value, 0);
            }
        }

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
                return Address.Host;
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

            for (int i = 1; i < 16; ++i)
            {
                mUnusedPlayerIds.Add(1 << i);
            }

            mImplementation = new Peripheral();
            mImplementation.onBluetoothRequire += OnBluetoothRequire;
            mImplementation.onReady += OnReady;
            mImplementation.onFail += OnFail;
            mImplementation.onConnect += OnConnect;
            mImplementation.onDisconnect += OnDisconnect;
            mImplementation.onReceiveDirect += OnReceiveDirect;
            mImplementation.onReceive += OnReceive;

            if (!mImplementation.Initialize(mServiceUUID, mUploadUUID, mDownloadUUID))
            {
                mImplementation.Dispose();
                mImplementation = null;
                throw new Exception("Failed to initialize");
            }
        }

        public void StartAdvertising(string deviceName)
        {
            if (!IsReady)
            {
                throw new Exception("Not ready");
            }

            if (string.IsNullOrEmpty(deviceName))
            {
                throw new Exception("Invalid deviceName");
            }

            if (Encoding.UTF8.GetByteCount(deviceName) > DEVICE_NAME_SIZE_MAX)
            {
                throw new Exception("deviceName too large");
            }

            if (mAdvertising)
            {
                throw new Exception("Already advertising started");
            }

            if (mImplementation.StartAdvertising(deviceName))
            {
                mAdvertising = true;
            }
            else
            {
                throw new Exception("Failed to start advertising");
            }
        }

        public void StopAdvertising()
        {
            if (!IsReady)
            {
                throw new Exception("Not ready");
            }

            if (mAdvertising)
            {
                mImplementation.StopAdvertising();
                mAdvertising = false;
            }
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
            if (mImplementation != null)
            {
                mImplementation.Dispose();
                mImplementation = null;
            }

            mAdvertising = false;
            mMaximumPlayers = 0;

            mCentralContexts.Clear();
            mUnusedPlayerIds.Clear();

            base.Cleanup();
        }

        // Internal

        private const int DEVICE_NAME_SIZE_MAX = 27;

        private Peripheral mImplementation;
        private bool mAdvertising;
        private int mMaximumPlayers;

        private class CentralContext
        {
            public int connectionId;
            public byte[] authHash;
            public int playerId;
        }

        private List<CentralContext> mCentralContexts = new List<CentralContext>();
        private List<int> mUnusedPlayerIds = new List<int>();


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

                var player = new Player(Address.Host, mLocalPlayerName);
                mPlayers.Add(player);

                InvokeOnReady();
            });
        }

        private void OnFail()
        {
            Post(() =>
            {
                mAdvertising = false;

                InvokeOnFail();
            });
        }

        private void OnConnect(int connectionId)
        {
            Post(() =>
            {
                if (!IsReady)
                {
                    return;
                }

                if ((mMaximumPlayers > 0) && (mPlayers.Count >= mMaximumPlayers))
                {
                    mImplementation.Invalidate(connectionId);
                }

                var authData = new byte[16];
                RandomNumberGenerator.Create().GetNonZeroBytes(authData);

                // SYSMSG_REQUEST_AUTHENTICATION

                mSendBuffer.Clear();
                mSendBuffer.Write(SYSMSG_REQUEST_AUTHENTICATION);
                mSendBuffer.WriteBytes(authData, 0, authData.Length);

                if (!mImplementation.SendDirect(mSendBuffer.RawData, mSendBuffer.Size, connectionId))
                {
                    Debug.LogWarning("Failed to send SYSMSG_REQUEST_AUTHENTICATION");
                    mImplementation.Invalidate(connectionId);
                    return;
                }

                var bytes = new byte[32];
                Array.Copy(mAuthData, 0, bytes, 0, 16);
                Array.Copy(authData, 0, bytes, 16, 16);

                var context = new CentralContext();
                context.connectionId = connectionId;
                context.authHash = new SHA256Managed().ComputeHash(bytes);
                context.playerId = 0;

                mCentralContexts.Add(context);
            });
        }

        private void OnDisconnect(int connectionId)
        {
            Post(() =>
            {
                if (!IsReady)
                {
                    return;
                }

                var context = mCentralContexts.Where(ctx => ctx.connectionId == connectionId).FirstOrDefault();
                if (context == null)
                {
                    Debug.LogWarningFormat("Invalid connectionId: {0}", connectionId);
                    return;
                }

                mCentralContexts.Remove(context);

                if (context.playerId != 0)
                {
                    mUnusedPlayerIds.Add(context.playerId);
                }

                var player = mPlayers.Where(p => p.PlayerId == context.playerId).FirstOrDefault();
                if (player != null)
                {
                    // SYSMSG_PLAYER_LEAVE

                    mSendBuffer.Clear();
                    mSendBuffer.Write(SYSMSG_PLAYER_LEAVE);
                    mSendBuffer.Write((UInt16)context.playerId);

                    foreach (var ctx in mCentralContexts)
                    {
                        if (ctx.playerId != 0)
                        {
                            if (!mImplementation.SendDirect(mSendBuffer.RawData, mSendBuffer.Size, ctx.connectionId))
                            {
                                Debug.LogWarning("Failed to send SYSMSG_PLAYER_LEAVE");
                            }
                        }
                    }

                    mPlayers.Remove(player);

                    InvokeOnPlayerLeave(player);
                }
            });
        }

        private void OnReceiveDirect(byte[] message, int connectionId)
        {
            Post(() =>
            {
                mReceiveBuffer.Clear();
                mReceiveBuffer.WriteBytes(message, 0, message.Length);
                mReceiveBuffer.Seek(0);

                byte msgType = mReceiveBuffer.ReadByte();
                switch (msgType)
                {
                    case SYSMSG_RESPOND_AUTHENTICATION:
                        OnReceive_RespondAuthentication(connectionId);
                        break;


                    default:
                        Debug.LogWarningFormat("Invalid message type: {0}", msgType);
                        mImplementation.Invalidate(connectionId);
                        break;
                }
            });
        }

        private void OnReceive_RespondAuthentication(int connectionId)
        {
            var context = mCentralContexts.Where(ctx => ctx.connectionId == connectionId).FirstOrDefault();
            if (context == null)
            {
                Debug.LogWarningFormat("Invalid connectionId: {0}", connectionId);
                return;
            }

            var receivedHash = new byte[32];
            mReceiveBuffer.ReadBytes(receivedHash, 0, receivedHash.Length);

            var playerName = mReceiveBuffer.ReadString();

            if (context.playerId != 0)
            {
                Debug.LogWarning("Already authenticated");
                return;
            }

            var hash = context.authHash;
            context.authHash = null;

            if (!receivedHash.SequenceEqual(hash))
            {
                Debug.LogWarning("Authentication error");
                mImplementation.Invalidate(connectionId);
                return;
            }

            if (mUnusedPlayerIds.Count == 0)
            {
                Debug.LogWarning("Too many players");
                mImplementation.Invalidate(connectionId);
                return;
            }

            context.playerId = mUnusedPlayerIds[0];
            mUnusedPlayerIds.RemoveAt(0);

            mImplementation.Accept(connectionId, context.playerId);

            // SYSMSG_ACCEPT_AUTHENTICATION

            mSendBuffer.Clear();
            mSendBuffer.Write(SYSMSG_ACCEPT_AUTHENTICATION);
            mSendBuffer.Write((UInt16)context.playerId);
            mSendBuffer.Write((byte)mPlayers.Count);

            foreach (var ply in mPlayers)
            {
                mSendBuffer.Write((UInt16)ply.PlayerId);
                mSendBuffer.WriteString(ply.PlayerName);
            }

            if (!mImplementation.SendDirect(mSendBuffer.RawData, mSendBuffer.Size, connectionId))
            {
                Debug.LogWarning("Failed to send SYSMSG_ACCEPT_AUTHENTICATION");
                mImplementation.Invalidate(connectionId);
                return;
            }

            // SYSMSG_PLAYER_JOIN

            mSendBuffer.Clear();
            mSendBuffer.Write(SYSMSG_PLAYER_JOIN);
            mSendBuffer.Write((UInt16)context.playerId);
            mSendBuffer.WriteString(playerName);

            foreach (var ctx in mCentralContexts)
            {
                if ((ctx.playerId != 0) && (ctx != context))
                {
                    if (!mImplementation.SendDirect(mSendBuffer.RawData, mSendBuffer.Size, ctx.connectionId))
                    {
                        Debug.LogWarning("Failed to send SYSMSG_PLAYER_JOIN");
                    }
                }
            }

            var player = new Player(context.playerId, playerName);
            mPlayers.Add(player);

            InvokeOnPlayerJoin(player);
        }

        private void OnReceive(byte[] message, int playerId)
        {
            Post(() =>
            {
                if (!IsReady)
                {
                    return;
                }

                InvokeOnReceive(message, message.Length, playerId);
            });
        }
    }
}
