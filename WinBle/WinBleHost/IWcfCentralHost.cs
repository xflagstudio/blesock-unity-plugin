using System.ServiceModel;

namespace BleSock.Windows
{
    [ServiceContract(CallbackContract = typeof(IWcfCentralCallback))]
    internal interface IWcfCentralHost
    {
        [OperationContract(IsOneWay = false)]
        bool Initialize(string serviceUUID, string uploadUUID, string downloadUUID);

        [OperationContract(IsOneWay = false)]
        bool StartScan();

        [OperationContract(IsOneWay = true)]
        void StopScan();

        [OperationContract(IsOneWay = false)]
        bool Connect(int deviceId);

        [OperationContract(IsOneWay = true)]
        void Accept();

        [OperationContract(IsOneWay = true)]
        void Disconnect();

        [OperationContract(IsOneWay = true)]
        void Send(byte[] message, int messageSize, int receiver);

        [OperationContract(IsOneWay = true)]
        void Cleanup();
    }
}
