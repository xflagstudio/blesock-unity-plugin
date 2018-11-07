using System;
using System.Runtime.InteropServices;

using UnityEngine;

#if UNITY_EDITOR_WIN && NET_4_6

using BleSock.Windows;

#endif

namespace BleSock
{
    internal abstract class CentralBase : IDisposable
    {
        // Events

        public event Action onBluetoothRequire;         // Bluetoothの有効化が要求された
        public event Action onReady;                    // Initializeが完了した
        public event Action onFail;                     // InitializeもしくはStartScan、Connectに失敗した

        public event Action<string, int> onDiscover;    // ペリフェラルを発見した
        public event Action onConnect;                  // ペリフェラルに接続された
        public event Action onDisconnect;               // ペリフェラルから切断された

        public event Action<byte[], int> onReceive;     // メッセージを受信した

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
            return false;
        }

        public virtual bool StartScan()
        {
            return false;
        }

        public virtual void StopScan() { }

        public virtual bool Connect(int deviceId)
        {
            return false;
        }

        public virtual void Accept() { }

        public virtual void Disconnect() { }

        public virtual bool Send(byte[] message, int messageSize, int receiver)
        {
            return false;
        }

        public virtual void Cleanup()
        {
            onBluetoothRequire = null;
            onReady = null;
            onFail = null;

            onDiscover = null;
            onConnect = null;
            onDisconnect = null;

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

        protected void InvokeOnDiscover(string deviceName, int deviceId)
        {
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
        }

        protected void InvokeOnConnect()
        {
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

        protected void InvokeOnDisconnect()
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

    internal class Central : CentralBase
    {
        // Constructor

        public Central()
        {
            mInstance = new AndroidJavaObject(NAME_PREFIX + "CentralImpl");
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
                    return mInstance.Call<bool>("initialize", serviceUUID, uploadUUID, downloadUUID, new CentralCallback(this));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override bool StartScan()
        {
            if (mInstance != null)
            {
                try
                {
                    return mInstance.Call<bool>("startScan");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void StopScan()
        {
            if (mInstance != null)
            {
                try
                {
                    mInstance.Call("stopScan");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override bool Connect(int deviceId)
        {
            if (mInstance != null)
            {
                try
                {
                    return mInstance.Call<bool>("connect", deviceId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void Accept()
        {
            if (mInstance != null)
            {
                try
                {
                    mInstance.Call("accept");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override void Disconnect()
        {
            if (mInstance != null)
            {
                try
                {
                    mInstance.Call("disconnect");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
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

        private class CentralCallback : AndroidJavaProxy
        {
            private Central mOwner;

            public CentralCallback(Central owner) : base(NAME_PREFIX + "CentralCallback")
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

            public void onDiscover(string deviceName, int deviceId)
            {
                mOwner.InvokeOnDiscover(deviceName, deviceId);
            }

            public void onConnect()
            {
                mOwner.InvokeOnConnect();
            }

            public void onDisconnect()
            {
                mOwner.InvokeOnDisconnect();
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

    internal class Central : CentralBase
    {
        // Constructor

        public Central()
        {
            mHandle = GCHandle.Alloc(this);
            mInstance = _blesock_central_create();
        }

        // Properties

        public override bool IsBluetoothEnabled
        {
            get
            {
                if (mInstance != IntPtr.Zero)
                {
                    try
                    {
                        return _blesock_central_is_bluetooth_enabled(mInstance);
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
                    return _blesock_central_initialize(
                        mInstance,
                        (IntPtr)mHandle,
                        serviceUUID,
                        uploadUUID,
                        downloadUUID,
                        OnBluetoothRequireCallback,
                        OnReadyCallback,
                        OnFailCallback,
                        OnDiscoverCallback,
                        OnConnectCallback,
                        OnDisconnectCallback,
                        OnReceiveCallback);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override bool StartScan()
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    return _blesock_central_start_scan(mInstance);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void StopScan()
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    _blesock_central_stop_scan(mInstance);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override bool Connect(int deviceId)
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    return _blesock_central_connect(mInstance, deviceId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return false;
        }

        public override void Accept()
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    _blesock_central_accept(mInstance);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override void Disconnect()
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    _blesock_central_disconnect(mInstance);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override bool Send(byte[] message, int messageSize, int receiver)
        {
            if (mInstance != IntPtr.Zero)
            {
                try
                {
                    return _blesock_central_send(mInstance, message, messageSize, receiver);
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
                    _blesock_central_cleanup(mInstance);
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
                    _blesock_central_release(mInstance);
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
        private delegate void DiscoverCallback(IntPtr owner, string deviceName, Int32 deviceId);
        private delegate void ReceiveCallback(IntPtr owner, IntPtr message, Int32 messageSize, Int32 sender);

        private GCHandle mHandle;
        private IntPtr mInstance = IntPtr.Zero;

        [DllImport("__Internal")]
        private static extern IntPtr _blesock_central_create();

        [DllImport("__Internal")]
        private static extern bool _blesock_central_is_bluetooth_enabled(IntPtr instance);

        [DllImport("__Internal")]
        private static extern bool _blesock_central_initialize(
            IntPtr instance,
            IntPtr owner,
            string serviceUUID,
            string uploadUUID,
            string downloadUUID,
            CommonCallback onBluetoothRequire,
            CommonCallback onReady,
            CommonCallback onFail,
            DiscoverCallback onDiscover,
            CommonCallback onConnect,
            CommonCallback onDisconnect,
            ReceiveCallback onReceive);

        [DllImport("__Internal")]
        private static extern bool _blesock_central_start_scan(IntPtr instance);

        [DllImport("__Internal")]
        private static extern void _blesock_central_stop_scan(IntPtr instance);

        [DllImport("__Internal")]
        private static extern bool _blesock_central_connect(IntPtr instance, Int32 deviceId);

        [DllImport("__Internal")]
        private static extern void _blesock_central_accept(IntPtr instance);

        [DllImport("__Internal")]
        private static extern void _blesock_central_disconnect(IntPtr instance);

        [DllImport("__Internal")]
        private static extern bool _blesock_central_send(IntPtr instance, byte[] message, Int32 messageSize, Int32 to);

        [DllImport("__Internal")]
        private static extern void _blesock_central_cleanup(IntPtr instance);

        [DllImport("__Internal")]
        private static extern void _blesock_central_release(IntPtr instance);


        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnBluetoothRequireCallback(IntPtr owner)
        {
            var instance = (Central)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnBluetoothRequire();
        }

        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnReadyCallback(IntPtr owner)
        {
            var instance = (Central)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnReady();
        }

        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnFailCallback(IntPtr owner)
        {
            var instance = (Central)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnFail();
        }

        [AOT.MonoPInvokeCallback(typeof(DiscoverCallback))]
        static void OnDiscoverCallback(IntPtr owner, string deviceName, Int32 deviceId)
        {
            var instance = (Central)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnDiscover(deviceName, deviceId);
        }

        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnConnectCallback(IntPtr owner)
        {
            var instance = (Central)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnConnect();
        }

        [AOT.MonoPInvokeCallback(typeof(CommonCallback))]
        static void OnDisconnectCallback(IntPtr owner)
        {
            var instance = (Central)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnDisconnect();
        }

        [AOT.MonoPInvokeCallback(typeof(ReceiveCallback))]
        static void OnReceiveCallback(IntPtr owner, IntPtr message, Int32 messageSize, Int32 sender)
        {
            var buffer = new byte[messageSize];
            Marshal.Copy(message, buffer, 0, messageSize);

            var instance = (Central)GCHandle.FromIntPtr(owner).Target;
            instance.InvokeOnReceive(buffer, sender);
        }
    }

#elif UNITY_EDITOR_WIN && NET_4_6

    internal class Central : CentralBase
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

            mHandler = new CentralHandler();
            mHandler.onBluetoothRequire += InvokeOnBluetoothRequire;
            mHandler.onReady += InvokeOnReady;
            mHandler.onFail += InvokeOnFail;
            mHandler.onDiscover += InvokeOnDiscover;
            mHandler.onConnect += InvokeOnConnect;
            mHandler.onDisconnect += InvokeOnDisconnect;
            mHandler.onReceive += InvokeOnReceive;

            return mHandler.Initialize(EXECUTION_FILEPATH, PORT_NUMBER, serviceUUID, uploadUUID, downloadUUID);
        }

        public override bool StartScan()
        {
            if (mHandler != null)
            {
                return mHandler.StartScan();
            }

            return false;
        }

        public override void StopScan()
        {
            if (mHandler != null)
            {
                mHandler.StopScan();
            }
        }

        public override bool Connect(int deviceId)
        {
            if (mHandler != null)
            {
                return mHandler.Connect(deviceId);
            }

            return false;
        }

        public override void Accept()
        {
            if (mHandler != null)
            {
                mHandler.Accept();
            }
        }

        public override void Disconnect()
        {
            if (mHandler != null)
            {
                mHandler.Disconnect();
            }
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

        private CentralHandler mHandler;
    }

#else

    internal class Central : CentralBase { }

#endif
}
