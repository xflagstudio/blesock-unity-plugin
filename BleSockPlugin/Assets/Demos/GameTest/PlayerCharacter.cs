using UnityEngine;
using UnityEngine.UI;

public class PlayerCharacter : MonoBehaviour
{
    public const float RADIUS = 40;
    public const float VELOCITY_MAX = 300;
    public const float ACCELERATION = 600;
    public const float DEACCELERATION = 300;
    public const float DEAD_ALPHA = .2f;
    public const int KILL_SCORE = 100;


    public Image baseImage;
    public Text nameText;
    public Text scoreText;

    public int playerId;

    public Vector2 velocity;
    public float rotation;
    public bool accelerating;

    public bool alive;
    public int score;


    public RectTransform rectTransform
    {
        get
        {
            return (RectTransform)transform;
        }
    }

    public Vector2 position
    {
        get
        {
            return rectTransform.anchoredPosition;
        }

        set
        {
            rectTransform.anchoredPosition = value;
        }
    }

    public Vector2 forward
    {
        get
        {
            return baseImage.rectTransform.up;
        }
    }


    private void Update()
    {
        scoreText.text = score.ToString("n0");
        baseImage.rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);

        if (!alive)
        {
            return;
        }

        if (accelerating)
        {
            velocity += forward * (ACCELERATION * Time.deltaTime);

            float magnitude = velocity.magnitude;
            if (magnitude > VELOCITY_MAX)
            {
                velocity = velocity * (VELOCITY_MAX / magnitude);
            }
        }
        else
        {
            float magnitude = velocity.magnitude;

            if (magnitude > Mathf.Epsilon)
            {
                velocity = velocity * (Mathf.Max(magnitude - DEACCELERATION * Time.deltaTime, 0) / magnitude);
            }
        }

        position = position + velocity * Time.deltaTime;
    }

    public void Setup(int playerId, string name, Color color)
    {
        this.playerId = playerId;
        nameText.text = name;

        Color col = color;
        col.a = DEAD_ALPHA;
        baseImage.color = col;

        velocity = Vector2.zero;
        rotation = 0;
        accelerating = false;
        alive = false;
        score = 0;

        gameObject.SetActive(true);
    }

    public void Spawn(Vector2 position)
    {
        this.position = position;

        Color col = baseImage.color;
        col.a = 1;
        baseImage.color = col;

        alive = true;
    }

    public void Die()
    {
        Color col = baseImage.color;
        col.a = DEAD_ALPHA;
        baseImage.color = col;

        velocity = Vector2.zero;
        accelerating = false;
        alive = false;
    }

}
