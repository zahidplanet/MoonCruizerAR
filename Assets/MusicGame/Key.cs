using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Key : MonoBehaviour
{
    public AudioSource audioSource;
    public MusicGameManager gameManager;
    public int myNote = 10;

    // Start is called before the first frame update
    void Start()
    {
        audioSource = this.GetComponent<AudioSource>(); 
    }

    private void OnCollisionEnter(Collision collision)
    {
        Destroy(collision.gameObject);
        gameManager.PlayNote(myNote);
        audioSource.Play();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
