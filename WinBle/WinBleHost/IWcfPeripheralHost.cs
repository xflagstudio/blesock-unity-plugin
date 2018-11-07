using System.ServiceModel;

namespace BleSock.Windows
{
    [ServiceContract(CallbackContract = typeof(IWcfPeripheralCallback))]
    internal interface IWcfPeripheralHost
    {
        [OperationContract(IsOneWay = false)]
        bool Initialize(string serviceUUID, string uploadUUID, string downloadUUID);

        [OperationContract(IsOneWay = false)]
        bool StartAdvertising(string deviceName);

        [OperationContract(IsOneWay = true)]
        void StopAdvertising();

        [OperationContract(IsOneWay = false)]
        bool Accept(int connectionId, int playerId);

        [OperationContract(IsOneWay = true)]
        void Invalidate(int connectionId);

        [OperationContract(IsOneWay = true)]
        void SendDirect(byte[] message, int messageSize, int connectionId);

        [OperationContract(IsOneWay = true)]
        void Send(byte[] message, int messageSize, int receiver);

        [OperationContract(IsOneWay = true)]
        void Cleanup();
    }
}
