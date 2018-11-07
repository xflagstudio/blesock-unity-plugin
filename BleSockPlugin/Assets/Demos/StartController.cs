using System;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StartController : MonoBehaviour
{
    [Serializable]
    public class ButtonInfo
    {
        public Button button;
        public string sceneName;
    }

    public ButtonInfo[] buttonInfos;


    private void Start()
    {
        foreach (var info in buttonInfos)
        {
            info.button.onClick.AddListener(() =>
            {
                SceneManager.LoadScene(info.sceneName);
            });
        }
    }
}
