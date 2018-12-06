using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using UnityEngine;
using UnityEngine.UI;

public class GameTest : MonoBehaviour
{
    public GameObject modeSelectObject;
    public InputField playerNameInputField;
    public Button hostButton;
    public Button joinButton;

    public GameObject joinObject;
    public Dropdown devicesDropdown;
    public Button connectButton;
    public Button cancelButton;

    public Text logsText;

    public RectTransform charactersRoot;
    public PlayerCharacter playerCharacterOriginal;
    public Bullet bulletOriginal;


    private const string PROTOCOL_IDENTIFIER = "GameTest";

    private const float FIELD_X = 300;
    private const float FIELD_Y = 400;
    private const float SHOOT_INTERVAL = .3f;
    private const float SYNC_INTERVAL = .1f;
    private const float REVIVE_TIME = 3;

    private const byte MSG_SPAWN    = 0;
    private const byte MSG_SYNC     = 1;
    private const byte MSG_SHOOT    = 2;
    private const byte MSG_DIE      = 3;
    private const byte MSG_REVIVE   = 4;


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
    private BleSock.MessageBuffer mSendBuffer = new BleSock.MessageBuffer();
    private BleSock.MessageBuffer mReceiveBuffer = new BleSock.MessageBuffer();

    private List<PlayerCharacter> mPlayerCharacters = new List<PlayerCharacter>();
    private List<Bullet> mBullets = new List<Bullet>();

    private float mMyColor;
    private PlayerCharacter mMyPlayerCharacter;
    private float mShootTimer = 0;
    private int mNextBulletId = 1;
    private float mSyncTimer = 0;
    private float mReviveTimer = 0;

    private List<string> mLogs = new List<string>();


