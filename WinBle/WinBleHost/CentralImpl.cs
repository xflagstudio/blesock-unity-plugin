using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace BleSock.Windows
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Multiple)]
    internal class CentralImpl : IWcfCentralHost, IDisposable
    {
        public void Run(int portNumber)
        {
            if (mServiceHost != null)
            {
                Utils.Error("Already opening service-host");
                return;
            }

            var bindingElements = new BindingElement[] // UnityEditorのmonoからはこの組み合わせでしかWCFを使用できない
            {
                    new BinaryMessageEncodingBindingElement(),
                    new TcpTransportBindingElement()
            };
            string address = string.Format("net.tcp://localhost:{0}/WcfCentralHost", portNumber);

            mServiceHost = new ServiceHost(this);
            mServiceHost.AddServiceEndpoint(typeof(IWcfCentralHost), new CustomBinding(bindingElements), address);
            mServiceHost.Open();

            Utils.Info("Hosting service: {0}", address);

            while (!mFinished)
            {
                Thread.Sleep(10);
            }
        }

        public void Dispose()
        {
            lock (mLockObject)
            {
                CleanupInternal();
            }

            if (mServiceHost != null)
            {
                mServiceHost.Close();
                mServiceHost = null;
            }
        }

        // Internal

        private const int MESSAGE_SIZE_MAX = 4096;
        private const int BUFFER_SIZE = 8192;
        private const int ACCEPTANCE_TIMEOUT = 20000;

        private enum Status
        {
            Invalid,
            Ready,
            Scan,
            Connect,
            Online,
            Disconnect,
        }

        private ServiceHost mServiceHost;
        private bool mFinished;

        private object mLockObject = new object();
        private Status mStatus = Status.Invalid;

        private IWcfCentralCallback mCallback;
        private Guid mServiceUUID;
        private Guid mUploadUUID;
        private Guid mDownloadUUID;

        private BluetoothLEAdvertisementWatcher mWatcher;

        private class PeripheralContext
        {
            public ulong Address { get; private set; }
            public int Id { get; private set; }
            public string Name { get; private set; }

            public PeripheralContext(ulong address, int id, string name)
            {
                Address = address;
                Id = id;
                Name = name;
            }
        }

        private List<PeripheralContext> mDiscoveredPeripherals = new List<PeripheralContext>();
        private int mNextPeripheralId = 1;

        private CancellationTokenSource mConnectionCancelTokenSource;
        private BluetoothLEDevice mDevice;
        private GattDeviceService mService;
        private GattCharacteristic mUploadCharacteristic;
        private GattCharacteristic mDownloadCharacteristic;

        private BinaryWriter mSendBufferWriter = new BinaryWriter(new MemoryStream());
        private BinaryReader mReceiveBufferReader = new BinaryReader(new MemoryStream());
        private int mReceiveMessageSize = -1;
        private int mReceiveMessageAddress = -1;
        private bool mValueWriting;


        private void SetStatus(Status status)
        {
            if (mStatus != status)
            {
                Utils.Info("status: {0}", status.ToString());
                mStatus = status;
            }
        }

        private void StopScanInternal()
        {
            if (mStatus != Status.Scan)
            {
                Utils.Error("invalid status: {0}", mStatus.ToString());
                return;
            }

            mWatcher.Stop();
            mWatcher = null;

            SetStatus(Status.Ready);
        }

        private void CleanupInternal()
        {
            if (mStatus == Status.Scan)
            {
                StopScanInternal();
            }

            SetStatus(Status.Invalid);

            CleanupConnection();

            mCallback = null;
            mFinished = true;
        }

        private void CleanupConnection()
        {
            if (mConnectionCancelTokenSource != null)
            {
                mConnectionCancelTokenSource.Dispose();
                mConnectionCancelTokenSource = null;
            }

            mUploadCharacteristic = null;
            mDownloadCharacteristic = null;

            if (mService != null)
            {
                mService.Dispose();
                mService = null;
            }

            if (mDevice != null)
            {
                mDevice.Dispose();
                mDevice = null;
            }

            mSendBufferWriter.BaseStream.SetLength(0);
            mReceiveBufferReader.BaseStream.SetLength(0);
            mReceiveMessageSize = -1;
            mReceiveMessageAddress = -1;
            mValueWriting = false;
        }

        private void HandleError()
        {
            lock (mLockObject)
            {
                if (mStatus == Status.Connect)
                {
                    CleanupConnection();
                    SetStatus(Status.Ready);

                    mCallback.OnFail();
                }
                else if (mStatus == Status.Online)
                {
                    CleanupConnection();
                }
            }
        }

        private async void ConnectInternal(ulong address)
        {
            mConnectionCancelTokenSource = new CancellationTokenSource(ACCEPTANCE_TIMEOUT);

            try
            {
                mDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(mConnectionCancelTokenSource.Token);
                if (mDevice == null)
                {
                    Utils.Error("failed to get bluetooth device");
                    HandleError();
                    return;
                }

                var servicesResult = await mDevice.GetGattServicesForUuidAsync(mServiceUUID, BluetoothCacheMode.Uncached).
                    AsTask(mConnectionCancelTokenSource.Token);
                if (servicesResult.Status != GattCommunicationStatus.Success)
                {
                    Utils.Error("failed to get services: {0}", servicesResult.Status.ToString());
                    HandleError();
                    return;
                }

                mService = servicesResult.Services.FirstOrDefault();
                if (mService == null)
                {
                    HandleError();
                    Utils.Info("communication service not found");
                    return;
                }

                var characteristicsResult = await mService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).
                    AsTask(mConnectionCancelTokenSource.Token);
                if (characteristicsResult.Status != GattCommunicationStatus.Success)
                {
                    Utils.Error("failed to get characteristics: {0}", characteristicsResult.Status.ToString());
                    HandleError();
                    return;
                }

                mUploadCharacteristic = characteristicsResult.Characteristics.Where(ch => ch.Uuid == mUploadUUID).FirstOrDefault();
                if (mUploadCharacteristic == null)
                {
                    Utils.Error("uploading characteristic not found");
                    HandleError();
                    return;
                }

                mDownloadCharacteristic = characteristicsResult.Characteristics.Where(ch => ch.Uuid == mDownloadUUID).FirstOrDefault();
                if (mDownloadCharacteristic == null)
                {
                    Utils.Error("downloading characteristic not found");
                    HandleError();
                    return;
                }

                mDownloadCharacteristic.ValueChanged += OnCharacteristicValueChanged;

                var configResult = await mDownloadCharacteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Indicate).AsTask(mConnectionCancelTokenSource.Token);
                if (configResult.Status != GattCommunicationStatus.Success)
                {
                    Utils.Error("failed to write characteristic configuration: {0}", configResult.Status.ToString());
                    HandleError();
                    return;
                }

                var writeResult = await mUploadCharacteristic.WriteValueWithResultAsync(new byte[0].AsBuffer(), GattWriteOption.WriteWithResponse).
                    AsTask(mConnectionCancelTokenSource.Token);
                if (writeResult.Status != GattCommunicationStatus.Success)
                {
                    Utils.Error("failed to write characteristic value: {0}", writeResult.Status.ToString());
                    HandleError();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                Utils.Info("canceled");
                HandleError();
                return;
            }
            catch (Exception e)
            {
                Utils.Error(e.ToString());
                HandleError();
                return;
            }

            lock (mLockObject)
            {
                mConnectionCancelTokenSource.Token.Register(() =>
                {
                    CleanupConnection();
                });

                mDevice.ConnectionStatusChanged += OnConnectionStatusChanged;
                mDevice.GattServicesChanged += OnGattServicesChanged;

                SetStatus(Status.Online);

                mCallback.OnConnect();
            }
        }

        private async void ProcessSendBuffer()
        {
            byte[] value;

            lock (mLockObject)
            {
                mSendBufferWriter.BaseStream.Seek(0, SeekOrigin.Begin);

                int size = Math.Min((int)mSendBufferWriter.BaseStream.Length, mService.Session.MaxPduSize - 3);
                value = new byte[size];

                mSendBufferWriter.BaseStream.Read(value, 0, size);

                Utils.CompactStream(mSendBufferWriter.BaseStream);

                Utils.Info("send: {0} bytes remain {1} bytes", size, mSendBufferWriter.BaseStream.Length);

                mValueWriting = true;
            }

            var writeResult = await mUploadCharacteristic.WriteValueWithResultAsync(value.AsBuffer()).AsTask();

            lock (mLockObject)
            {
                mValueWriting = false;

                if (writeResult.Status != GattCommunicationStatus.Success)
                {
                    Utils.Error("failed to write characteristic value: {0}", writeResult.Status.ToString());
                    HandleError();
                    return;
                }

                if ((mStatus == Status.Online) && (mSendBufferWriter.BaseStream.Length > 0))
                {
                    ProcessSendBuffer();
                }
            }
        }

        private async void ProcessReceiveBuffer(byte[] value)
        {
            Utils.Info("received: {0} bytes remain {1} bytes", value.Length - 1, mReceiveBufferReader.BaseStream.Length);

            lock (mLockObject)
            {
                mReceiveBufferReader.BaseStream.Write(value, 0, value.Length - 1);

                if (mReceiveBufferReader.BaseStream.Length > BUFFER_SIZE)
                {
                    Utils.Error("receive buffer overflow");
                    HandleError();
                    return;
                }

                mReceiveBufferReader.BaseStream.Seek(0, SeekOrigin.Begin);

                bool willContinue = (value[value.Length - 1] != 0);

                while (true)
                {
                    if (mReceiveMessageSize == -1)
                    {
                        if (mReceiveBufferReader.BaseStream.Length - mReceiveBufferReader.BaseStream.Position < 2)
                        {
                            break;
                        }

                        mReceiveMessageSize = mReceiveBufferReader.ReadUInt16();
                    }

                    if (mReceiveMessageAddress == -1)
                    {
                        if (mReceiveBufferReader.BaseStream.Length - mReceiveBufferReader.BaseStream.Position < 2)
                        {
                            break;
                        }

                        mReceiveMessageAddress = mReceiveBufferReader.ReadUInt16();
                    }

                    if (mReceiveBufferReader.BaseStream.Length - mReceiveBufferReader.BaseStream.Position < mReceiveMessageSize)
                    {
                        break;
                    }

                    var message = mReceiveBufferReader.ReadBytes(mReceiveMessageSize);
                    int sender = mReceiveMessageAddress;

                    mReceiveMessageSize = -1;
                    mReceiveMessageAddress = -1;

                    mCallback.OnReceive(message, sender);
                }

                Utils.CompactStream(mReceiveBufferReader.BaseStream);

                if (!willContinue)
                {
                    return;
                }
            }

            var readResult = await mDownloadCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask();

            lock (mLockObject)
            {
                if (readResult.Status != GattCommunicationStatus.Success)
                {
                    Utils.Error("failed to read characteristic value: {0}", readResult.Status.ToString());
                    HandleError();
                    return;
                }

                if ((mStatus == Status.Online) && (readResult.Value.Length > 1))
                {
                    ProcessReceiveBuffer(readResult.Value.AsBytes());
                }
            }
        }

        // BluetoothLEAdvertisementWatcher

        private void OnWatcherReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Utils.Info("OnWatcherReceived: {0}", args.Advertisement.ManufacturerData.Count);

            lock (mLockObject)
            {
                if (mStatus != Status.Scan)
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return;
                }

                if (mDiscoveredPeripherals.Exists(ctx => ctx.Address == args.BluetoothAddress))
                {
                    return;
                }

                string deviceName = args.Advertisement.LocalName;
                if (string.IsNullOrEmpty(deviceName))
                {
                    // HACK: ScanResponseが取得できないので暫定
                    deviceName = args.BluetoothAddress.ToString();

                    /*
                    Utils.Error("device-name not exists");
                    return;
                    */
                }

                var context = new PeripheralContext(args.BluetoothAddress, mNextPeripheralId++, deviceName);
                mDiscoveredPeripherals.Add(context);

                Utils.Info("discover deviceName: {0} deviceId: {1}", context.Name, context.Id);

                mCallback.OnDiscover(context.Name, context.Id);
            }
        }

        private void OnWatcherStopped(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            // Utils.Info("OnWatcherStopped: {0}", args.Error.ToString());

            lock (mLockObject)
            {
                if (mStatus != Status.Scan)
                {
                    return;
                }

                Utils.Error("error: {0}", args.Error.ToString());

                if (args.Error == BluetoothError.RadioNotAvailable)
                {
                    mCallback.OnBluetoothRequire();
                }

                StopScanInternal();

                mCallback.OnFail();
            }
        }

        // BluetoothLEDevice

        private void OnConnectionStatusChanged(BluetoothLEDevice device, object args)
        {
            // Utils.Info("OnConnectionStatusChanged: {0}", device.ConnectionStatus);

            lock (mLockObject)
            {
                if (device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    Utils.Info("connected");
                    return;
                }

                if ((mStatus != Status.Online) && (mStatus != Status.Disconnect))
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return;
                }

                CleanupConnection();
                SetStatus(Status.Ready);

                Utils.Info("disconnected");

                mCallback.OnDisconnect();
            }
        }

        private void OnGattServicesChanged(BluetoothLEDevice device, object args)
        {
            // Utils.Info("OnGattServicesChanged");

            lock (mLockObject)
            {
                if (mStatus == Status.Online) // サービス変更された場合は強制切断する
                {
                    Utils.Info("services changed");

                    CleanupConnection();
                }
            }
        }

        // GattCharacteristic

        private void OnCharacteristicValueChanged(GattCharacteristic characteristic, GattValueChangedEventArgs args)
        {
            // Utils.Info("OnCharacteristicValueChanged: {0} bytes", args.CharacteristicValue.Length);

            lock (mLockObject)
            {
                if (mStatus != Status.Online)
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    HandleError();
                    return;
                }

                if ((args.CharacteristicValue == null) || (args.CharacteristicValue.Length < 1))
                {
                    Utils.Error("invalid value");
                    return;
                }

                ProcessReceiveBuffer(args.CharacteristicValue.AsBytes());
            }
        }

        // IWcfCentralHost

        bool IWcfCentralHost.Initialize(string serviceUUID, string uploadUUID, string downloadUUID)
        {
            Utils.Info("Initialize: {0}, {1}, {2}", serviceUUID, uploadUUID, downloadUUID);

            lock (mLockObject)
            {
                if (mStatus != Status.Invalid)
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return false;
                }

                try
                {
                    mCallback = OperationContext.Current.GetCallbackChannel<IWcfCentralCallback>();

                    mServiceUUID = new Guid(serviceUUID);
                    mUploadUUID = new Guid(uploadUUID);
                    mDownloadUUID = new Guid(downloadUUID);
                }
                catch (Exception e)
                {
                    Utils.Error(e.ToString());
                    CleanupInternal();
                    return false;
                }

                SetStatus(Status.Ready);

                mCallback.OnReady();
            }

            return true;
        }

        bool IWcfCentralHost.StartScan()
        {
            Utils.Info("StartScan");

            lock (mLockObject)
            {
                if (mStatus == Status.Scan)
                {
                    Utils.Info("already scanning");
                    return true;
                }

                if (mStatus != Status.Ready)
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return false;
                }

                mWatcher = new BluetoothLEAdvertisementWatcher();
                mWatcher.ScanningMode = BluetoothLEScanningMode.Active;
                mWatcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(mServiceUUID);
                mWatcher.Received += OnWatcherReceived;
                mWatcher.Stopped += OnWatcherStopped;
                mWatcher.Start();

                mDiscoveredPeripherals.Clear();

                SetStatus(Status.Scan);
            }

            return true;
        }

        void IWcfCentralHost.StopScan()
        {
            Utils.Info("StopScan");

            lock (mLockObject)
            {
                StopScanInternal();
            }
        }

        bool IWcfCentralHost.Connect(int deviceId)
        {
            Utils.Info("Connect: {0}", deviceId);

            lock (mLockObject)
            {
                if ((mStatus != Status.Ready) && (mStatus != Status.Scan))
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return false;
                }

                var context = mDiscoveredPeripherals.Where(ctx => ctx.Id == deviceId).FirstOrDefault();
                if (context == null)
                {
                    Utils.Error("invalid deviceId: {0}", deviceId);
                    return false;
                }

                if (mStatus == Status.Scan)
                {
                    StopScanInternal();
                }

                SetStatus(Status.Connect);

                ConnectInternal(context.Address);
           }

            return true;
        }

        void IWcfCentralHost.Accept()
        {
            Utils.Info("Accept");

            lock (mLockObject)
            {
                if (mStatus != Status.Online)
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return;
                }

                if (mConnectionCancelTokenSource != null)
                {
                    mConnectionCancelTokenSource.Dispose();
                    mConnectionCancelTokenSource = null;
                }
            }
        }

        void IWcfCentralHost.Disconnect()
        {
            Utils.Info("Disconnect");

            lock (mLockObject)
            {
                if (mStatus == Status.Connect)
                {
                    mConnectionCancelTokenSource.Cancel();
                }
                else if (mStatus == Status.Online)
                {
                    CleanupConnection();
                    SetStatus(Status.Disconnect);
                }
                else
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                }
            }
        }


        void IWcfCentralHost.Send(byte[] message, int messageSize, int receiver)
        {
            // Utils.Info("Send: {0} bytes to {1}", messageSize, receiver);

            lock (mLockObject)
            {
                if (mStatus != Status.Online)
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return;
                }

                if (message == null)
                {
                    Utils.Error("message is null");
                    return;
                }

                if ((messageSize < 0) || (messageSize > message.Length))
                {
                    Utils.Error("invalid message size");
                    return;
                }

                if (messageSize > MESSAGE_SIZE_MAX)
                {
                    Utils.Error("message size too large");
                    return;
                }

                mSendBufferWriter.Write((short)messageSize);
                mSendBufferWriter.Write((short)receiver);
                mSendBufferWriter.Write(message, 0, messageSize);
                mSendBufferWriter.Flush();

                if (mSendBufferWriter.BaseStream.Length > BUFFER_SIZE)
                {
                    Utils.Error("send buffer overflow");
                    HandleError();
                    return;
                }

                if (!mValueWriting)
                {
                    ProcessSendBuffer();
                }
            }
        }

        void IWcfCentralHost.Cleanup()
        {
            Utils.Info("Cleanup");

            lock (mLockObject)
            {
                CleanupInternal();
            }
        }
    }
}
