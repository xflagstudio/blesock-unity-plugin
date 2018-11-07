using System;
using System.Runtime.InteropServices;

using UnityEngine;

/* #if UNITY_EDITOR_WIN && NET_4_6

using BleSock.Windows;

#endif */

namespace BleSock
{
    internal abstract class PeripheralBase : IDisposable
    {
        // Events

        public event Action onBluetoothRequire;             // Bluetoothの有効化が要求された
        public event Action onReady;                        // Initializeが完了した
        public event Action onFail;                         // InitializeもしくはStartAdvertisingに失敗した

        public event Action<int> onConnect;                 // セントラルが接続した
        public event Action<int> onDisconnect;              // セントラルが無効となった

        public event Action<byte[], int> onReceiveDirect;   // セントラルから直接にメッセージを受信した
        public event Action<byte[], int> onReceive;         // メッセージを受信した

        // Properties

        public virtual bool IsBluetoothEnabled
        {
            get
            {
                return false;
            }
        }

        // Methods

        public virtual bool Initialize(string serviceUUID, string uploadUUID, string downloadUUID)
        {
            Debug.LogError("Unsupported platform");
            return false;
        }

        public virtual bool StartAdvertising(string deviceName)
        {
            return false;
        }

        public virtual void StopAdvertising() { }

        public virtual bool Accept(int connectionId, int playerId)
        {
            return false;
        }

        public virtual void Invalidate(int connectionId) { }

        public virtual bool SendDirect(byte[] message, int messageSize, int connectionId)
        {
            return false;
        }

        public virtual bool Send(byte[] message, int messageSize, int receiver)
        {
            return false;
        }

        public virtual void Cleanup()
        {
            onBluetoothRequire = null;
            onReady = null;
            onFail = null;

            onConnect = null;
            onDisconnect = null;

            onReceiveDirect = null;
            onReceive = null;
        }

        public virtual void Dispose()
        {
            Cleanup();
        }

        // Internal

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

