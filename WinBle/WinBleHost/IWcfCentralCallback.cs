using System.ServiceModel;

namespace BleSock.Windows
{
    [ServiceContract]
    internal interface IWcfCentralCallback
    {
        [OperationContract(IsOneWay = true)]
        void OnBluetoothRequire();

        [OperationContract(IsOneWay = true)]
        void OnReady();

        [OperationContract(IsOneWay = true)]
        void OnFail();

        [OperationContract(IsOneWay = true)]
        void OnDiscover(string deviceName, int deviceId);

        [OperationContract(IsOneWay = true)]
        void OnConnect();

        [OperationContract(IsOneWay = true)]
        void OnDisconnect();

        [OperationContract(IsOneWay = true)]
        void OnReceive(byte[] message, int sender);
    }
}
