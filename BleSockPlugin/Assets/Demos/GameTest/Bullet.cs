using UnityEngine;
using UnityEngine.UI;

public class Bullet : MonoBehaviour
{
    public const float RADIUS = 10;
    public const float VELOCITY = 600;


    public Image baseImage;

    public int playerId;
    public int bulletId;
    public Vector2 velocity;


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
    }


    private void Update()
    {
        rectTransform.anchoredPosition = position + velocity * Time.deltaTime;
    }

    public void Spawn(int playerId, int bulletId, Vector2 position, Vector2 velocity, Color color)
    {
        this.playerId = playerId;
        this.bulletId = bulletId;
        rectTransform.anchoredPosition = position;
        this.velocity = velocity;
        baseImage.color = color;

        gameObject.SetActive(true);
    }
}
