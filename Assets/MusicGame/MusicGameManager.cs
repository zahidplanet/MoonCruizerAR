using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MusicGameManager : MonoBehaviour
{
    // Start is called before the first frame update

    public int[] GameNotes = new int[16];
    public int[] PlayerNotes = new int[16];
    public int currentNote = 0;
    public int notePlayed = 10;
    public GameObject RewardObject;

    void Start()
    {
        
    }

    public void PlayNote(int notePlayed )
    {

        Debug.Log("a");
        if(notePlayed == GameNotes[currentNote])
        {
            Debug.Log("b");
         
            if(currentNote >= 3) RewardObject.SetActive(true);
           PlayerNotes[currentNote] = notePlayed;
            currentNote++;

        }
        else
        {
            Debug.Log("c");
            currentNote = 0;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