    private void Start()
    {
        UnityEngine.Random.InitState(Environment.TickCount);

        // ModeSelect

        modeSelectObject.SetActive(true);
        playerNameInputField.onEndEdit.AddListener((name) =>
        {
            bool interactable = !string.IsNullOrEmpty(name);
            hostButton.interactable = interactable;
            joinButton.interactable = interactable;
        });

        hostButton.interactable = false;
        hostButton.onClick.AddListener(() =>
        {
            modeSelectObject.SetActive(false);

            InitHost();
        });

        joinButton.interactable = false;
        joinButton.onClick.AddListener(() =>
        {
            modeSelectObject.SetActive(false);
            joinObject.SetActive(true);

            devicesDropdown.interactable = false;
            connectButton.interactable = false;

            InitJoin();
        });

        joinObject.SetActive(false);

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
        });

        cancelButton.onClick.AddListener(() =>
        {
            if (mPeer != null)
            {
                mPeer.Dispose();
                mPeer = null;
            }

            modeSelectObject.SetActive(true);
            joinObject.SetActive(false);
        });
    }

    private void Update()
    {
        if ((mMyPlayerCharacter != null) && (mPeer != null))
        {
            UpdateMyCharacter();
        }

        UpdateBullets();
    }

    private void WriteFloat(float value)
    {
        mSendBuffer.Write(Mathf.FloatToHalf(value));
    }

    private void WriteVector2(Vector2 value)
    {
        WriteFloat(value.x);
        WriteFloat(value.y);
    }

    private void UpdateMyCharacter()
    {
        if (!mMyPlayerCharacter.alive) // 復活
        {
            mReviveTimer += Time.deltaTime;

            if (mReviveTimer >= REVIVE_TIME)
            {
                mMyPlayerCharacter.Spawn(GetPlayerSpawnPosition());

                // MSG_REVIVE

                mSendBuffer.Clear();
                mSendBuffer.Write(MSG_REVIVE);
                WriteVector2(mMyPlayerCharacter.position);

                mPeer.Send(mSendBuffer.RawData, mSendBuffer.Size, BleSock.Address.Others);
            }

            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            StartCoroutine(TouchDown());
        }

        // 弾に対し総当たりで衝突判定（よい子はまねしてはいけない）

        foreach (var bullet in mBullets)
        {
            if (bullet.playerId == mMyPlayerCharacter.playerId)
            {
                continue;
            }

            float sqrMagnitude = (bullet.position - mMyPlayerCharacter.position).sqrMagnitude;
            if (sqrMagnitude > (PlayerCharacter.RADIUS + Bullet.RADIUS) * (PlayerCharacter.RADIUS + Bullet.RADIUS))
            {
                continue;
            }

            mBullets.Remove(bullet);
            Destroy(bullet.gameObject);

            mMyPlayerCharacter.Die();
            mReviveTimer = 0;

            var killer = mPeer.Players.Where(player => player.PlayerId == bullet.playerId).FirstOrDefault();
            if (killer != null)
            {
                Log("{0}に倒された", killer.PlayerName);
            }

            var killerCharacter = mPlayerCharacters.Where(pc => pc.playerId == bullet.playerId).FirstOrDefault();
            if (killerCharacter != null)
            {
                killerCharacter.score += PlayerCharacter.KILL_SCORE;
            }

            // MSG_DIE

            mSendBuffer.Clear();
            mSendBuffer.Write(MSG_DIE);
            mSendBuffer.Write(bullet.playerId);
            mSendBuffer.Write(bullet.bulletId);

            mPeer.Send(mSendBuffer.RawData, mSendBuffer.Size, BleSock.Address.Others);

            break;
        }

        // 壁と衝突判定

        Vector2 position = mMyPlayerCharacter.position;

        if (position.x < -FIELD_X + PlayerCharacter.RADIUS)
        {
            position.x = -FIELD_X + PlayerCharacter.RADIUS;
            mMyPlayerCharacter.velocity.x = -mMyPlayerCharacter.velocity.x;
        }

        if (position.x > FIELD_X - PlayerCharacter.RADIUS)
        {
            position.x = FIELD_X - PlayerCharacter.RADIUS;
            mMyPlayerCharacter.velocity.x = -mMyPlayerCharacter.velocity.x;
        }

        if (position.y < -FIELD_Y + PlayerCharacter.RADIUS)
        {
            position.y = -FIELD_Y + PlayerCharacter.RADIUS;
            mMyPlayerCharacter.velocity.y = -mMyPlayerCharacter.velocity.y;
        }

        if (position.y > FIELD_Y - PlayerCharacter.RADIUS)
        {
            position.y = FIELD_Y - PlayerCharacter.RADIUS;
            mMyPlayerCharacter.velocity.y = -mMyPlayerCharacter.velocity.y;
        }

        mMyPlayerCharacter.position = position;

        // 他プレイヤーへ同期

        mSyncTimer += Time.deltaTime;
        if (mSyncTimer >= SYNC_INTERVAL)
        {
            mSyncTimer -= SYNC_INTERVAL;

            // MSG_SYNC

            mSendBuffer.Clear();
            mSendBuffer.Write(MSG_SYNC);
            WriteVector2(mMyPlayerCharacter.position);
            WriteVector2(mMyPlayerCharacter.velocity);
            WriteFloat(mMyPlayerCharacter.rotation);
            mSendBuffer.Write(mMyPlayerCharacter.accelerating);

            mPeer.Send(mSendBuffer.RawData, mSendBuffer.Size, BleSock.Address.Others);
        }
    }

    private IEnumerator TouchDown()
    {
        mMyPlayerCharacter.accelerating = true;

        Vector3 lastPosition = charactersRoot.InverseTransformPoint(Input.mousePosition);

        while (mMyPlayerCharacter.alive && (mPeer != null) && Input.GetMouseButton(0)) // 自キャラ操作中
        {
            Vector3 position = charactersRoot.InverseTransformPoint(Input.mousePosition);
            mMyPlayerCharacter.rotation -= position.x - lastPosition.x;

            lastPosition = position;

            mShootTimer += Time.deltaTime;
            if (mShootTimer >= SHOOT_INTERVAL)
            {
                mShootTimer -= SHOOT_INTERVAL;

                var bullet = SpawnBullet(
                    mMyPlayerCharacter.playerId,
                    mNextBulletId++,
                    mMyPlayerCharacter.position + mMyPlayerCharacter.forward * PlayerCharacter.RADIUS,
                    mMyPlayerCharacter.forward * Bullet.VELOCITY,
                    mMyPlayerCharacter.baseImage.color);

                // MSG_SHOOT

                mSendBuffer.Clear();
                mSendBuffer.Write(MSG_SHOOT);
                mSendBuffer.Write(bullet.bulletId);
                WriteVector2(bullet.position);
                WriteVector2(bullet.velocity);

                mPeer.Send(mSendBuffer.RawData, mSendBuffer.Size, BleSock.Address.Others);
            }

            yield return null;
        }

        mMyPlayerCharacter.accelerating = false;
        mShootTimer = 0;
    }

    private void UpdateBullets()
    {
        int index = 0;
        while (index < mBullets.Count)
        {
            var bullet = mBullets[index];
            Vector2 position = bullet.position;

            if ((position.x < -FIELD_X - Bullet.RADIUS) ||
                (position.x > FIELD_X + Bullet.RADIUS) ||
                (position.y < -FIELD_Y - Bullet.RADIUS) ||
                (position.y > FIELD_Y + Bullet.RADIUS))
            {
                mBullets.RemoveAt(index);
                Destroy(bullet.gameObject);

                continue;
            }

            ++index;
        }
    }

    private Vector2 GetPlayerSpawnPosition()
    {
        while (true)
        {
            Vector2 position = new Vector2(
                UnityEngine.Random.Range(-FIELD_X + PlayerCharacter.RADIUS, FIELD_X - PlayerCharacter.RADIUS),
                UnityEngine.Random.Range(-FIELD_Y + PlayerCharacter.RADIUS, FIELD_Y - PlayerCharacter.RADIUS));
            bool pass = true;

            foreach (var playerCharacter in mPlayerCharacters)
            {
                float sqrMagnitude = (playerCharacter.position - position).sqrMagnitude;
                if (sqrMagnitude <= PlayerCharacter.RADIUS * PlayerCharacter.RADIUS * 4)
                {
                    pass = false;
                    break;
                }
            }

            if (!pass)
            {
                continue;
            }

            foreach (var bullet in mBullets)
            {
                float sqrMagnitude = (bullet.position - position).sqrMagnitude;
                if (sqrMagnitude <= PlayerCharacter.RADIUS * PlayerCharacter.RADIUS * 4)
                {
                    pass = false;
                    break;
                }
            }

            if (!pass)
            {
                continue;
            }

            return position;
        }
    }

    private Bullet SpawnBullet(int playerId, int bulletId, Vector2 position, Vector2 direction, Color color)
    {
        var bullet = Instantiate(bulletOriginal, charactersRoot, false);
        bullet.Spawn(playerId, bulletId, position, direction, color);

        mBullets.Add(bullet);

        return bullet;
    }

    private void OnDestroy()
    {
        if (mPeer != null)
        {
            mPeer.Dispose();
            mPeer = null;
        }
    }

    private void InitHost()
    {
        var host = new BleSock.HostPeer();

        host.onBluetoothRequire += () =>
        {
            Log("Bluetoothを有効にしてください");
        };

        host.onReady += () =>
        {
            Log("初期化が完了しました");

            AddMyPlayerCharacter();

            try
            {
                host.StartAdvertising(playerNameInputField.text);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Log("アドバタイズ開始できません");
            }
        };

        host.onFail += () =>
        {
            Log("失敗しました");
        };

        host.onPlayerJoin += OnPlayerJoin;
        host.onPlayerLeave += OnPlayerLeave;
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
    }

    private void InitJoin()
    {
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

            joinObject.SetActive(false);

            AddMyPlayerCharacter();
        };

        guest.onDisconnect += () =>
        {
            Log("切断されました");

            mPeer.Dispose();
            mPeer = null;
        };

        guest.onPlayerJoin += OnPlayerJoin;
        guest.onPlayerLeave += OnPlayerLeave;
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
    }

    private void AddMyPlayerCharacter()
    {
        mMyColor = UnityEngine.Random.value;

        mMyPlayerCharacter = Instantiate(playerCharacterOriginal, charactersRoot, false);
        mMyPlayerCharacter.Setup(mPeer.LocalPlayerId, "<color=red>あなた</color>", Color.HSVToRGB(mMyColor, 1, 1));
        mMyPlayerCharacter.Spawn(GetPlayerSpawnPosition());

        mPlayerCharacters.Add(mMyPlayerCharacter);

        SendSpawn(BleSock.Address.Others);
    }

    private void SendSpawn(int receiver)
    {
        if (mPeer == null)
        {
            return;
        }

        // MSG_SPAWN

        mSendBuffer.Clear();
        mSendBuffer.Write(MSG_SPAWN);
        WriteFloat(mMyColor);
        WriteVector2(mMyPlayerCharacter.position);
        WriteVector2(mMyPlayerCharacter.velocity);
        WriteFloat(mMyPlayerCharacter.rotation);
        mSendBuffer.Write(mMyPlayerCharacter.accelerating);
        mSendBuffer.Write(mMyPlayerCharacter.alive);
        mSendBuffer.Write(mMyPlayerCharacter.score);

        mPeer.Send(mSendBuffer.RawData, mSendBuffer.Size, receiver);
    }

    private void OnPlayerJoin(BleSock.Player player)
    {
        Log("{0} が参加しました", player.PlayerName);

        SendSpawn(player.PlayerId);
    }

    private void OnPlayerLeave(BleSock.Player player)
    {
        Log("{0} が離脱しました", player.PlayerName);

        var playerCharacter = mPlayerCharacters.Where(pc => pc.playerId == player.PlayerId).FirstOrDefault();
        if (playerCharacter != null)
        {
            mPlayerCharacters.Remove(playerCharacter);
            Destroy(playerCharacter.gameObject);
        }
    }

    private void OnReceive(byte[] message, int messageSize, BleSock.Player sender)
    {
        mReceiveBuffer.Clear();
        mReceiveBuffer.WriteBytes(message, 0, messageSize);
        mReceiveBuffer.Seek(0);

        switch (mReceiveBuffer.ReadByte())
        {
            case MSG_SPAWN:
                OnReceiveSpawn(sender);
                break;

            case MSG_SYNC:
                OnReceiveSync(sender);
                break;

            case MSG_SHOOT:
                OnReceiveShoot(sender);
                break;

            case MSG_DIE:
                OnReceiveDie(sender);
                break;

            case MSG_REVIVE:
                OnReceiveRevive(sender);
                break;
        }
    }

    private float ReadFloat()
    {
        return Mathf.HalfToFloat(mReceiveBuffer.ReadUInt16());
    }

    private Vector2 ReadVector2()
    {
        float x = ReadFloat();
        float y = ReadFloat();

        return new Vector2(x, y);
    }

    private void OnReceiveSpawn(BleSock.Player sender)
    {
        var playerCharacter = Instantiate(playerCharacterOriginal, charactersRoot, false);
        playerCharacter.Setup(sender.PlayerId, sender.PlayerName, Color.HSVToRGB(ReadFloat(), 1, 1));
        playerCharacter.Spawn(ReadVector2());
        playerCharacter.velocity = ReadVector2();
        playerCharacter.rotation = ReadFloat();
        playerCharacter.accelerating = mReceiveBuffer.ReadBoolean();
        playerCharacter.alive = mReceiveBuffer.ReadBoolean();
        playerCharacter.score = mReceiveBuffer.ReadInt32();

        mPlayerCharacters.Add(playerCharacter);
    }

    private void OnReceiveSync(BleSock.Player sender)
    {
        var playerCharacter = mPlayerCharacters.Where(pc => pc.playerId == sender.PlayerId).FirstOrDefault();
        if (playerCharacter != null)
        {
            playerCharacter.position = ReadVector2();
            playerCharacter.velocity = ReadVector2();
            playerCharacter.rotation = ReadFloat();
            playerCharacter.accelerating = mReceiveBuffer.ReadBoolean();
        }
    }

    private void OnReceiveShoot(BleSock.Player sender)
    {
        var playerCharacter = mPlayerCharacters.Where(pc => pc.playerId == sender.PlayerId).FirstOrDefault();
        if (playerCharacter != null)
        {
            int bulletId = mReceiveBuffer.ReadInt32();
            Vector2 position = ReadVector2();
            Vector2 velocity = ReadVector2();
            SpawnBullet(sender.PlayerId, bulletId, position, velocity, playerCharacter.baseImage.color);
        }
    }

    private void OnReceiveDie(BleSock.Player sender)
    {
        var playerCharacter = mPlayerCharacters.Where(pc => pc.playerId == sender.PlayerId).FirstOrDefault();
        if (playerCharacter != null)
        {
            playerCharacter.Die();
        }

        int killerPlayerId = mReceiveBuffer.ReadInt32();
        int bulletId = mReceiveBuffer.ReadInt32();

        if (killerPlayerId == mPeer.LocalPlayerId)
        {
            Log("{0}を倒した", sender.PlayerName);
        }

        var killerCharacter = mPlayerCharacters.Where(pc => pc.playerId == killerPlayerId).FirstOrDefault();
        if (killerCharacter != null)
        {
            killerCharacter.score += PlayerCharacter.KILL_SCORE;
        }

        var bullet = mBullets.Where(b => (b.playerId == killerPlayerId) && (b.bulletId == bulletId)).FirstOrDefault();
        if (bullet != null)
        {
            mBullets.Remove(bullet);
            Destroy(bullet.gameObject);
        }
    }

    private void OnReceiveRevive(BleSock.Player sender)
    {
        var playerCharacter = mPlayerCharacters.Where(pc => pc.playerId == sender.PlayerId).FirstOrDefault();
        if (playerCharacter != null)
        {
            playerCharacter.Spawn(ReadVector2());
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
