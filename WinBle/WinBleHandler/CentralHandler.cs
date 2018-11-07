using System;
using System.ServiceModel;
using System.ServiceModel.Channels;

using UnityEngine;

namespace BleSock.Windows
{
    public class CentralHandler : HandlerBase, IWcfCentralCallback
    {
        // Events

        public event Action onBluetoothRequire;
        public event Action onReady;
        public event Action onFail;

        public event Action<string, int> onDiscover;
        public event Action onConnect;
        public event Action onDisconnect;

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
                    string arguments = string.Format("Central {0}", portNumber);
                    LaunchProcess(executionFilepath, arguments);
                }

                var bindingElements = new BindingElement[] // UnityEditorのmonoからはこの組み合わせでしかWCFを使用できない
                {
                    new BinaryMessageEncodingBindingElement(),
                    new TcpTransportBindingElement()
                };
                string address = string.Format("net.tcp://localhost:{0}/WcfCentralHost", portNumber);

                mHost = DuplexChannelFactory<IWcfCentralHost>.CreateChannel(
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

        public bool StartScan()
        {
            if (mHost == null)
            {
                return false;
            }

            try
            {
                if (!mHost.StartScan())
                {
                    Debug.LogError("failed to StartScan");
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

        public void StopScan()
        {
            if (mHost == null)
            {
                return;
            }

            try
            {
                mHost.StopScan();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
            }
        }


        public bool Connect(int deviceId)
        {
            if (mHost == null)
            {
                return false;
            }

            try
            {
                if (!mHost.Connect(deviceId))
                {
                    Debug.LogError("failed to Connect");
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

        public void Accept()
        {
            if (mHost == null)
            {
                return;
            }

            try
            {
                mHost.Accept();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
            }
        }

        public void Disconnect()
        {
            if (mHost == null)
            {
                return;
            }

            try
            {
                mHost.Disconnect();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mHost = null;
            }
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

            onDiscover = null;
            onConnect = null;
            onDisconnect = null;

            onReceive = null;
        }

        // Internal

        private IWcfCentralHost mHost;

        protected override void OnProcessExited()
        {
            base.OnProcessExited();

            mHost = null;
        }

        // IWcfCentralCallback

        void IWcfCentralCallback.OnBluetoothRequire()
        {
            onBluetoothRequire?.Invoke();
        }

        void IWcfCentralCallback.OnReady()
        {
            onReady?.Invoke();
        }

        void IWcfCentralCallback.OnFail()
        {
            onFail?.Invoke();
        }

        void IWcfCentralCallback.OnDiscover(string deviceName, int deviceId)
        {
            onDiscover?.Invoke(deviceName, deviceId);
        }

        void IWcfCentralCallback.OnConnect()
        {
            onConnect?.Invoke();
        }

        void IWcfCentralCallback.OnDisconnect()
        {
            onDisconnect?.Invoke();
        }

        void IWcfCentralCallback.OnReceive(byte[] message, int sender)
        {
            onReceive?.Invoke(message, sender);
        }
    }
}
