using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

using UnityEngine;

namespace BleSock
{
    public static class Address
    {
        // Constants

        public const int Host   = 1;
        public const int All    = 0xffff;
        public const int Others = 0x10000;
    }

    public abstract class PeerBase : IDisposable
    {
        // Events

        public event Action onBluetoothRequire;
        public event Action onReady;
        public event Action onFail;

        public event Action<Player> onPlayerJoin;
        public event Action<Player> onPlayerLeave;

        public event Action<byte[], int, Player> onReceive;

        // Properties

        public bool IsReady
        {
            get
            {
                return mReady;
            }
        }

        public IList<Player> Players
        {
            get
            {
                return mPlayers.AsReadOnly();
            }
        }

        public Player LocalPlayer
        {
            get
            {
                return mPlayers.Where(p => p.PlayerId == LocalPlayerId).FirstOrDefault();
            }
        }

        public abstract bool IsBluetoothEnabled { get; }

        public abstract int LocalPlayerId { get; }

        // Methods

        public abstract void Send(byte[] message, int messageSize, int receiver);

        public virtual void Cleanup()
        {
            onBluetoothRequire = null;
            onReady = null;
            onFail = null;
            onPlayerJoin = null;
            onPlayerLeave = null;
            onReceive = null;

            mReady = false;
            mSynchronizationContext = null;
            mServiceUUID = null;
            mUploadUUID = null;
            mDownloadUUID = null;
            mAuthData = null;
            mLocalPlayerName = null;
            mPlayers.Clear();
            mSendBuffer = null;
            mReceiveBuffer = null;
        }

        public void Dispose()
        {
            Cleanup();
        }

        // Internal

        protected const int MESSAGE_SIZE_MAX = 4096;
        protected const int PLAYER_NAME_LENGTH_MAX = 32;

        // protected const byte SYSMSG_REQUEST_PING            = 0;
        // protected const byte SYSMSG_RESPOND_PING            = 1;

        protected const byte SYSMSG_REQUEST_AUTHENTICATION  = 10;
        protected const byte SYSMSG_ACCEPT_AUTHENTICATION   = 11;
        protected const byte SYSMSG_PLAYER_JOIN             = 12;
        protected const byte SYSMSG_PLAYER_LEAVE            = 13;

        protected const byte SYSMSG_RESPOND_AUTHENTICATION  = 20;

        private readonly byte[] PBKDF2_SALT = { 0x6d, 0x9f, 0x67, 0x59, 0x05, 0xc8, 0xbb, 0x21 };
        private const int PBKDF2_ITERATIONS = 1193;


        protected bool mReady;
        protected SynchronizationContext mSynchronizationContext;
        protected string mServiceUUID;
        protected string mUploadUUID;
        protected string mDownloadUUID;
        protected byte[] mAuthData;
        protected string mLocalPlayerName;
        protected List<Player> mPlayers = new List<Player>();
        protected MessageBuffer mSendBuffer;
        protected MessageBuffer mReceiveBuffer;


        protected void InitializeInternal(string protocolIdentifier, string playerName, SynchronizationContext synchronizationContext)
        {
            if (string.IsNullOrEmpty(protocolIdentifier))
            {
                throw new Exception("Invalid protocol-identifier");
            }

            if (string.IsNullOrEmpty(playerName))
            {
                throw new Exception("Invalid player-name");
            }

            if (playerName.Length > PLAYER_NAME_LENGTH_MAX)
            {
                throw new Exception("Player-name too long");
            }

            mSynchronizationContext = (synchronizationContext != null) ? synchronizationContext : SynchronizationContext.Current;
            if (mSynchronizationContext == null)
            {
                throw new Exception("Synchronization context is null");
            }

            var deriveBytes = new Rfc2898DeriveBytes(protocolIdentifier, PBKDF2_SALT, PBKDF2_ITERATIONS);

            mServiceUUID = new Guid(deriveBytes.GetBytes(16)).ToString("D");
            mUploadUUID = new Guid(deriveBytes.GetBytes(16)).ToString("D");
            mDownloadUUID = new Guid(deriveBytes.GetBytes(16)).ToString("D");
            // Debug.LogFormat("serviceUUID: {0} uploadUUID: {1} downloadUUID: {2}", mServiceUUID, mUploadUUID, mDownloadUUID);

            mAuthData = deriveBytes.GetBytes(16);

            mLocalPlayerName = playerName;

            mSendBuffer = new MessageBuffer();
            mReceiveBuffer = new MessageBuffer();
        }

        protected int PrepareSend(byte[] message, int messageSize, int receiver)
        {
            if (!IsReady)
            {
                throw new Exception("Not ready");
            }

            if (message == null)
            {
                throw new Exception("Message is null");
            }

            if (messageSize < 0)
            {
                throw new Exception("Invalid message-size");
            }

            if ((messageSize > message.Length) || (messageSize > MESSAGE_SIZE_MAX))
            {
                throw new Exception("Message-size too large");
            }

            int result = receiver;

            if ((receiver & Address.Others) != 0)
            {
                result |= ~LocalPlayerId;
            }

            return result & 0xffff;
        }

        protected void Post(Action action)
        {
            if ((mSynchronizationContext == null) || (action == null))
            {
                return;
            }

            mSynchronizationContext.Post(_ =>
            {
                action();
            }, null);
        }

        protected void InvokeOnBluetoothRequire()
        {
            if (onBluetoothRequire != null)
            {
                try
                {
                    onBluetoothRequire();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        protected void InvokeOnReady()
        {
            if (onReady != null)
            {
                try
                {
                    onReady();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        protected void InvokeOnFail()
        {
            if (onFail != null)
            {
                try
                {
                    onFail();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        protected void InvokeOnPlayerJoin(Player player)
        {
            if (onPlayerJoin != null)
            {
                try
                {
                    onPlayerJoin(player);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        protected void InvokeOnPlayerLeave(Player player)
        {
            if (onPlayerLeave != null)
            {
                try
                {
                    onPlayerLeave(player);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        protected void InvokeOnReceive(byte[] message, int messageSize, int playerId)
        {
            var player = mPlayers.Where(p => p.PlayerId == playerId).FirstOrDefault();
            if (player == null)
            {
                Debug.LogWarningFormat("Invalid playerId: {0}", playerId);
                return;
            }

            if (onReceive != null)
            {
                try
                {
                    onReceive(message, messageSize, player);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
