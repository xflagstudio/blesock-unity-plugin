using System;
using System.ServiceModel;
using System.ServiceModel.Channels;

using UnityEngine;

namespace BleSock.Windows
{
    public class PeripheralHandler : HandlerBase, IWcfPeripheralCallback
    {
        // Events

        public event Action onBluetoothRequire;
        public event Action onReady;
        public event Action onFail;

        public event Action<int> onConnect;
        public event Action<int> onDisconnect;

        public event Action<byte[], int> onReceiveDirect;
        public event Action<byte[], int> onReceive;

        // Properties

        public bool IsBluetoothEnabled
        {
            get
            {
                return true; // TODO
            }
        }

        // Methods

        public bool Initialize(string executionFilepath, int portNumber, string serviceUUID, string uploadUUID, string downloadUUID)
        {
            if (mHost != null)
            {
                Debug.LogError("already called initialize");
                return false;
            }

            try
            {
                if (string.IsNullOrEmpty(executionFilepath))
                {
                    Debug.Log("skip launching process");
                }
                else
                {
                    string arguments = string.Format("Peripheral {0}", portNumber);
                    LaunchProcess(executionFilepath, arguments);
                }

                var bindingElements = new BindingElement[] // UnityEditorのmonoからはこの組み合わせでしかWCFを使用できない
                {
                    new BinaryMessageEncodingBindingElement(),
                    new TcpTransportBindingElement()
                };
                string address = string.Format("net.tcp://localhost:{0}/WcfPeripheralHost", portNumber);

                mHost = DuplexChannelFactory<IWcfPeripheralHost>.CreateChannel(
                    this,
                    new CustomBinding(bindingElements),
                    new EndpointAddress(address));

                Debug.LogFormat("Host: {0}", address);

                if (!mHost.Initialize(serviceUUID, uploadUUID, downloadUUID))
                {
                    Debug.LogError("failed to Initialize");
                    Cleanup();
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Cleanup();
                return false;
            }

            return true;
        }

        public bool StartAdvertising(string deviceName)
        {
            if (mHost == null)
            {
                return false;
            }

            try
            {
                if (!mHost.StartAdvertising(deviceName))
                {
                    Debug.LogError("failed to StartAdvertising");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
                return false;
            }

            return true;
        }

        public void StopAdvertising()
        {
            if (mHost == null)
            {
                return;
            }

            try
            {
                mHost.StopAdvertising();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
            }
        }


        public bool Accept(int connectionId, int playerId)
        {
            if (mHost == null)
            {
                return false;
            }

            try
            {
                if (!mHost.Accept(connectionId, playerId))
                {
                    Debug.LogError("failed to Accept");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
                return false;
            }

            return true;
        }

        public void Invalidate(int connectionId)
        {
            if (mHost == null)
            {
                return;
            }

            try
            {
                mHost.Invalidate(connectionId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
            }
        }

        public bool SendDirect(byte[] message, int messageSize, int connectionId)
        {
            if (mHost == null)
            {
                return false;
            }

            try
            {
                mHost.SendDirect(message, messageSize, connectionId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
                return false;
            }

            return true;
        }

        public bool Send(byte[] message, int messageSize, int receiver)
        {
            if (mHost == null)
            {
                return false;
            }

            try
            {
                mHost.Send(message, messageSize, receiver);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
                return false;
            }

            return true;
        }

        public override void Cleanup()
        {
            if (mHost != null)
            {
                try
                {
                    mHost.Cleanup();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                mHost = null;
            }

            base.Cleanup();

            onBluetoothRequire = null;
            onReady = null;
            onFail = null;

            onConnect = null;
            onDisconnect = null;

            onReceiveDirect = null;
            onReceive = null;
        }

        // Internal

        private IWcfPeripheralHost mHost;

        protected override void OnProcessExited()
        {
            base.OnProcessExited();

            mHost = null;
        }

        // IWcfPeripheralCallback

        void IWcfPeripheralCallback.OnBluetoothRequire()
        {
            onBluetoothRequire?.Invoke();
        }

        void IWcfPeripheralCallback.OnReady()
        {
            onReady?.Invoke();
        }

        void IWcfPeripheralCallback.OnFail()
        {
            onFail?.Invoke();
        }

        void IWcfPeripheralCallback.OnConnect(int connectionId)
        {
            onConnect?.Invoke(connectionId);
        }

        void IWcfPeripheralCallback.OnDisconnect(int connectionId)
        {
            onDisconnect?.Invoke(connectionId);
        }

        void IWcfPeripheralCallback.OnReceiveDirect(byte[] message, int connectionId)
        {
            onReceiveDirect?.Invoke(message, connectionId);
        }

        void IWcfPeripheralCallback.OnReceive(byte[] message, int sender)
        {
            onReceive?.Invoke(message, sender);
        }
    }
}