        protected void InvokeOnConnect(int connectionId)
        {
            if (onConnect != null)
            {
                try
                {
                    onConnect(connectionId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        protected void InvokeOnDisconnect(int connectionId)
        {
            if (onDisconnect != null)
            {
                try
                {
                    onDisconnect(connectionId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        protected void InvokeOnReceiveDirect(byte[] message, int connectionId)
        {
            if (onReceiveDirect != null)
            {
                try
                {
                    onReceiveDirect(message, connectionId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        protected void InvokeOnReceive(byte[] message, int sender)
        {
            if (onReceive != null)
            {
                try
                {
                    onReceive(message, sender);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR

    internal class Peripheral : PeripheralBase
    {
        // Constructor

        public Peripheral()
        {
            mInstance = new AndroidJavaObject(NAME_PREFIX + "PeripheralImpl");
        }

        // Properties

        public override bool IsBluetoothEnabled
        {
            get
            {
                return AndroidUtils.IsBluetoothEnabled;
            }
        }

        // Methods

        public override bool Initialize(string serviceUUID, string uploadUUID, string downloadUUID)
        {
            if (mInstance != null)
            {
                try
                {
                    return mInstance.Call<bool>("initialize", serviceUUID, uploadUUID, downloadUUID, new PeripheralCallback(this));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override bool StartAdvertising(string deviceName)
        {
            if (mInstance != null)
            {
                try
                {
                    return mInstance.Call<bool>("startAdvertising", deviceName);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void StopAdvertising()
        {
            if (mInstance != null)
            {
                try
                {
                    mInstance.Call("stopAdvertising");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override bool Accept(int connectionId, int playerId)
        {
            if (mInstance != null)
            {
                try
                {
                    return mInstance.Call<bool>("accept", connectionId, playerId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void Invalidate(int connectionId)
        {
            if (mInstance != null)
            {
                try
                {
                    mInstance.Call("invalidate", connectionId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override bool SendDirect(byte[] message, int messageSize, int connectionId)
        {
            if (mInstance != null)
            {
                try
                {
                    return mInstance.Call<bool>("sendDirect", message, messageSize, connectionId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override bool Send(byte[] message, int messageSize, int receiver)
        {
            if (mInstance != null)
            {
                try
                {
                    return mInstance.Call<bool>("send", message, messageSize, receiver);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void Cleanup()
        {
            if (mInstance != null)
            {
                try
                {
                    mInstance.Call("cleanup");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            base.Cleanup();
        }

        public override void Dispose()
        {
            base.Dispose();

            if (mInstance != null)
            {
                mInstance.Dispose();
                mInstance = null;
            }
        }

        // Internal

        private const string NAME_PREFIX = "xflag.plugins.bleSock.";

        private class PeripheralCallback : AndroidJavaProxy
        {
            private Peripheral mOwner;

            public PeripheralCallback(Peripheral owner) : base(NAME_PREFIX + "PeripheralCallback")
            {
                mOwner = owner;
            }

            public void onBluetoothRequire()
            {
                mOwner.InvokeOnBluetoothRequire();
            }

            public void onReady()
            {
                mOwner.InvokeOnReady();
            }

            public void onFail()
            {
                mOwner.InvokeOnFail();
            }

            public void onConnect(int connectionId)
            {
                mOwner.InvokeOnConnect(connectionId);
            }

            public void onDisconnect(int connectionId)
            {
                mOwner.InvokeOnDisconnect(connectionId);
            }

            public void onReceiveDirect(AndroidJavaObject message, int connectionId)
            {
                byte[] bytes = null;

                try
                {
                    bytes = message.Call<byte[]>("getBytes");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                mOwner.InvokeOnReceiveDirect(bytes, connectionId);
            }

            public void onReceive(AndroidJavaObject message, int sender)
            {
                byte[] bytes = null;

                try
                {
                    bytes = message.Call<byte[]>("getBytes");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                mOwner.InvokeOnReceive(bytes, sender);
            }
        }

        private AndroidJavaObject mInstance = null;
    }

#elif UNITY_IOS && !UNITY_EDITOR

    internal class Peripheral : PeripheralBase
    {
        // Constructor

        public Peripheral()
        {
            mHandle = GCHandle.Alloc(this);
            mInstance = _blesock_peripheral_create();
        }

        // Propeties

        public override bool IsBluetoothEnabled
        {
            get
            {
                if (mInstance != IntPtr.Zero)
                {
                    try
                    {
                        return _blesock_peripheral_is_bluetooth_enabled(mInstance);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }

                return false;
            }
        }

        // Methods

        public override bool Initialize(string serviceUUID, string uploadUUID, string downloadUUID)
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    return _blesock_peripheral_initialize(
                        mInstance,
                        (IntPtr)mHandle,
                        serviceUUID,
                        uploadUUID,
                        downloadUUID,
                        OnBluetoothRequireCallback,
                        OnReadyCallback,
                        OnFailCallback,
                        OnConnectCallback,
                        OnDisconnectCallback,
                        OnReceiveDirectCallback,
                        OnReceiveCallback);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override bool StartAdvertising(string deviceName)
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    return _blesock_peripheral_start_advertising(mInstance, deviceName);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void StopAdvertising()
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    _blesock_peripheral_stop_advertising(mInstance);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override bool Accept(int connectionId, int playerId)
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    return _blesock_peripheral_accept(mInstance, connectionId, playerId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void Invalidate(int connectionId)
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    _blesock_peripheral_invalidate(mInstance, connectionId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override bool SendDirect(byte[] message, int messageSize, int connectionId)
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    return _blesock_peripheral_send_direct(mInstance, message, messageSize, connectionId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override bool Send(byte[] message, int messageSize, int receiver)
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    return _blesock_peripheral_send(mInstance, message, messageSize, receiver);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void Cleanup()
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    _blesock_peripheral_cleanup(mInstance);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            base.Cleanup();
        }

        public override void Dispose()
        {
            base.Dispose();

            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    _blesock_peripheral_release(mInstance);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                mInstance = IntPtr.Zero;
            }

            if (mHandle.IsAllocated)
            {
                mHandle.Free();
            }
        }

        // Internal

        private delegate void CommonCallback(IntPtr owner);
        private delegate void ConnectionCallback(IntPtr owner, Int32 connectionId);
        private delegate void ReceiveCallback(IntPtr owner, IntPtr message, Int32 messageSize, Int32 sender);

        private GCHandle mHandle;
        private IntPtr mInstance = IntPtr.Zero;

        [DllImport("__Internal")]
        private static extern IntPtr _blesock_peripheral_create();

        [DllImport("__Internal")]
        private static extern bool _blesock_peripheral_is_bluetooth_enabled(IntPtr instance);

        [DllImport("__Internal")]
        private static extern bool _blesock_peripheral_initialize(
            IntPtr instance,
            IntPtr owner,
            string serviceUUID,
            string uploadUUID,
            string downloadUUID,
            CommonCallback onBluetoothRequire,
            CommonCallback onReady,
            CommonCallback onFail,
            ConnectionCallback onConnect,
            ConnectionCallback onDisconnect,
            ReceiveCallback onReceiveDirect,
            ReceiveCallback onReceive);

        [DllImport("__Internal")]
        private static extern bool _blesock_peripheral_start_advertising(IntPtr instance, string deviceName);

        [DllImport("__Internal")]
        private static extern void _blesock_peripheral_stop_advertising(IntPtr instance);

        [DllImport("__Internal")]
        private static extern bool _blesock_peripheral_accept(IntPtr instance, Int32 connectionId, Int32 playerId);

        [DllImport("__Internal")]
        private static extern void _blesock_peripheral_invalidate(IntPtr instance, Int32 connectionId);

        [DllImport("__Internal")]
        private static extern bool _blesock_peripheral_send_direct(IntPtr instance, byte[] message, Int32 messageSize, Int32 connectionId);

        [DllImport("__Internal")]
        private static extern bool _blesock_peripheral_send(IntPtr instance, byte[] message, Int32 messageSize, Int32 receiver);

        [DllImport("__Internal")]
        private static extern void _blesock_peripheral_cleanup(IntPtr instance);

        [DllImport("__Internal")]
        private static extern void _blesock_peripheral_release(IntPtr instance);


        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnBluetoothRequireCallback(IntPtr owner)
        {
            var instance = (Peripheral)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnBluetoothRequire();
        }

        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnReadyCallback(IntPtr owner)
        {
            var instance = (Peripheral)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnReady();
        }

        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnFailCallback(IntPtr owner)
        {
            var instance = (Peripheral)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnFail();
        }

        [AOT.MonoPInvokeCallback(typeof(ConnectionCallback))]
        static void OnConnectCallback(IntPtr owner, Int32 connectionId)
        {
            var instance = (Peripheral)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnConnect(connectionId);
        }

        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnDisconnectCallback(IntPtr owner, Int32 connectionId)
        {
            var instance = (Peripheral)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnDisconnect(connectionId);
        }

        [AOT.MonoPInvokeCallback(typeof(ReceiveCallback))]
        static void OnReceiveDirectCallback(IntPtr owner, IntPtr message, Int32 messageSize, Int32 connectionId)
        {
            var buffer = new byte[messageSize];
            Marshal.Copy(message, buffer, 0, messageSize);

            var instance = (Peripheral)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnReceiveDirect(buffer, connectionId);
        }

        [AOT.MonoPInvokeCallback(typeof(ReceiveCallback))]
        static void OnReceiveCallback(IntPtr owner, IntPtr message, Int32 messageSize, Int32 sender)
        {
            var buffer = new byte[messageSize];
            Marshal.Copy(message, buffer, 0, messageSize);

            var instance = (Peripheral)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnReceive(buffer, sender);
        }
    }

/* #elif UNITY_EDITOR_WIN && NET_4_6 // Windows10のペリフェラルは非常に不安定なので封印

    internal class Peripheral : PeripheralBase
    {
        // Properties

        public override bool IsBluetoothEnabled
        {
            get
            {
                if (mHandler != null)
                {
                    return mHandler.IsBluetoothEnabled;
                }

                return false;
            }
        }

        // Methods

        public override bool Initialize(string serviceUUID, string uploadUUID, string downloadUUID)
        {
            if (mHandler != null)
            {
                Debug.LogError("already called initialize");
                return false;
            }

            mHandler = new PeripheralHandler();
            mHandler.onBluetoothRequire += InvokeOnBluetoothRequire;
            mHandler.onReady += InvokeOnReady;
            mHandler.onFail += InvokeOnFail;
            mHandler.onConnect += InvokeOnConnect;
            mHandler.onDisconnect += InvokeOnDisconnect;
            mHandler.onReceiveDirect += InvokeOnReceiveDirect;
            mHandler.onReceive += InvokeOnReceive;

            return mHandler.Initialize(EXECUTION_FILEPATH, PORT_NUMBER, serviceUUID, uploadUUID, downloadUUID);
        }

        public override bool StartAdvertising(string deviceName)
        {
            if (mHandler != null)
            {
                return mHandler.StartAdvertising(deviceName);
            }

            return false;
        }

        public override void StopAdvertising()
        {
            if (mHandler != null)
            {
                mHandler.StopAdvertising();
            }
        }

        public override bool Accept(int connectionId, int playerId)
        {
            if (mHandler != null)
            {
                return mHandler.Accept(connectionId, playerId);
            }

            return false;
        }

        public override void Invalidate(int connectionId)
        {
            if (mHandler != null)
            {
                mHandler.Invalidate(connectionId);
            }
        }

        public override bool SendDirect(byte[] message, int messageSize, int connectionId)
        {
            if (mHandler != null)
            {
                return mHandler.SendDirect(message, messageSize, connectionId);
            }

            return false;
        }

        public override bool Send(byte[] message, int messageSize, int receiver)
        {
            if (mHandler != null)
            {
                return mHandler.Send(message, messageSize, receiver);
            }

            return false;
        }

        public override void Cleanup()
        {
            if (mHandler != null)
            {
                mHandler.Cleanup();
            }

            base.Cleanup();
        }

        public override void Dispose()
        {
            base.Dispose();

            if (mHandler != null)
            {
                mHandler.Dispose();
                mHandler = null;
            }
        }

        // Internal

        private const string EXECUTION_FILEPATH = "../Externals/Windows10/WinBleHost.exe";
        private const int PORT_NUMBER = 12345;

        private PeripheralHandler mHandler;
    }

*/
#else

    internal class Peripheral : PeripheralBase { }

#endif
}
