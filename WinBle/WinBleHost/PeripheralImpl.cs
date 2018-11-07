using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace BleSock.Windows
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Multiple)]
    internal class PeripheralImpl : IWcfPeripheralHost, IDisposable
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
            string address = string.Format("net.tcp://localhost:{0}/WcfPeripheralHost", portNumber);

            mServiceHost = new ServiceHost(this);
            mServiceHost.AddServiceEndpoint(typeof(IWcfPeripheralHost), new CustomBinding(bindingElements), address);
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
            Initialize,
            Ready,
            Advertise,
        }

        private ServiceHost mServiceHost;
        private bool mFinished;

        private object mLockObject = new object();
        private Status mStatus = Status.Invalid;

        private IWcfPeripheralCallback mCallback;
        private Guid mServiceUUID;
        private Guid mUploadUUID;
        private Guid mDownloadUUID;

        private CancellationTokenSource mInitializationCancelTokenSource;
        private GattServiceProvider mServiceProvider;
        private GattLocalCharacteristic mUploadCharacteristic;
        private GattLocalCharacteristic mDownloadCharacteristic;

        private class CentralContext
        {
            public readonly GattSubscribedClient client;
            public int connectionId = 0;
            public int playerId = 0;
            public readonly BinaryWriter sendBufferWriter = new BinaryWriter(new MemoryStream());
            public readonly BinaryReader receiveBufferReader = new BinaryReader(new MemoryStream());
            public int receiveMessageSize = -1;
            public int receiveMessageAddress = -1;
            public bool valueWriting = false;

            public CentralContext(GattSubscribedClient client)
            {
                this.client = client;
            }
        }

        private List<CentralContext> mSubscribedCentrals = new List<CentralContext>();
        private int mNextConnectionId = 1;



        private void SetStatus(Status status)
        {
            if (mStatus != status)
            {
                Utils.Info("status: {0}", status.ToString());
                mStatus = status;
            }
        }

        private void CleanupInternal()
        {
            if (mStatus == Status.Advertise)
            {
                mServiceProvider.StopAdvertising();
            }

            SetStatus(Status.Invalid);

            if (mInitializationCancelTokenSource != null)
            {
                mInitializationCancelTokenSource.Dispose();
                mInitializationCancelTokenSource = null;
            }

            mUploadCharacteristic = null;
            mDownloadCharacteristic = null;
            mServiceProvider = null;

            mSubscribedCentrals.Clear();


            // TODO


            mCallback = null;
            mFinished = true;
        }

        private void HandleError()
        {
            mUploadCharacteristic = null;
            mDownloadCharacteristic = null;
            mServiceProvider = null;

            mCallback.OnFail();
        }

        private async void InitializeInternal()
        {
            mInitializationCancelTokenSource = new CancellationTokenSource(ACCEPTANCE_TIMEOUT);

            try
            {
                var serviceProviderResult = await GattServiceProvider.CreateAsync(mServiceUUID).AsTask(mInitializationCancelTokenSource.Token);
                if (serviceProviderResult.Error != BluetoothError.Success)
                {
                    Utils.Error("failed to create service provider: {0}", serviceProviderResult.Error.ToString());

                    if (serviceProviderResult.Error == BluetoothError.RadioNotAvailable)
                    {
                        mCallback.OnBluetoothRequire();
                    }

                    HandleError();
                    return;
                }

                mServiceProvider = serviceProviderResult.ServiceProvider;
                mServiceProvider.AdvertisementStatusChanged += OnAdvertisementStatusChanged;

                var uploadParameters = new GattLocalCharacteristicParameters()
                {
                    CharacteristicProperties = GattCharacteristicProperties.Write,
                };

                var uploadCharacteristicResult = await mServiceProvider.Service.CreateCharacteristicAsync(mUploadUUID, uploadParameters).
                    AsTask(mInitializationCancelTokenSource.Token);
                if (uploadCharacteristicResult.Error != BluetoothError.Success)
                {
                    Utils.Error("failed to create uploading characteristic: {0}", uploadCharacteristicResult.Error.ToString());
                    HandleError();
                    return;
                }

                mUploadCharacteristic = uploadCharacteristicResult.Characteristic;
                mUploadCharacteristic.WriteRequested += OnCharacteristicWriteRequested;

                var downloadParameters = new GattLocalCharacteristicParameters
                {
                    CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Indicate,
                };

                var downloadCharacteristicResult = await mServiceProvider.Service.CreateCharacteristicAsync(mDownloadUUID, downloadParameters).
                    AsTask(mInitializationCancelTokenSource.Token);
                if (downloadCharacteristicResult.Error != BluetoothError.Success)
                {
                    Utils.Error("failed to create downloading characteristic: {0}", downloadCharacteristicResult.Error.ToString());
                    HandleError();
                    return;
                }

                mDownloadCharacteristic = downloadCharacteristicResult.Characteristic;
                mDownloadCharacteristic.SubscribedClientsChanged += OnCharacteristicSubscribedClientsChanged;
                mDownloadCharacteristic.ReadRequested += OnCharacteristicReadRequested;
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
                SetStatus(Status.Ready);

                mCallback.OnReady();
            }
        }

        private void RemoveCentral(CentralContext context)
        {
            mSubscribedCentrals.Remove(context);


            // TODO: timeout


            if (context.connectionId != 0)
            {
                mCallback.OnDisconnect(context.connectionId);
            }
        }

        private bool SendInternal(CentralContext context, byte[] message, int messageSize, int address)
        {
            context.sendBufferWriter.Write((short)messageSize);
            context.sendBufferWriter.Write((short)address);
            context.sendBufferWriter.Write(message, 0, messageSize);
            context.sendBufferWriter.Flush();

            if (context.sendBufferWriter.BaseStream.Length > BUFFER_SIZE)
            {
                Utils.Error("send buffer overflow");
                return false;
            }

            if (!context.valueWriting)
            {
                SendNotification(context);
            }

            return true;
        }

        private async void SendNotification(CentralContext context)
        {
            Utils.Info("MaxNotificationSize: {0}", context.client.MaxNotificationSize); // ** TEST **

            var value = ProcessSendBuffer(context, context.client.MaxNotificationSize - 3);

            Utils.Info("notifyValue: {0} bytes remain {1} bytes", value.Length, context.sendBufferWriter.BaseStream.Length);

            await mDownloadCharacteristic.NotifyValueAsync(value.AsBuffer(), context.client).AsTask();
        }

        private byte[] ProcessSendBuffer(CentralContext context, int maxSize)
        {
            byte[] value;

            context.sendBufferWriter.BaseStream.Seek(0, SeekOrigin.Begin);

            int size = Math.Min((int)context.sendBufferWriter.BaseStream.Length, maxSize - 1);
            value = new byte[size + 1];

            context.sendBufferWriter.BaseStream.Read(value, 0, size);

            Utils.CompactStream(context.sendBufferWriter.BaseStream);

            if (context.sendBufferWriter.BaseStream.Length > 0)
            {
                value[size] = 1;
                context.valueWriting = true;
            }
            else
            {
                value[size] = 0;
                context.valueWriting = false;
            }

            return value;
        }

        private void ProcessReceiveBuffer(CentralContext context, byte[] value)
        {
            Utils.Info("received: {0} bytes remain {1} bytes {2}", value.Length, context.receiveBufferReader.BaseStream.Length, context.client.Session.DeviceId.Id);

            context.receiveBufferReader.BaseStream.Write(value, 0, value.Length - 1);

            if (context.receiveBufferReader.BaseStream.Length > BUFFER_SIZE)
            {
                Utils.Error("receive buffer overflow");
                RemoveCentral(context);
                return;
            }

            context.receiveBufferReader.BaseStream.Seek(0, SeekOrigin.Begin);

            while (true)
            {
                if (context.receiveMessageSize == -1)
                {
                    if (context.receiveBufferReader.BaseStream.Length - context.receiveBufferReader.BaseStream.Position < 2)
                    {
                        break;
                    }

                    context.receiveMessageSize = context.receiveBufferReader.ReadUInt16();

                    if (context.receiveMessageSize > MESSAGE_SIZE_MAX)
                    {
                        Utils.Error("invalid message size: {0}", context.receiveMessageSize);
                        RemoveCentral(context);
                        return;
                    }
                }

                if (context.receiveMessageAddress == -1)
                {
                    if (context.receiveBufferReader.BaseStream.Length - context.receiveBufferReader.BaseStream.Position < 2)
                    {
                        break;
                    }

                    context.receiveMessageAddress = context.receiveBufferReader.ReadUInt16();
                }

                if (context.receiveBufferReader.BaseStream.Length - context.receiveBufferReader.BaseStream.Position < context.receiveMessageSize)
                {
                    break;
                }

                var message = context.receiveBufferReader.ReadBytes(context.receiveMessageSize);
                int to = context.receiveMessageAddress;
                context.receiveMessageSize = -1;
                context.receiveMessageAddress = -1;

                if (context.playerId != 0)
                {
                    int index = 0;
                    while (index < mSubscribedCentrals.Count)
                    {
                        var ctx = mSubscribedCentrals[index];
                        if ((ctx.playerId & to) != 0)
                        {
                            if (!SendInternal(ctx, message, message.Length, context.playerId))
                            {
                                RemoveCentral(ctx);
                            }
                            else
                            {
                                ++index;
                            }
                        }
                    }

                    if ((to & 1) != 0)
                    {
                        mCallback.OnReceive(message, context.playerId);
                    }
                }

                if (to == 0)
                {
                    mCallback.OnReceiveDirect(message, context.connectionId);
                }
            }

            Utils.CompactStream(context.receiveBufferReader.BaseStream);
        }

        // GattServiceProvider

        private void OnAdvertisementStatusChanged(GattServiceProvider serviceProvider, GattServiceProviderAdvertisementStatusChangedEventArgs args)
        {
            Utils.Info("AdvertisementStatusChanged error: {0} status: {1}", args.Error.ToString(), args.Status.ToString());

            if (mStatus != Status.Advertise)
            {
                return;
            }

            if (args.Status == GattServiceProviderAdvertisementStatus.Started)
            {
                Utils.Info("advertising..");
            }
            else if (args.Error != BluetoothError.Success)
            {
                Utils.Error("error: {0}", args.Error.ToString());

                if (args.Error == BluetoothError.RadioNotAvailable)
                {
                    mCallback.OnBluetoothRequire();
                }

                mServiceProvider.StopAdvertising();

                SetStatus(Status.Ready);

                mCallback.OnFail();
            }
        }

        // GattLocalCharacteristic

        private async void OnCharacteristicWriteRequested(GattLocalCharacteristic characteristic, GattWriteRequestedEventArgs args)
        {
            Utils.Info("OnWriteRequested: {0}", args.Session.DeviceId.Id);

            using (var derferral = args.GetDeferral())
            {
                var request = await args.GetRequestAsync().AsTask();

                lock (mLockObject)
                {
                    if ((mStatus != Status.Ready) && (mStatus != Status.Advertise))
                    {
                        Utils.Error("invalid status: {0}", mStatus.ToString());
                        request.RespondWithProtocolError(GattProtocolError.RequestNotSupported);
                        return;
                    }

                    var context = mSubscribedCentrals.Where(ctx => ctx.client.Session.DeviceId.Id == args.Session.DeviceId.Id).FirstOrDefault();
                    if (context == null)
                    {
                        Utils.Error("not subscribed");
                        request.RespondWithProtocolError(GattProtocolError.InsufficientAuthentication);
                        return;
                    }

                    if ((request.Option != GattWriteOption.WriteWithResponse) || (request.Offset != 0) || (request.Value == null))
                    {
                        Utils.Error("invalid parameter");
                        request.RespondWithProtocolError(GattProtocolError.InvalidPdu);
                        return;
                    }

                    request.Respond();

                    if (context.connectionId == 0) // Negotiation complete
                    {
                        Utils.Info("negotiation complete");

                        context.connectionId = mNextConnectionId++;

                        mCallback.OnConnect(context.connectionId);
                    }
                    else
                    {
                        ProcessReceiveBuffer(context, request.Value.AsBytes());
                    }
                }
            }
        }

        private void OnCharacteristicSubscribedClientsChanged(GattLocalCharacteristic characteristic, object args)
        {
            Utils.Info("OnCharacteristicSubscribedClientsChanged");

            // TODO
            var unsubscribedCentrals = mSubscribedCentrals.Where(ctx => !characteristic.SubscribedClients.Contains(ctx.client)).ToList(); // Unsubscribed
            foreach (var context in unsubscribedCentrals)
            {
                Utils.Info("unsubscribed: {0}", context.client.Session.DeviceId.Id);
                RemoveCentral(context);
            }

            foreach (var client in characteristic.SubscribedClients)
            {
                if (!mSubscribedCentrals.Exists(ctx => { return ctx.client == client; })) // Subscribed
                {
                    Utils.Info("subscribed: {0}", client.Session.DeviceId.Id);

                    client.Session.SessionStatusChanged += OnSessionStatusChanged;

                    var context = new CentralContext(client);

                    
                    // TODO: timeout


                    mSubscribedCentrals.Add(context);
                }
            }
        }

        private async void OnCharacteristicReadRequested(GattLocalCharacteristic characteristic, GattReadRequestedEventArgs args)
        {
            Utils.Info("OnReadRequested: {0}", args.Session.DeviceId.Id);

            using (var deferral = args.GetDeferral())
            {
                var request = await args.GetRequestAsync().AsTask();

                Utils.Info("length: {0} offset: {1}", request.Length, request.Offset);

                lock (mLockObject)
                {
                    if ((mStatus != Status.Ready) && (mStatus != Status.Advertise))
                    {
                        Utils.Error("invalid status: {0}", mStatus.ToString());
                        request.RespondWithProtocolError(GattProtocolError.RequestNotSupported);
                        return;
                    }

                    var context = mSubscribedCentrals.Where(ctx => ctx.client.Session.DeviceId.Id == args.Session.DeviceId.Id).FirstOrDefault();
                    if (context == null)
                    {
                        Utils.Error("not subscribed");
                        request.RespondWithProtocolError(GattProtocolError.InsufficientAuthentication);
                        return;
                    }

                    if (request.Offset != 0)
                    {
                        Utils.Error("invalid parameter");
                        request.RespondWithProtocolError(GattProtocolError.InvalidPdu);
                        return;
                    }

                    byte[] value;

                    if (context.sendBufferWriter.BaseStream.Length > 0)
                    {
                        value = ProcessSendBuffer(context, (int)request.Length);
                        Utils.Info("respond: {0} bytes remain {1} bytes {2}", value.Length, context.sendBufferWriter.BaseStream.Length, context.client.Session.DeviceId.Id);
                    }
                    else
                    {
                        Utils.Info("respond: empty");
                        value = new byte[0];
                        context.valueWriting = false;
                    }

                    request.RespondWithValue(value.AsBuffer());
                }
            }
        }

        // GattSession

        private void OnSessionStatusChanged(GattSession session, GattSessionStatusChangedEventArgs args)
        {
            Utils.Info("OnSessionStatusChanged status: {0} error: {1}", args.Status.ToString(), args.Error.ToString());

            if (args.Status != GattSessionStatus.Closed)
            {
                return;
            }

            var context = mSubscribedCentrals.Where(ctx => ctx.client.Session.DeviceId.Id == session.DeviceId.Id).FirstOrDefault();
            if (context != null)
            {
                Utils.Info("disconnected: {0}", session.DeviceId.Id);
                RemoveCentral(context);
            }
        }

        // IWcfPeripheralHost

        bool IWcfPeripheralHost.Initialize(string serviceUUID, string uploadUUID, string downloadUUID)
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
                    mCallback = OperationContext.Current.GetCallbackChannel<IWcfPeripheralCallback>();

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

                SetStatus(Status.Initialize);

                InitializeInternal();
            }

            return true;
        }

        bool IWcfPeripheralHost.StartAdvertising(string deviceName)
        {
            Utils.Info("StartAdvertising: {0}", deviceName); // ただしWindows10ではdeviceNameは反映できないのであった

            lock (mLockObject)
            {
                if (mStatus == Status.Advertise)
                {
                    Utils.Error("already advertising");
                    return false;
                }

                if (mStatus != Status.Ready)
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return false;
                }

                var advertisingParameters = new GattServiceProviderAdvertisingParameters
                {
                    IsDiscoverable = true,
                    IsConnectable = true,
                };

                mServiceProvider.StartAdvertising(advertisingParameters);

                SetStatus(Status.Advertise);
            }

            return true;
        }

        void IWcfPeripheralHost.StopAdvertising()
        {
            Utils.Info("StopAdvertising");

            lock (mLockObject)
            {
                if (mStatus != Status.Advertise)
                {
                    Utils.Error("invalid status: {0}", mStatus.ToString());
                    return;
                }

                mServiceProvider.StopAdvertising();

                SetStatus(Status.Ready);
            }
        }

        bool IWcfPeripheralHost.Accept(int connectionId, int playerId)
        {
            Utils.Info("Accept connectionId: {0} playerId: {1}", connectionId, playerId);

            lock (mLockObject)
            {
                var context = mSubscribedCentrals.Where(ctx => ctx.connectionId == connectionId).FirstOrDefault();
                if (context == null)
                {
                    Utils.Error("invalid connectionId: {0}", connectionId);
                    return false;
                }

                if (context.playerId != 0)
                {
                    Utils.Info("already accepted");
                }

                context.playerId = playerId;


                // TODO: timeout


            }

            return true;
        }

        void IWcfPeripheralHost.Invalidate(int connectionId)
        {
            Utils.Info("Invalidate connectionId: {0}", connectionId);

            lock (mLockObject)
            {
                var context = mSubscribedCentrals.Where(ctx => ctx.connectionId == connectionId).FirstOrDefault();
                if (context == null)
                {
                    Utils.Error("invalid connectionId: {0}", connectionId);
                    return;
                }

                RemoveCentral(context);
            }
        }

        void IWcfPeripheralHost.SendDirect(byte[] message, int messageSize, int connectionId)
        {
            lock (mLockObject)
            {
                if ((mStatus != Status.Ready) && (mStatus != Status.Advertise))
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

                var context = mSubscribedCentrals.Where(ctx => (ctx.connectionId != 0) && (ctx.connectionId == connectionId)).FirstOrDefault();
                if (context == null)
                {
                    Utils.Error("invalid connectionId: {0}", connectionId);
                    return;
                }

                if (!SendInternal(context, message, messageSize, 0))
                {
                    RemoveCentral(context);
                }
            }
        }

        void IWcfPeripheralHost.Send(byte[] message, int messageSize, int receiver)
        {
            lock (mLockObject)
            {
                if ((mStatus != Status.Ready) && (mStatus != Status.Advertise))
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
                    Utils.Error("invalid message-size");
                    return;
                }

                if (messageSize > MESSAGE_SIZE_MAX)
                {
                    Utils.Error("message-size too large");
                    return;
                }

                int index = 0;
                while (index < mSubscribedCentrals.Count)
                {
                    var context = mSubscribedCentrals[index];
                    if ((context.playerId & receiver) != 0)
                    {
                        if (!SendInternal(context, message, messageSize, 1))
                        {
                            RemoveCentral(context);
                        }
                        else
                        {
                            ++index;
                        }
                    }
                }
            }
        }

        void IWcfPeripheralHost.Cleanup()
        {
            Utils.Info("Cleanup");

            lock (mLockObject)
            {
                CleanupInternal();
            }
        }
    }
}
