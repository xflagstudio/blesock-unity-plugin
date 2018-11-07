using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.UI;

public class ChatTest : MonoBehaviour
{
    public GameObject modeSelectObject;
    public InputField playerNameInputField;
    public Button hostButton;
    public Button joinButton;

    public GameObject hostObject;
    public Button advertiseButton;
    public Button stopButton;

    public GameObject guestObject;
    public Dropdown devicesDropdown;
    public Button connectButton;
    public Button disconnectButton;

    public GameObject chatObject;
    public Text statusText;
    public Text logsText;
    public InputField sendInputField;
    public Button backButton;


    private const string PROTOCOL_IDENTIFIER = "ChatTest";


    private class DeviceOptionData : Dropdown.OptionData
    {
        public int deviceId { get; private set; }

        public DeviceOptionData(string deviceName, int deviceId)
        {
            text = deviceName;
            this.deviceId = deviceId;
        }
    }


    private BleSock.PeerBase mPeer;
    private List<string> mLogs = new List<string>();


    private void Start()
    {
        modeSelectObject.SetActive(true);

        playerNameInputField.onEndEdit.AddListener((name) =>
        {
            bool interactable = !string.IsNullOrEmpty(name);
            hostButton.interactable = interactable;
            joinButton.interactable = interactable;
        });

        // Host

        hostObject.SetActive(false);

        hostButton.interactable = false;
        hostButton.onClick.AddListener(() =>
        {
            modeSelectObject.SetActive(false);
            hostObject.SetActive(true);
            chatObject.SetActive(true);

            advertiseButton.interactable = false;
            stopButton.interactable = false;

            backButton.interactable = true;

            var host = new BleSock.HostPeer();

            host.onReady += () =>
            {
                Log("初期化が完了しました");
                advertiseButton.interactable = true;
                sendInputField.interactable = true;
            };

            host.onBluetoothRequire += () =>
            {
                Log("Bluetoothを有効にしてください");
            };

            host.onFail += () =>
            {
                Log("失敗しました");
            };

            host.onPlayerJoin += (player) =>
            {
                Log("{0} が参加しました", player.PlayerName);
            };

            host.onPlayerLeave += (player) =>
            {
                Log("{0} が離脱しました", player.PlayerName);
            };

            host.onReceive += (message, messageSize, sender) =>
            {
                Log("{0}: {1}", sender.PlayerName, Encoding.UTF8.GetString(message, 0, messageSize));
            };

            try
            {
                host.Initialize(PROTOCOL_IDENTIFIER, playerNameInputField.text);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("初期化できません");
                return;
            }

            Log("初期化しています..");
            mPeer = host;
        });

        advertiseButton.onClick.AddListener(() =>
        {
            try
            {
                ((BleSock.HostPeer)mPeer).StartAdvertising(playerNameInputField.text);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("アドバタイズ開始できません");
                return;
            }

            Log("アドバタイズしています..");
            advertiseButton.interactable = false;
            stopButton.interactable = true;
        });

        stopButton.onClick.AddListener(() =>
        {
            ((BleSock.HostPeer)mPeer).StopAdvertising();

            Log("アドバタイズを停止しました");
            advertiseButton.interactable = true;
            stopButton.interactable = false;
        });

        // Guest

        guestObject.SetActive(false);

        joinButton.interactable = false;
        joinButton.onClick.AddListener(() =>
        {
            modeSelectObject.SetActive(false);
            guestObject.SetActive(true);
            chatObject.SetActive(true);

            devicesDropdown.interactable = false;
            connectButton.interactable = false;
            disconnectButton.interactable = false;

            backButton.interactable = true;

            var guest = new BleSock.GuestPeer();

            guest.onBluetoothRequire += () =>
            {
                Log("Bluetoothを有効にしてください");
            };

            guest.onReady += () =>
            {
                Log("初期化が完了しました");

                try
                {
                    guest.StartScan();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Log("スキャン開始できません");
                    return;
                }

                Log("デバイスを探索しています..");
                devicesDropdown.ClearOptions();
            };

            guest.onFail += () =>
            {
                Log("失敗しました");
            };

            guest.onDiscover += (deviceName, deviceId) =>
            {
                Log("デバイスを発見: {0} [{1}]", deviceName, deviceId);
                devicesDropdown.options.Add(new DeviceOptionData(deviceName, deviceId));

                if (!devicesDropdown.interactable)
                {
                    devicesDropdown.interactable = true;
                    devicesDropdown.value = 0;
                    devicesDropdown.RefreshShownValue();
                    connectButton.interactable = true;
                }
            };

            guest.onConnect += () =>
            {
                Log("接続されました");

                foreach (var player in guest.Players)
                {
                    Log(player.PlayerName);
                }

                sendInputField.interactable = true;
            };

            guest.onDisconnect += () =>
            {
                Log("切断されました");
                sendInputField.interactable = false;
                disconnectButton.interactable = false;
            };

            guest.onPlayerJoin += (player) =>
            {
                Log("{0} が参加しました", player.PlayerName);
            };

            guest.onPlayerLeave += (player) =>
            {
                Log("{0} が離脱しました", player.PlayerName);
            };

            guest.onReceive += (message, messageSize, sender) =>
            {
                Log("{0}: {1}", sender.PlayerName, Encoding.UTF8.GetString(message, 0, messageSize));
            };

            try
            {
                guest.Initialize(PROTOCOL_IDENTIFIER, playerNameInputField.text);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("初期化できません");
                return;
            }

            Log("初期化しています..");
            mPeer = guest;
        });

        connectButton.onClick.AddListener(() =>
        {
            var optionData = (DeviceOptionData)devicesDropdown.options[devicesDropdown.value];
            try
            {
                ((BleSock.GuestPeer)mPeer).Connect(optionData.deviceId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("エラー");
                return;
            }

            Log("デバイスに接続しています..");
            devicesDropdown.interactable = false;
            connectButton.interactable = false;
            disconnectButton.interactable = true;
        });

        disconnectButton.onClick.AddListener(() =>
        {
            ((BleSock.GuestPeer)mPeer).Disconnect();
        });

        // Chat

        chatObject.SetActive(false);

        sendInputField.interactable = false;
        sendInputField.onEndEdit.AddListener((text) =>
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            try
            {
                mPeer.Send(bytes, bytes.Length, BleSock.Address.All);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("エラー");
                return;
            }

            sendInputField.text = "";
        });

        backButton.interactable = false;
        backButton.onClick.AddListener(() =>
        {
            if (mPeer != null)
            {
                mPeer.Dispose();
                mPeer = null;
            }

            mLogs.Clear();

            modeSelectObject.SetActive(true);
            hostObject.SetActive(false);
            guestObject.SetActive(false);
            chatObject.SetActive(false);
        });
    }

    private void Update()
    {
        if (mPeer != null)
        {
            statusText.text = string.Format("BluetoothEnabled: {0}", mPeer.IsBluetoothEnabled.ToString());
        }
    }

    private void OnDestroy()
    {
        if (mPeer != null)
        {
            mPeer.Dispose();
            mPeer = null;
        }
    }

    private void Log(string format, params object[] args)
    {
        mLogs.Add(string.Format(format, args));

        if (mLogs.Count > 12)
        {
            mLogs.RemoveAt(0);
        }

        var builder = new StringBuilder();
        foreach (var log in mLogs)
        {
            builder.AppendLine(log);
        }

        logsText.text = builder.ToString();
    }
}
