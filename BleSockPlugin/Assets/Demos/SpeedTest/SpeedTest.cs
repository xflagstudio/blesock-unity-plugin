using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;
using UnityEngine.UI;

public class SpeedTest : MonoBehaviour
{
    public GameObject modeSelectObject;
    public InputField playerNameInputField;
    public Button hostButton;
    public Button joinButton;

    public GameObject connectObject;
    public Dropdown devicesDropdown;
    public Button connectButton;

    public GameObject testObject;
    public Text logsText;
    public Dropdown sizesDropdown;
    public Button testButton;
    public Button backButton;


    private const string PROTOCOL_IDENTIFIER = "SpeedTest";


    private class ValueOptionData : Dropdown.OptionData
    {
        public int value { get; private set; }

        public ValueOptionData(string text, int value)
        {
            this.text = text;
            this.value = value;
        }
    }


    private BleSock.PeerBase mPeer;
    private RandomNumberGenerator mRandom = RandomNumberGenerator.Create();
    private int mStartTime;
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

        hostButton.interactable = false;
        hostButton.onClick.AddListener(() =>
        {
            modeSelectObject.SetActive(false);
            testObject.SetActive(true);

            backButton.interactable = true;

            var host = new BleSock.HostPeer();
            host.MaximumPlayers = 2;

            host.onReady += () =>
            {
                Log("初期化が完了しました");

                try
                {
                    host.StartAdvertising(playerNameInputField.text);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Log("アドバタイズ開始できません");
                }

                Log("接続を待っています..");
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
                sizesDropdown.interactable = true;
                testButton.interactable = true;
            };

            host.onPlayerLeave += (player) =>
            {
                Log("{0} が離脱しました", player.PlayerName);
                sizesDropdown.interactable = false;
                testButton.interactable = false;
            };

            host.onReceive += OnReceive;

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

        // Guest

        connectObject.SetActive(false);

        joinButton.interactable = false;
        joinButton.onClick.AddListener(() =>
        {
            modeSelectObject.SetActive(false);
            connectObject.SetActive(true);
            testObject.SetActive(true);

            devicesDropdown.interactable = false;
            connectButton.interactable = false;

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
                devicesDropdown.options.Add(new ValueOptionData(deviceName, deviceId));

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
                sizesDropdown.interactable = true;
                testButton.interactable = true;
            };

            guest.onDisconnect += () =>
            {
                Log("切断されました");
                sizesDropdown.interactable = false;
                testButton.interactable = false;
            };

            guest.onPlayerJoin += (player) =>
            {
                Log("{0} が参加しました", player.PlayerName);
            };

            guest.onPlayerLeave += (player) =>
            {
                Log("{0} が離脱しました", player.PlayerName);
            };

            guest.onReceive += OnReceive;

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
            var optionData = (ValueOptionData)devicesDropdown.options[devicesDropdown.value];
            try
            {
                ((BleSock.GuestPeer)mPeer).Connect(optionData.value);
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
        });

        // Test

        testObject.SetActive(false);

        sizesDropdown.interactable = false;
        for (int i = 8; i < 13; ++i)
        {
            int size = 1 << i;
            sizesDropdown.options.Add(new ValueOptionData(size.ToString(), size));
        }
        sizesDropdown.value = 0;

        testButton.interactable = false;
        testButton.onClick.AddListener(() =>
        {
            int size = ((ValueOptionData)sizesDropdown.options[sizesDropdown.value]).value;
            var bytes = new byte[size];

            mRandom.GetBytes(bytes);

            bytes[0] = 0;

            try
            {
                mPeer.Send(bytes, bytes.Length, BleSock.Address.Others);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("エラー");
                return;
            }

            Log("スピード計測中..");
            mStartTime = Environment.TickCount;
            sizesDropdown.interactable = false;
            testButton.interactable = false;
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
            connectObject.SetActive(false);
            testObject.SetActive(false);
        });
    }

    private void OnReceive(byte[] message, int messageSize, BleSock.Player sender)
    {
        if (message[0] == 0)
        {
            message[0] = 1;

            try
            {
                mPeer.Send(message, message.Length, BleSock.Address.Others);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("エラー");
                return;
            }
        }
        else
        {
            Log("計測が完了しました: {0} bytes/sec", (float)messageSize * 1000 / (Environment.TickCount - mStartTime));
            sizesDropdown.interactable = true;
            testButton.interactable = true;
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
